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
    private static readonly HashSet<string> FileCommands = new(StringComparer.OrdinalIgnoreCase)
        { "cat", "head", "tail", "ls", "find", "cp", "mv", "rm", "touch", "mkdir", "rmdir", "tee", "truncate", "chmod", "chown", "Get-Content", "Set-Content", "Add-Content", "Copy-Item", "Move-Item", "Remove-Item", "Get-ChildItem" };

    private static readonly HashSet<string> ReadCommands = new(StringComparer.OrdinalIgnoreCase)
        { "cat", "head", "tail", "ls", "pwd", "rg", "grep", "find", "wc", "stat", "echo", "printf", "Get-Content", "Get-ChildItem", "Get-Location" };

    private sealed record InvocationPath(string Path, bool ReadOnly);

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
    /// Flags recognizable network, credential and out-of-brief path attempts without retaining their arguments.
    /// A failed check adds an audit-unverified warning without discarding the action's completed verdicts.
    /// </summary>
    public static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted) =>
        Inspect(action, workspace, permitted, IsReadOnly);

    internal static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted, Func<string, bool> isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(permitted);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var paths = new List<InvocationPath>();
        bool readOnly = action.Tool is "Read" or "Glob" or "Grep" or "view_image";
        bool sawCommand = false;
        bool interpretable = false;
        Check(() => interpretable = action.Input.GetRawText().Length <= 65536 && action.Input.ValueKind == JsonValueKind.Object, codes);
        if (!interpretable)
        {
            codes.Add("audit-unverified");
        }
        else
        {
            Check(() => GatherPaths(action.Input, paths, readOnly), codes);
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
                    readOnly = (!sawCommand || readOnly) && commandReads && paths.All(p => p.ReadOnly);
                    sawCommand = true;
                }
            }

            Check(() =>
            {
                if (StringField(action.Input, "patch") is string patch)
                {
                    foreach (Match match in PatchPaths().Matches(patch))
                    {
                        paths.Add(new InvocationPath(match.Groups[1].Value, false));
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
                    Check(() => working = Path.GetFullPath(cwd, workspace), codes);
                    if (working is not null)
                    {
                        Check(() =>
                        {
                            // A classified read needs only the workspace boundary. For other invocations,
                            // preserve the conservative check of a supplied base that they may change.
                            if (readOnly ? !PermittedPaths.Contains(working, workspace)
                                : working != Path.GetFullPath(workspace) && !PermittedPaths.Allows(working, workspace, permitted))
                            {
                                codes.Add(readOnly ? "scope-read" : "scope");
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
                    string normalized = path.Replace('\\', '/');
                    if (working is null && !Path.IsPathFullyQualified(normalized))
                    {
                        codes.Add("audit-unverified");
                        return;
                    }

                    string resolved = Path.IsPathFullyQualified(normalized) ? Path.GetFullPath(normalized) : Path.GetFullPath(normalized, working!);
                    if (invocation.ReadOnly)
                    {
                        if (!PermittedPaths.Contains(resolved, workspace)) codes.Add("scope-read");
                    }
                    else if (!PermittedPaths.Allows(resolved, workspace, permitted))
                    {
                        codes.Add("scope");
                    }
                }, codes);
            }
        }

        string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        string? fingerprint = null;
        Check(() => fingerprint = Fingerprint(action), codes);
        return codes.Order(StringComparer.Ordinal).Select(code => new RunSignal(code, code is "network" or "credentials" or "scope" ? "critical" : "warning", at, code switch
        {
            "network" => "The worker observed an invocation that attempts network access.",
            "credentials" => "The worker observed an invocation targeting credentials or a secret store.",
            "scope" => "The worker observed an invocation that may change a path outside the assignment's permitted paths.",
            "scope-read" => "The worker observed an invocation that may read outside its workspace.",
            _ => "The worker could not complete one or more invocation checks; completed audit verdicts still apply.",
        }, fingerprint)).ToArray();
    }

    private static string? StringField(JsonElement input, string name) =>
        input.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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

    private static void GatherPaths(JsonElement value, List<InvocationPath> paths, bool readOnly)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name is "path" or "file_path" or "notebook_path" or "target_file")
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        paths.Add(new InvocationPath(property.Value.GetString()!, readOnly));
                    }
                }
                else if (property.Name is "changes" or "paths" or "files")
                {
                    GatherPaths(property.Value, paths, readOnly);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    paths.Add(new InvocationPath(item.GetString()!, readOnly));
                }
                else
                {
                    GatherPaths(item, paths, readOnly);
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

        string[] words = [];
        if (!Check(() => words = ShellWords().Matches(command).Select(m => m.Value).ToArray(), codes))
        {
            return false;
        }

        string[] tokens = words.Select(w => w.Trim('"', '\'')).ToArray();
        bool first = true;
        bool redirect = false;
        bool inputRedirect = false;
        bool descriptorRedirect = false;
        bool reads = false;
        bool allReadOnly = true;
        string? wrapper = null;
        string executable = "";
        int operand = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            bool quoted = words[i].StartsWith('"') || words[i].StartsWith('\'');
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
                    paths.Add(new InvocationPath(token, inputRedirect));
                }

                redirect = false;
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
                            paths.Add(new InvocationPath(argument, false));
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
                reads = false;
                Check(() => reads = isReadOnly(string.Join(" ", words[i..end])), codes);
                bool shell = executable is "bash" or "sh" or "zsh" or "pwsh" or "powershell" or "cmd"
                    && i + 2 < end && tokens[i + 1] is "-c" or "-lc" or "-Command" or "/c";
                allReadOnly &= reads || shell;
                if (NetworkCommands.Contains(executable))
                {
                    codes.Add("network");
                }

                if (executable is "printenv" or "cmdkey" or "secret-tool" or "Get-StoredCredential"
                    || executable == "security"
                    || executable == "op"
                    || executable == "pass"
                    || executable == "git" && command.Contains("credential", StringComparison.Ordinal)
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

            if (executable == "git" && token is "fetch" or "pull" or "push" or "clone" or "ls-remote"
                || executable is "npm" or "pnpm" or "yarn" or "pip" or "pip3" && token is "install" or "ci" or "add"
                || executable == "dotnet" && token == "restore")
            {
                codes.Add("network");
            }

            if (executable is "python" or "python3" or "node" or "ruby" or "perl")
            {
                codes.Add("audit-unverified");
                Check(() =>
                {
                    if (NetworkCode().IsMatch(token))
                    {
                        codes.Add("network");
                    }
                }, codes);
                Check(() =>
                {
                    foreach (Match match in CodePaths().Matches(token))
                    {
                        paths.Add(new InvocationPath(match.Groups[1].Value, false));
                    }
                }, codes);

                if (i > 0 && tokens[i - 1] is "-c" or "-e")
                {
                    continue;
                }
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
            if (!pattern && ((reads && executable is not ("pwd" or "Get-Location")) || FileCommands.Contains(executable) || token.Contains('/', StringComparison.Ordinal) || token.Contains('\\', StringComparison.Ordinal)
                || token.Contains('.', StringComparison.Ordinal) || token.StartsWith('~')))
            {
                paths.Add(new InvocationPath(token, reads));
            }
        }

        return allReadOnly;
    }

    private static bool Separator(string word) => word is ";" or "&&" or "||" or "|" or "&" or "\n" or "\r\n";

    private static bool IsReadOnly(string command)
    {
        string[] words = ShellWords().Matches(command).Select(m => m.Value).ToArray();
        string[] tokens = words.Select(w => w.Trim('"', '\'')).ToArray();
        string executable = tokens[0].Replace('\\', '/').Split('/')[^1];
        if (executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executable = executable[..^4];
        if (!ReadCommands.Contains(executable)) return false;
        // These options execute helpers or write files. Unclassified forms keep the conservative path verdict.
        if (executable.Equals("find", StringComparison.OrdinalIgnoreCase) && tokens.Any(t => t is "-delete" or "-exec" or "-execdir" or "-ok" or "-okdir" or "-fprint" or "-fprint0" or "-fprintf" or "-fls")) return false;
        if (executable.Equals("rg", StringComparison.OrdinalIgnoreCase) && tokens.Any(t => t is "--pre" or "--hostname-bin" or "-z" or "--search-zip"
            || t.StartsWith("--pre=", StringComparison.Ordinal) || t.StartsWith("--hostname-bin=", StringComparison.Ordinal))) return false;
        return !words.Any(w => !w.StartsWith('\'') && (w.Contains("$(", StringComparison.Ordinal) || w.Contains('`', StringComparison.Ordinal)));
    }

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"|'[^']*'|[^\\s;&|<>]+|[;&|<>]+|\\r?\\n", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ShellWords();

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
