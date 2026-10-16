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

    /// <summary>A stable action fingerprint, independent of event id and JSON property order.</summary>
    public static string Fingerprint(HarnessAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ContentHash.Sha256(Encoding.UTF8.GetBytes(action.Tool + "\n" + CanonicalJson.Serialize(action.Input)));
    }

    /// <summary>
    /// Flags recognizable network, credential and out-of-brief path attempts without retaining their arguments.
    /// A failed check adds an audit-unverified warning without discarding the action's completed verdicts.
    /// </summary>
    public static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted) =>
        Inspect(action, workspace, permitted, command => Regex.IsMatch(command, @"\A(?:cat|head|tail)\s+\.vericairn/brief\.md\s*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));

    internal static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted, Func<string, bool> isBriefRead)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(permitted);
        ArgumentNullException.ThrowIfNull(isBriefRead);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var paths = new List<string>();
        bool readsBrief = action.Tool == "Read";
        bool briefReadUnverified = false;
        bool interpretable = false;
        Check(() => interpretable = action.Input.GetRawText().Length <= 65536 && action.Input.ValueKind == JsonValueKind.Object, codes);
        if (!interpretable)
        {
            codes.Add("audit-unverified");
        }
        else
        {
            Check(() => GatherPaths(action.Input, paths), codes);
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
                if (!Check(() => command = StringField(action.Input, field), codes))
                {
                    briefReadUnverified = true;
                }

                if (command is not null)
                {
                    Check(() => InspectCommand(command, paths, codes, 0), codes);
                    if (!Check(() => readsBrief |= isBriefRead(command), codes))
                    {
                        briefReadUnverified = true;
                    }
                }
            }

            Check(() =>
            {
                if (StringField(action.Input, "patch") is string patch)
                {
                    foreach (Match match in PatchPaths().Matches(patch))
                    {
                        paths.Add(match.Groups[1].Value);
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
                            if (working != Path.GetFullPath(workspace) && !PermittedPaths.Allows(working, workspace, permitted))
                            {
                                codes.Add("scope");
                            }
                        }, codes);
                    }
                }
            }

            foreach (string path in paths)
            {
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
                    // Reading the exported brief is part of every assignment. An undecidable brief-read check
                    // cannot establish a scope violation for that path, but does not exempt any other path.
                    bool brief = resolved == Path.Combine(Path.GetFullPath(workspace), ".vericairn", "brief.md");
                    if (!(brief && readsBrief) && !PermittedPaths.Allows(resolved, workspace, permitted))
                    {
                        codes.Add(brief && briefReadUnverified ? "audit-unverified" : "scope");
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
            "scope" => "The worker observed an invocation targeting a path outside the assignment's permitted paths.",
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
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or CanonicalJsonException
            or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or TimeoutException)
        {
            codes.Add("audit-unverified");
            return false;
        }
    }

    private static void GatherPaths(JsonElement value, List<string> paths)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name is "path" or "file_path" or "notebook_path" or "target_file")
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        paths.Add(property.Value.GetString()!);
                    }
                }
                else if (property.Name is "changes" or "paths" or "files")
                {
                    GatherPaths(property.Value, paths);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    paths.Add(item.GetString()!);
                }
                else
                {
                    GatherPaths(item, paths);
                }
            }
        }
    }

    private static void InspectCommand(string command, List<string> paths, HashSet<string> codes, int depth)
    {
        if (depth > 3)
        {
            codes.Add("audit-unverified");
            return;
        }

        string[] words = [];
        if (!Check(() => words = ShellWords().Matches(command).Select(m => m.Value).ToArray(), codes))
        {
            return;
        }

        string[] tokens = words.Select(w => w.Trim('"', '\'')).ToArray();
        bool first = true;
        bool redirect = false;
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
                continue;
            }

            if (!quoted && (token.Contains('>', StringComparison.Ordinal) || token == "<"))
            {
                redirect = true;
                continue;
            }

            if (redirect)
            {
                paths.Add(token);
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
                            paths.Add(argument);
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
                Check(() => InspectCommand(nested, paths, codes, depth + 1), codes);
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
                        paths.Add(match.Groups[1].Value);
                    }
                }, codes);

                if (i > 0 && tokens[i - 1] is "-c" or "-e")
                {
                    continue;
                }
            }

            if (token.StartsWith('-') || token.Contains("://", StringComparison.Ordinal) || executable is "echo" or "printf")
            {
                continue;
            }

            operand++;
            bool pattern = executable is "rg" or "grep" or "sed" or "awk" && operand == 1;
            if (!pattern && (FileCommands.Contains(executable) || token.Contains('/', StringComparison.Ordinal) || token.Contains('\\', StringComparison.Ordinal)
                || token.StartsWith('.') || token.StartsWith('~')))
            {
                paths.Add(token);
            }
        }
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

    [GeneratedRegex(@"^\*\*\* (?:Add|Update|Delete|Move to) File: (.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PatchPaths();
}
