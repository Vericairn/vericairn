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

    /// <summary>Flags recognizable network, credential and out-of-brief path attempts without retaining their arguments.</summary>
    public static IReadOnlyList<RunSignal> Inspect(HarnessAction action, string workspace, IReadOnlyList<string> permitted)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(permitted);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var paths = new List<string>();
        bool readsBrief = action.Tool == "Read";
        string input = action.Input.GetRawText();
        if (input.Length > 65536 || action.Input.ValueKind != JsonValueKind.Object)
        {
            codes.Add("audit-unverified");
        }
        else
        {
            GatherPaths(action.Input, paths);
            if (action.Tool is "WebFetch" or "WebSearch" or "web_search" || action.Tool.Contains("__web", StringComparison.OrdinalIgnoreCase))
            {
                codes.Add("network");
            }

            foreach (string field in new[] { "command", "cmd" })
            {
                if (action.Input.TryGetProperty(field, out JsonElement command) && command.ValueKind == JsonValueKind.String)
                {
                    InspectCommand(command.GetString()!, paths, codes, 0);
                    readsBrief |= Regex.IsMatch(command.GetString()!, @"\A(?:cat|head|tail)\s+\.vericairn/brief\.md\s*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                }
            }

            if (action.Input.TryGetProperty("patch", out JsonElement patch) && patch.ValueKind == JsonValueKind.String)
            {
                foreach (Match match in PatchPaths().Matches(patch.GetString()!))
                {
                    paths.Add(match.Groups[1].Value);
                }
            }

            if (paths.Any(path => SecretPath().IsMatch(path.Replace('\\', '/'))))
            {
                codes.Add("credentials");
            }

            string working = workspace;
            foreach (string field in new[] { "cwd", "workdir", "working_directory" })
            {
                if (action.Input.TryGetProperty(field, out JsonElement cwd) && cwd.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        working = Path.GetFullPath(cwd.GetString()!, workspace);
                        if (working != Path.GetFullPath(workspace) && !PermittedPaths.Allows(working, workspace, permitted))
                        {
                            codes.Add("scope");
                        }
                    }
                    catch (ArgumentException)
                    {
                        codes.Add("scope");
                    }
                }
            }

            foreach (string path in paths)
            {
                // Reading the exported brief is part of every assignment. It does not authorize changing worker metadata.
                string resolved;
                try
                {
                    resolved = Path.GetFullPath(path.Replace('\\', '/'), working);
                }
                catch (ArgumentException)
                {
                    codes.Add("scope");
                    continue;
                }

                bool briefRead = readsBrief && resolved == Path.Combine(Path.GetFullPath(workspace), ".vericairn", "brief.md");
                if (!briefRead && !PermittedPaths.Allows(resolved, workspace, permitted))
                {
                    codes.Add("scope");
                }
            }
        }

        string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        string fingerprint = Fingerprint(action);
        return codes.Order(StringComparer.Ordinal).Select(code => new RunSignal(code, code is "credentials" or "scope" ? "critical" : "warning", at, code switch
        {
            "network" => "The worker observed an invocation that attempts network access.",
            "credentials" => "The worker observed an invocation targeting credentials or a secret store.",
            "scope" => "The worker observed an invocation targeting a path outside the assignment's permitted paths or an unresolved path.",
            _ => "The worker could not fully interpret an invocation; its command audit is incomplete.",
        }, fingerprint)).ToArray();
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

        string[] tokens = ShellWords().Matches(command).Select(m => m.Value.Trim('"', '\'')).ToArray();
        bool first = true;
        bool redirect = false;
        string executable = "";
        int operand = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token is ";" or "&&" or "||" or "|" or "&" or "\n" or "\r\n")
            {
                first = true;
                continue;
            }

            if (token.Contains('>', StringComparison.Ordinal) || token == "<")
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
                if (token.Contains('=', StringComparison.Ordinal) || token is "sudo" or "env" or "command" or "exec")
                {
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
                InspectCommand(tokens[++i], paths, codes, depth + 1);
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
                if (NetworkCode().IsMatch(token))
                {
                    codes.Add("network");
                }

                foreach (Match match in CodePaths().Matches(token))
                {
                    paths.Add(match.Groups[1].Value);
                }

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

    [GeneratedRegex(@"(?:requests|httpx)\.(?:get|post|put|delete|request)\s*\(|socket\.(?:socket|create_connection)\s*\(|urllib\.request\.urlopen\s*\(|\bfetch\s*\(", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NetworkCode();

    [GeneratedRegex("(?:open|readFileSync|writeFileSync)\\s*\\(\\s*['\"]([^'\"]+)['\"]", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CodePaths();

    [GeneratedRegex(@"^\*\*\* (?:Add|Update|Delete|Move to) File: (.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PatchPaths();
}
