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
    private sealed record InvocationPath(string Path, PathAccess Access, string? WorkingDirectory = null, bool CopiesTree = false);
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
            string? working = workspace;
            foreach (string field in new[] { "cwd", "workdir", "working_directory" })
            {
                string? cwd = null;
                if (!Check(() => cwd = StringField(action.Input, field), codes)) working = null;
                else if (cwd is not null)
                {
                    working = null;
                    Check(() => working = ResolvePath(ExpandPath(cwd, environment), workspace), codes);
                    if (working is null) codes.Add("audit-unverified");
                }
            }

            Check(() => GatherPaths(action.Input, paths, structured), codes);
            for (int i = 0; i < paths.Count; i++)
            {
                int index = i;
                paths[index] = paths[index] with { WorkingDirectory = working };
                Check(() => paths[index] = paths[index] with { Path = ExpandPath(paths[index].Path, environment) }, codes);
            }
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
                Check(() => command = CommandField(action.Input, field, codes), codes);
                if (command is not null)
                {
                    bool commandReads = false;
                    Check(() => commandReads = InspectCommand(command, paths, codes, isReadOnly, new CommandContext(working, environment), 0), codes);
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
                        paths.Add(new InvocationPath(match.Groups[1].Value, PathAccess.Write, working));
                    }
                }
            }, codes);

            if (working is not null)
            {
                Check(() =>
                {
                    // A working directory alone does not attribute an outside change.
                    if (!InsideBoundary(working, workspace, environment)) codes.Add(readOnly ? "scope-read" : "audit-unverified");
                }, codes);
            }

            foreach (InvocationPath invocation in paths)
            {
                string path = invocation.Path;
                Check(() =>
                {
                    if (CredentialPath(path, invocation.WorkingDirectory, environment, invocation.CopiesTree))
                    {
                        codes.Add("credentials");
                    }
                }, codes);
                Check(() =>
                {
                    if (NullDevice(path)) return;
                    string normalized = path.Replace('\\', '/');
                    if (Unresolved(normalized))
                    {
                        // An unresolved expansion cannot establish which boundary a write would cross.
                        codes.Add("audit-unverified");
                        return;
                    }
                    if (invocation.WorkingDirectory is null && !Path.IsPathFullyQualified(normalized))
                    {
                        codes.Add("audit-unverified");
                        return;
                    }

                    string resolved = ResolvePath(normalized, invocation.WorkingDirectory)!;
                    if (NullDevice(resolved)) return;
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

    private static string? CommandField(JsonElement input, string name, HashSet<string> codes)
    {
        if (!input.TryGetProperty(name, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Array) { codes.Add("audit-unverified"); return null; }
        var words = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) words.Add(item.GetString()!);
            else codes.Add("audit-unverified");
        }
        if (words.Count == 0) { codes.Add("audit-unverified"); return null; }
        // A single string may be a shell command; an argv array must keep argument boundaries,
        // notably the body passed to sh -c and quoted text that is not itself an invocation.
        return words.Count == 1 ? words[0] : string.Join(' ', words.Select(QuoteArgument));
    }

    private static string QuoteArgument(string word)
    {
        int equal = word.IndexOf('=', StringComparison.Ordinal);
        if (equal > 0 && word[..equal].All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return word[..(equal + 1)] + QuoteWord(word[(equal + 1)..]);
        return QuoteWord(word);
    }

    private static string QuoteWord(string word) => word.Length > 0 && word.All(c => char.IsAsciiLetterOrDigit(c) || "_./\\-=:$%~{}".Contains(c, StringComparison.Ordinal))
        ? word : "'" + word.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

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

    [GeneratedRegex("(?:\"(?:\\\\.|[^\"\\\\])*\"|'[^']*'|[^\\s;&|<>()\"'])+|<<<|<<-|[;&|<>]+|[()]|\\r?\\n", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ShellWords();

    [GeneratedRegex("\\\\([$`\"\\\\])", RegexOptions.CultureInvariant, 100)]
    private static partial Regex DoubleQuotedEscape();

    [GeneratedRegex(@"\A[0-9]+(?:,[0-9]+)?p\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SedPrint();

    [GeneratedRegex(@"(?:^|/)(?:\.ssh|\.aws|\.azure|\.gnupg|\.kube|\.docker|\.netrc|\.npmrc|\.pypirc|\.git-credentials|\.env(?:\.[^/]*)?|\.credentials(?:\.json)?|auth\.json|credentials(?:\.json)?|id_rsa|id_ed25519|Keychains|keyrings|Credentials)(?:/|$)|(?:^|/)\.config/(?:gcloud|gh)(?:/|$)|(?:^|/)run/secrets(?:/|$)|\.(?:p8|p12)(?:$|/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SecretPath();

    [GeneratedRegex(@"(?<!\\)\$(?:\{)?(?:AWS_SECRET_ACCESS_KEY|AWS_SESSION_TOKEN|OPENAI_API_KEY|ANTHROPIC_API_KEY|GITHUB_TOKEN|GH_TOKEN|AZURE_CLIENT_SECRET)(?:\}|\b)", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SecretEnvironment();

    [GeneratedRegex(@"(?:requests|httpx)\.(?:get|post|put|delete|request)\s*\(|socket\.(?:socket|create_connection)\s*\(|urllib\.request\.urlopen\s*\(|\bfetch\s*\(", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NetworkCode();

    [GeneratedRegex(@"^\*\*\* (?:(?:Add|Update|Delete) File|Move to): ([^\r\n]+)\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PatchPaths();
}
