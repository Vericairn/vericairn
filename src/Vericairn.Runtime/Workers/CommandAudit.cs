// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// Observes recognizable invocation inputs, not an agent's prose. This is a conservative command audit, not a shell interpreter
/// or a network sandbox. Only fixed summaries and hashes leave the local run state.
/// </summary>
public static partial class CommandAudit
{
    private static readonly HashSet<string> NetworkCommands = new(StringComparer.OrdinalIgnoreCase)
        { "curl", "wget", "ssh", "scp", "sftp", "ftp", "telnet", "nc", "ncat", "netcat", "ping", "dig", "nslookup", "gh", "aws", "az", "gcloud", "Invoke-WebRequest", "Invoke-RestMethod", "iwr", "irm" };
    private static readonly HashSet<string> MutatingCommands = new(StringComparer.OrdinalIgnoreCase)
        { "cp", "mv", "rm", "touch", "mkdir", "rmdir", "ln", "tee", "truncate", "chmod", "chown", "Set-Content", "Add-Content", "Clear-Content", "Copy-Item", "Move-Item", "Remove-Item", "New-Item", "Rename-Item", "Set-Item", "Clear-Item", "Set-Acl" };

    private static readonly HashSet<string> ReadCommands = new(StringComparer.OrdinalIgnoreCase)
        { "cat", "head", "tail", "ls", "pwd", "rg", "grep", "find", "wc", "stat", "nl", "sed", "echo", "printf", "Get-Content", "Get-ChildItem", "Get-Location" };
    private static readonly HashSet<string> GitReadCommands = new(StringComparer.Ordinal)
        { "show", "log", "diff", "blame", "cat-file", "ls-tree", "ls-files", "rev-parse", "status" };
    private static readonly HashSet<string> GitMessageCommands = new(StringComparer.Ordinal)
        { "commit", "tag", "merge", "revert", "notes", "stash" };

    private enum PathAccess { Read, Write, Unknown }
    private sealed record InvocationPath(string Path, PathAccess Access);
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>();

    /// <summary>A stable action fingerprint, independent of event id and JSON property order.</summary>
    public static string Fingerprint(HarnessAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ContentHash.Sha256(Encoding.UTF8.GetBytes(action.Tool + "\n" + CanonicalJson.Serialize(action.Input)));
    }

    /// <summary>An uncanonicalizable input cannot support deduplication, but its other checks still apply.</summary>
    internal static string? TryFingerprint(HarnessAction action)
    {
        try
        {
            return Fingerprint(action);
        }
        catch (Exception exception) when (CheckFailure(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Flags recognizable network, credential and outside-workspace path attempts without retaining their arguments.
    /// A failed check adds an audit-unverified warning without discarding the action's completed verdicts.
    /// </summary>
    public static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted) =>
        Inspect(action, workspace, permitted, NoEnvironment, IsReadOnly);

    internal static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted, Func<string, bool> isReadOnly) =>
        Inspect(action, workspace, permitted, NoEnvironment, isReadOnly);

    internal static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted, IReadOnlyDictionary<string, string> environment) =>
        Inspect(action, workspace, permitted, environment, IsReadOnly);

    private static RunSignal[] Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted, IReadOnlyDictionary<string, string> environment, Func<string, bool> isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(action);
        // Retain the public argument; actual changes are checked against these patterns at settlement.
        ArgumentNullException.ThrowIfNull(permitted);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var paths = new List<InvocationPath>();
        bool readOnly = action.Tool is "Read" or "Glob" or "Grep" or "view_image";
        PathAccess structured = readOnly ? PathAccess.Read : action.Tool is "Write" or "Edit" or "MultiEdit" or "NotebookEdit" or "apply_patch" or "file_change" ? PathAccess.Write : PathAccess.Unknown;
        bool sawCommand = false;
        bool interpretable = false;
        Check(() => interpretable = action.Input.GetRawText().Length <= 65536 && action.Input.ValueKind == JsonValueKind.Object, codes);
        if (!interpretable)
        {
            codes.Add("audit-unverified");
        }
        else
        {
            Check(() => GatherPaths(action.Input, paths, structured), codes);
            if (action.Tool is "WebFetch" or "WebSearch" or "web_search" || action.Tool.Contains("__web", StringComparison.OrdinalIgnoreCase))
            {
                codes.Add("network");
            }

            foreach (string field in new[] { "url", "uri" })
            {
                Check(() =>
                {
                    if (action.Input.TryGetProperty(field, out JsonElement url) && url.ValueKind == JsonValueKind.String
                        && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? address) && address.Scheme is "http" or "https" or "ftp" or "sftp" or "ssh" or "ws" or "wss")
                    {
                        codes.Add("network");
                    }
                }, codes);
            }

            foreach (string field in new[] { "command", "cmd" })
            {
                string? command = null;
                Check(() => command = StringField(action.Input, field), codes);
                if (command is not null)
                {
                    bool commandReads = false;
                    Check(() => commandReads = InspectCommand(command, paths, codes, isReadOnly, 0), codes);
                    readOnly = (!sawCommand || readOnly) && commandReads && paths.All(p => p.Access == PathAccess.Read);
                    sawCommand = true;
                }
            }

            Check(() =>
            {
                if (StringField(action.Input, "patch") is string patch)
                {
                    foreach (Match match in PatchPaths().Matches(patch))
                    {
                        paths.Add(new InvocationPath(match.Groups[1].Value, PathAccess.Write));
                    }
                }
            }, codes);

            string? working = workspace;
            foreach (string field in new[] { "cwd", "workdir", "working_directory" })
            {
                string? cwd = null;
                if (!Check(() => cwd = StringField(action.Input, field), codes))
                {
                    working = null;
                }
                else if (cwd is not null)
                {
                    // A failed resolution leaves no base for relative paths. A failed permission check does not
                    // erase a successfully resolved base, so other paths can still be checked independently.
                    working = null;
                    Check(() => working = Path.GetFullPath(ExpandTemporaryPath(cwd, environment), workspace), codes);
                    if (working is not null)
                    {
                        Check(() =>
                        {
                            // The worktree contains local changes; settlement checks them against the brief.
                            // During execution, only crossing its boundary requires scope attention.
                            if (!InsideBoundary(working, workspace, environment))
                            {
                                // The working directory alone does not attribute an outside change.
                                codes.Add(readOnly ? "scope-read" : "audit-unverified");
                            }
                        }, codes);
                    }
                }
            }

            foreach (InvocationPath invocation in paths)
            {
                string path = invocation.Path;
                Check(() =>
                {
                    if (SecretPath().IsMatch(path.Replace('\\', '/')))
                    {
                        codes.Add("credentials");
                    }
                }, codes);
                Check(() =>
                {
                    string normalized = ExpandTemporaryPath(path, environment).Replace('\\', '/');
                    if (normalized.IndexOfAny(['~', '$', '`']) >= 0)
                    {
                        // An unresolved expansion cannot establish which boundary a write would cross.
                        codes.Add("audit-unverified");
                        return;
                    }
                    if (working is null && !Path.IsPathFullyQualified(normalized))
                    {
                        codes.Add("audit-unverified");
                        return;
                    }

                    string resolved = Path.IsPathFullyQualified(normalized) ? Path.GetFullPath(normalized) : Path.GetFullPath(normalized, working!);
                    if (!InsideBoundary(resolved, workspace, environment))
                    {
                        codes.Add(PossiblyInsideBoundary(resolved, workspace, environment) ? "audit-unverified" : invocation.Access switch
                        {
                            PathAccess.Read => "scope-read",
                            PathAccess.Write => "scope",
                            _ => "audit-unverified",
                        });
                    }
                }, codes);
            }
        }

        string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        string? fingerprint = null;
        Check(() => fingerprint = Fingerprint(action), codes);
        return codes.Order(StringComparer.Ordinal).Select(code => new RunSignal(code, code is "credentials" or "scope" ? "critical" : "warning", at, code switch
        {
            "network" => "The worker observed an invocation that attempts network access.",
            "credentials" => "The worker observed an invocation targeting credentials or a secret store.",
            "scope" => "The worker observed an invocation that may change a path outside its workspace.",
            "scope-read" => "The worker observed an invocation that may read outside its workspace.",
            _ => "The worker could not complete one or more invocation checks; completed audit verdicts still apply.",
        }, fingerprint)).ToArray();
    }

    private static string? StringField(JsonElement input, string name) =>
        input.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<string> TemporaryNames => OperatingSystem.IsWindows() ? ["TMPDIR", "TEMP", "TMP"] : ["TMPDIR"];

    private static bool InsideBoundary(string path, string workspace, IReadOnlyDictionary<string, string> environment) =>
        PermittedPaths.Contains(path, workspace) || TemporaryNames.Any(name => environment.TryGetValue(name, out string? root)
            && Path.IsPathFullyQualified(root) && PermittedPaths.Contains(path, root));

    private static bool PossiblyInsideBoundary(string path, string workspace, IReadOnlyDictionary<string, string> environment) =>
        LexicallyInside(path, workspace) || TemporaryNames.Any(name => environment.TryGetValue(name, out string? root)
            && Path.IsPathFullyQualified(root) && LexicallyInside(path, root));

    private static bool LexicallyInside(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    private static string ExpandTemporaryPath(string path, IReadOnlyDictionary<string, string> environment)
    {
        // Only the environment actually passed at launch supplies a temporary boundary. No ambient
        // lookup, default system temporary directory, or path claimed in a tool event can grant one.
        foreach (string name in TemporaryNames)
        {
            if (!environment.TryGetValue(name, out string? root) || !Path.IsPathFullyQualified(root)) continue;
            foreach (string prefix in new[] { "$" + name, "${" + name + "}", "$env:" + name, "%" + name + "%" })
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal) && (path.Length == prefix.Length || path[prefix.Length] is '/' or '\\'))
                    return root.TrimEnd('/', '\\') + path[prefix.Length..];
            }
        }

        return path;
    }

    private static bool Check(Action check, HashSet<string> codes)
    {
        try
        {
            check();
            return true;
        }
        catch (Exception exception) when (CheckFailure(exception))
        {
            codes.Add("audit-unverified");
            return false;
        }
    }

    private static bool CheckFailure(Exception exception) => exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or CanonicalJsonException
        or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or TimeoutException;

    private static void GatherPaths(JsonElement value, List<InvocationPath> paths, PathAccess access)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name is "path" or "file_path" or "notebook_path" or "target_file")
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        paths.Add(new InvocationPath(property.Value.GetString()!, access));
                    }
                }
                else if (property.Name is "changes" or "paths" or "files")
                {
                    GatherPaths(property.Value, paths, property.Name == "changes" ? PathAccess.Write : access);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    paths.Add(new InvocationPath(item.GetString()!, access));
                }
                else
                {
                    GatherPaths(item, paths, access);
                }
            }
        }
    }

    private static bool InspectCommand(string command, List<InvocationPath> paths, HashSet<string> codes, Func<string, bool> isReadOnly, int depth)
    {
        if (depth > 3)
        {
            codes.Add("audit-unverified");
            return false;
        }

        ShellToken[] shellTokens = [];
        if (!Check(() => shellTokens = ShellTokens(command, codes), codes))
        {
            return false;
        }

        string[] words = shellTokens.Select(t => t.Text).ToArray();
        string[] tokens = words.Select(w => w.Trim('"', '\'')).ToArray();
        bool first = true;
        bool redirect = false;
        bool inputRedirect = false;
        bool descriptorRedirect = false;
        bool reads = false;
        bool allReadOnly = true;
        string? wrapper = null;
        string executable = "";
        string? gitCommand = null;
        int gitCommandIndex = -1;
        bool textArgument = false;
        bool gitPathsOnly = false;
        int operand = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            bool quoted = words[i].StartsWith('"') || words[i].StartsWith('\'');
            if (shellTokens[i].Input is string input)
            {
                if (Interpreter(executable)) InspectCode(input, paths, codes);
                else if (shellTokens[i].ExpandInput)
                {
                    Check(() => { if (SecretEnvironment().IsMatch(input)) codes.Add("credentials"); }, codes);
                }

                continue;
            }
            Check(() =>
            {
                if (!words[i].StartsWith('\'') && SecretEnvironment().IsMatch(words[i]))
                {
                    codes.Add("credentials");
                }
            }, codes);

            if (!quoted && token is ";" or "&&" or "||" or "|" or "&" or "\n" or "\r\n")
            {
                first = true;
                wrapper = null;
                reads = false;
                textArgument = false;
                gitPathsOnly = false;
                continue;
            }

            if (!quoted && (token.Contains('>', StringComparison.Ordinal) || token == "<"))
            {
                redirect = true;
                inputRedirect = token == "<";
                descriptorRedirect = token.Contains('&', StringComparison.Ordinal);
                allReadOnly &= inputRedirect;
                continue;
            }

            if (redirect)
            {
                if (!descriptorRedirect || !(token == "-" || token.All(char.IsAsciiDigit)))
                {
                    paths.Add(new InvocationPath(token, inputRedirect ? PathAccess.Read : PathAccess.Write));
                }

                redirect = false;
                continue;
            }

            if (textArgument)
            {
                // Message/content text is not a path. Secret environment expansion was still checked above.
                textArgument = false;
                continue;
            }

            if (first)
            {
                if (wrapper is not null && token.StartsWith('-'))
                {
                    if (token is "-u" or "--user" or "-g" or "--group" or "--unset" or "-C" or "--chdir" && i + 1 < tokens.Length)
                    {
                        string argument = tokens[++i];
                        if (token is "-C" or "--chdir")
                        {
                            paths.Add(new InvocationPath(argument, PathAccess.Unknown));
                        }
                    }

                    continue;
                }

                if (token.Contains('=', StringComparison.Ordinal) || token is "sudo" or "env" or "command" or "exec")
                {
                    if (token is "sudo" or "env" or "command" or "exec")
                    {
                        wrapper = token;
                    }

                    if (token == "env" && i == tokens.Length - 1)
                    {
                        codes.Add("credentials");
                    }

                    continue;
                }

                executable = token.Replace('\\', '/').Split('/')[^1];
                executable = executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? executable[..^4] : executable;
                first = false;
                operand = 0;
                int end = i + 1;
                while (end < words.Length && !Separator(words[end])) end++;
                gitCommandIndex = executable.Equals("git", StringComparison.OrdinalIgnoreCase) ? GitSubcommand(tokens, i + 1, end) : -1;
                gitCommand = gitCommandIndex >= 0 ? tokens[gitCommandIndex] : null;
                reads = false;
                Check(() => reads = isReadOnly(string.Join(" ", words[i..end])), codes);
                bool shell = executable is "bash" or "sh" or "zsh" or "pwsh" or "powershell" or "cmd"
                    && i + 2 < end && tokens[i + 1] is "-c" or "-lc" or "-Command" or "/c";
                allReadOnly &= reads || shell;
                if (NetworkCommands.Contains(executable))
                {
                    codes.Add("network");
                }

                if (Interpreter(executable) && !shell) codes.Add("audit-unverified");

                if (executable is "printenv" or "cmdkey" or "secret-tool" or "Get-StoredCredential"
                    || executable == "security"
                    || executable == "op"
                    || executable == "pass"
                    || executable == "gpg" && command.Contains("--export-secret", StringComparison.Ordinal))
                {
                    codes.Add("credentials");
                }

                continue;
            }

            if (executable is "bash" or "sh" or "zsh" or "pwsh" or "powershell" or "cmd" && token is "-c" or "-lc" or "-Command" or "/c" && i + 1 < tokens.Length)
            {
                string nested = tokens[++i];
                bool nestedReads = false;
                Check(() => nestedReads = InspectCommand(nested, paths, codes, isReadOnly, depth + 1), codes);
                allReadOnly &= nestedReads;
                continue;
            }

            if (i == gitCommandIndex && token is "fetch" or "pull" or "push" or "clone" or "ls-remote"
                || executable is "npm" or "pnpm" or "yarn" or "pip" or "pip3" && token is "install" or "ci" or "add"
                || executable == "dotnet" && token == "restore")
            {
                codes.Add("network");
            }

            if (gitCommand is not null)
            {
                if (i > gitCommandIndex && token == "--") gitPathsOnly = true;
                if (i > gitCommandIndex && !gitPathsOnly && GitMessageCommands.Contains(gitCommand))
                {
                    if (token is "-m" or "--message")
                    {
                        textArgument = true;
                        continue;
                    }

                    if (token.StartsWith("--message=", StringComparison.Ordinal)) continue;
                }

                // A revision:path expression names an object in Git, not a filesystem operand.
                if (i > gitCommandIndex && !gitPathsOnly && GitReadCommands.Contains(gitCommand))
                {
                    if (!token.StartsWith('-') && GitObjectPath(token))
                    {
                        // Object names are not filesystem paths, but reading a credential object is still audited.
                        Check(() =>
                        {
                            if (SecretPath().IsMatch(token[(token.IndexOf(':', StringComparison.Ordinal) + 1)..].Replace('\\', '/')))
                                codes.Add("credentials");
                        }, codes);
                        continue;
                    }
                }

                if (token.Contains("credential", StringComparison.Ordinal)) codes.Add("credentials");
            }

            if (executable.Equals("git", StringComparison.OrdinalIgnoreCase) && !gitPathsOnly)
            {
                if (token is "-C" or "--work-tree" or "--output" && i + 1 < tokens.Length && !Separator(words[i + 1]))
                {
                    paths.Add(new InvocationPath(tokens[++i], PathAccess.Write));
                    allReadOnly = false;
                    continue;
                }

                string? target = token.StartsWith("--work-tree=", StringComparison.Ordinal) ? token["--work-tree=".Length..]
                    : token.StartsWith("--output=", StringComparison.Ordinal) ? token["--output=".Length..]
                    : token.StartsWith("-C", StringComparison.Ordinal) && token.Length > 2 ? token[2..] : null;
                if (target is not null)
                {
                    paths.Add(new InvocationPath(target.Trim('"', '\''), PathAccess.Write));
                    allReadOnly = false;
                    continue;
                }
            }

            if (Interpreter(executable) && i > 0 && tokens[i - 1] is "-c" or "-e")
            {
                InspectCode(token, paths, codes);
                continue;
            }

            if (executable.EndsWith("-Content", StringComparison.OrdinalIgnoreCase) && token.Equals("-Value", StringComparison.OrdinalIgnoreCase))
            {
                textArgument = true;
                continue;
            }

            if (executable is "rg" or "grep" && token is "-g" or "--glob" or "--iglob" or "-e" or "--regexp" or "-t" or "--type" or "-T" or "--type-not" && i + 1 < tokens.Length)
            {
                i++;
                continue;
            }

            if (token.StartsWith('-') || token.Contains("://", StringComparison.Ordinal) || executable is "echo" or "printf")
            {
                continue;
            }

            operand++;
            bool pattern = executable is "rg" or "grep" or "sed" or "awk" && operand == 1
                && !tokens.Contains("--files", StringComparer.Ordinal)
                && !tokens.Any(t => t is "-e" or "--regexp" or "-f" or "--file" || t.StartsWith("--regexp=", StringComparison.Ordinal));
            if (!pattern && ((reads && executable is not ("pwd" or "Get-Location")) || MutatingCommands.Contains(executable) || token.Contains('/', StringComparison.Ordinal) || token.Contains('\\', StringComparison.Ordinal)
                || token.Contains('.', StringComparison.Ordinal) || token.StartsWith('~') || token.Contains('$', StringComparison.Ordinal) || token.Contains('`', StringComparison.Ordinal)))
            {
                paths.Add(new InvocationPath(token, reads ? PathAccess.Read : MutatingCommands.Contains(executable) ? PathAccess.Write : PathAccess.Unknown));
            }
        }

        return allReadOnly;
    }

    private static bool Separator(string word) => word is ";" or "&&" or "||" or "|" or "&" or "\n" or "\r\n";

    private static bool Interpreter(string executable) => executable is "python" or "python3" or "node" or "ruby" or "perl" or "bash" or "sh" or "zsh" or "pwsh" or "powershell" or "cmd";

    private static void InspectCode(string code, List<InvocationPath> paths, HashSet<string> codes)
    {
        codes.Add("audit-unverified");
        Check(() => { if (NetworkCode().IsMatch(code)) codes.Add("network"); }, codes);
        Check(() => { if (SecretEnvironment().IsMatch(code)) codes.Add("credentials"); }, codes);
        Check(() =>
        {
            foreach (Match word in ShellWords().Matches(code))
            {
                string text = word.Value.Trim('"', '\'');
                if (NetworkCommands.Contains(text)) codes.Add("network");
                if (SecretPath().IsMatch(text.Replace('\\', '/'))) codes.Add("credentials");
            }
        }, codes);
        Check(() =>
        {
            foreach (Match match in CodePaths().Matches(code))
                paths.Add(new InvocationPath(match.Groups[1].Value, PathAccess.Unknown));
        }, codes);
    }

    private static int GitSubcommand(string[] tokens, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (tokens[i] is "-c" or "-C" or "--git-dir" or "--work-tree" or "--namespace" or "--config-env") i++;
            else if (!tokens[i].StartsWith('-')) return i;
        }

        return -1;
    }

    private static bool GitObjectPath(string token)
    {
        int colon = token.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && !Path.IsPathRooted(token)
            && !(colon == 1 && char.IsAsciiLetter(token[0]) && token.Length > 2 && token[2] is '/' or '\\');
    }

    private static bool IsReadOnly(string command)
    {
        string[] words = ShellWords().Matches(command).Select(m => m.Value).ToArray();
        string[] tokens = words.Select(w => w.Trim('"', '\'')).ToArray();
        string executable = tokens[0].Replace('\\', '/').Split('/')[^1];
        if (executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executable = executable[..^4];
        int gitCommand = executable.Equals("git", StringComparison.OrdinalIgnoreCase) ? GitSubcommand(tokens, 1, tokens.Length) : -1;
        if (!ReadCommands.Contains(executable) && !(gitCommand >= 0 && GitReadCommands.Contains(tokens[gitCommand]))) return false;
        if (executable.Equals("sed", StringComparison.OrdinalIgnoreCase)
            && !(tokens.Length >= 3 && tokens[1] == "-n" && SedPrint().IsMatch(tokens[2]))) return false;
        // These options execute helpers or write files. Unclassified forms keep the conservative path verdict.
        if (executable.Equals("find", StringComparison.OrdinalIgnoreCase) && tokens.Any(t => t is "-delete" or "-exec" or "-execdir" or "-ok" or "-okdir" or "-fprint" or "-fprint0" or "-fprintf" or "-fls")) return false;
        if (executable.Equals("rg", StringComparison.OrdinalIgnoreCase) && tokens.Any(t => t is "--pre" or "--hostname-bin" or "-z" or "--search-zip"
            || t.StartsWith("--pre=", StringComparison.Ordinal) || t.StartsWith("--hostname-bin=", StringComparison.Ordinal))) return false;
        if (gitCommand >= 0 && tokens.Skip(gitCommand + 1).TakeWhile(t => t != "--").Any(t => t is "--output" or "--ext-diff" or "--textconv" or "--filters"
            || t.StartsWith("--output=", StringComparison.Ordinal))) return false;
        return !words.Any(w => !w.StartsWith('\'') && (w.Contains("$(", StringComparison.Ordinal) || w.Contains('`', StringComparison.Ordinal)));
    }

    [GeneratedRegex("(?:\"(?:\\\\.|[^\"\\\\])*\"|'[^']*'|[^\\s;&|<>\"'])+|<<<|<<-|[;&|<>]+|\\r?\\n", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ShellWords();

    [GeneratedRegex(@"\A[0-9]+(?:,[0-9]+)?p\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SedPrint();

    [GeneratedRegex(@"(?:^|/)(?:\.ssh|\.aws|\.azure|\.gnupg|\.kube|\.docker|\.netrc|\.npmrc|\.pypirc|\.git-credentials|\.env(?:\.[^/]*)?|\.credentials(?:\.json)?|auth\.json|credentials(?:\.json)?|id_rsa|id_ed25519|Keychains|keyrings|Credentials)(?:/|$)|(?:^|/)\.config/(?:gcloud|gh)(?:/|$)|(?:^|/)run/secrets(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SecretPath();

    [GeneratedRegex(@"(?<!\\)\$(?:\{)?(?:AWS_SECRET_ACCESS_KEY|AWS_SESSION_TOKEN|OPENAI_API_KEY|ANTHROPIC_API_KEY|GITHUB_TOKEN|GH_TOKEN|AZURE_CLIENT_SECRET)(?:\}|\b)", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SecretEnvironment();

    [GeneratedRegex(@"(?:requests|httpx)\.(?:get|post|put|delete|request)\s*\(|socket\.(?:socket|create_connection)\s*\(|urllib\.request\.urlopen\s*\(|\bfetch\s*\(", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NetworkCode();

    [GeneratedRegex("(?:open|readFileSync|writeFileSync)\\s*\\(\\s*['\"]([^'\"]+)['\"]", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CodePaths();

    [GeneratedRegex(@"^\*\*\* (?:(?:Add|Update|Delete) File|Move to): ([^\r\n]+)\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PatchPaths();
}
