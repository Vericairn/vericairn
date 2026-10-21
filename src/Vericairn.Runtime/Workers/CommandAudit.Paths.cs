// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private sealed class CommandContext(string? working, IReadOnlyDictionary<string, string> environment)
    {
        public string? Working { get; set; } = working;
        public Dictionary<string, string> Environment { get; } = new(environment, WorkerEnvironment.Names);
        public IReadOnlyDictionary<string, string>? ExpansionEnvironment { get; set; }
        public CommandContext Copy() => new(Working, Environment) { ExpansionEnvironment = ExpansionEnvironment };
    }

    private static void AddPath(List<InvocationPath> paths, string path, PathAccess access, CommandContext context) =>
        paths.Add(new InvocationPath(ExpandPath(path, context.Environment), access, context.Working));

    private static bool NullDevice(string path) => path == "/dev/null" || OperatingSystem.IsWindows()
        && (path.Equals("$null", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path.Replace('\\', '/')).Equals("NUL", StringComparison.OrdinalIgnoreCase));

    private static bool Unresolved(string path) => path.IndexOfAny(['~', '$', '`']) >= 0 || IsMatch(PercentExpansion(), path);

    private static string? ResolvePath(string path, string? working)
    {
        path = path.Replace('\\', '/');
        if (Unresolved(path) || working is null && !Path.IsPathFullyQualified(path)) return null;
        return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, working!);
    }

    // Resolve only simple substitutions from the captured launch environment or assignments visible in
    // this command. The launch environment alone grants temporary boundaries; assignments grant none.
    // Never read ambient variables, execute substitutions, enumerate globs or read credential contents.
    private static string ExpandPath(string path, IReadOnlyDictionary<string, string> environment)
    {
        if (NullDevice(path)) return path;
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            string? home = environment.GetValueOrDefault(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME");
            if (!string.IsNullOrWhiteSpace(home)) path = home.TrimEnd('/', '\\') + path[1..];
        }

        return Replace(PathExpansion(), path, match =>
        {
            string name = match.Groups["name"].Value;
            return environment.TryGetValue(name, out string? value) ? value : match.Value;
        });
    }

    // Removes quoting as a POSIX shell does, so that a partly quoted word such as ''"$HOME"/x or ..''/x names the path the
    // shell would use. Variables expand outside single quotes, from the given environment only; an unknown one stays as its
    // text, which leaves the word unresolved. Without an environment the word is only unquoted. A leading tilde expands as
    // ExpandPath expands it, quoted or not, which keeps the audit's verdict conservative. A backslash escapes only where it
    // is not the path separator.
    private static string ShellWord(string word, IReadOnlyDictionary<string, string>? environment)
    {
        bool escapes = !OperatingSystem.IsWindows();
        var text = new StringBuilder(word.Length);
        for (int i = 0; i < word.Length;)
        {
            char c = word[i];
            if (c == '\'')
            {
                int close = word.IndexOf('\'', i + 1);
                // An unterminated quote is kept as written, so the word stays recognisably uncertain.
                if (close < 0) { text.Append(word, i, word.Length - i); break; }
                text.Append(word, i + 1, close - i - 1);
                i = close + 1;
            }
            else if (c == '"')
            {
                for (i++; i < word.Length && word[i] != '"';)
                {
                    if (escapes && word[i] == '\\' && i + 1 < word.Length && word[i + 1] is '$' or '`' or '"' or '\\') { text.Append(word[i + 1]); i += 2; }
                    else i = Expand(word, i, text, environment);
                }
                i++;
            }
            else if (escapes && c == '\\' && i + 1 < word.Length) { text.Append(word[i + 1]); i += 2; }
            else i = Expand(word, i, text, environment);
        }

        string value = text.ToString();
        if (environment is not null && (value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
            && environment.GetValueOrDefault(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME") is { } home && !string.IsNullOrWhiteSpace(home))
            value = home.TrimEnd('/', '\\') + value[1..];
        return value;

        static int Expand(string word, int i, StringBuilder text, IReadOnlyDictionary<string, string>? environment)
        {
            if (environment is not null && word[i] is '$' or '%' && Match(PathExpansionAt(), word, i) is { Success: true } match)
            {
                text.Append(environment.TryGetValue(match.Groups["name"].Value, out string? value) ? value : match.Value);
                return i + match.Length;
            }

            text.Append(word[i]);
            return i + 1;
        }
    }

    private static bool CredentialPath(string path, string? working, IReadOnlyDictionary<string, string> environment, bool copiesTree)
    {
        if (IsMatch(SecretPath(), path.Replace('\\', '/'))) return true;
        string? resolved = ResolvePath(path, working);
        if (resolved is null) return false;
        if (IsMatch(SecretPath(), resolved.Replace('\\', '/'))) return true;
        string? home = CredentialHome(environment, working);
        if (home is null) return false;
        // A recursive source contains the same stores when it names the home or an ancestor.
        // Listings and ordinary file reads do not acquire this classification.
        if (copiesTree && LexicallyInside(home, resolved)) return true;
        string relative = Path.GetRelativePath(home, resolved).Replace('\\', '/');
        // The estates directory, each estate's directory and each clone's directory hold live.db with the runtime's certificate
        // and identity. Workspaces beside them under an estate's directory hold none, so copies inside a workspace stay ordinary.
        if (copiesTree && IsMatch(VericairnStoreDirectory(), relative.TrimEnd('/'))) return true;
        // File/CNG/Secure Enclave/Secret Service stores all use keys/. Connections retain pairing
        // identities. RuntimeIdentity and RuntimeCertificate live beside EstateLocations' live.db.
        return IsMatch(VericairnCredentialRelativePath(), relative);
    }

    private static string? CredentialHome(IReadOnlyDictionary<string, string> environment, string? working)
    {
        if (environment.TryGetValue("VERICAIRN_HOME", out string? configured) && !string.IsNullOrWhiteSpace(configured))
            return ResolvePath(configured, working);
        if (OperatingSystem.IsWindows())
            return environment.TryGetValue("LOCALAPPDATA", out string? local) && Path.IsPathFullyQualified(local) ? Path.Combine(local, "Vericairn") : null;
        if (!OperatingSystem.IsMacOS() && environment.TryGetValue("XDG_CONFIG_HOME", out string? xdg) && Path.IsPathFullyQualified(xdg))
            return Path.Combine(xdg, "vericairn");
        if (!environment.TryGetValue("HOME", out string? home) || !Path.IsPathFullyQualified(home)) return null;
        return OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support", "Vericairn") : Path.Combine(home, ".config", "vericairn");
    }

    [GeneratedRegex(@"(?<![\\$])(?:\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?:(?i:env:))?(?<name>[A-Za-z_][A-Za-z0-9_]*)|%(?<name>[A-Za-z_][A-Za-z0-9_]*)%)", RegexOptions.CultureInvariant, 100)]
    private static partial Regex PathExpansion();

    [GeneratedRegex(@"\G(?:\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?:(?i:env:))?(?<name>[A-Za-z_][A-Za-z0-9_]*)|%(?<name>[A-Za-z_][A-Za-z0-9_]*)%)", RegexOptions.CultureInvariant, 100)]
    private static partial Regex PathExpansionAt();

    [GeneratedRegex(@"%[^%]+%", RegexOptions.CultureInvariant, 100)]
    private static partial Regex PercentExpansion();

    [GeneratedRegex(@"\Aestates(?:/[^/]+(?:/(?:local|[a-f0-9]{16}))?)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex VericairnStoreDirectory();

    [GeneratedRegex(@"\A(?:keys(?:/|$)|connections(?:/|$)|estates/[^/]+/(?:local|[a-f0-9]{16})$|estates/[^/]+/[^/]+/(?:runtime-certificate\.p12|runtime-identity\.json)(?:\.tmp)?$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex VericairnCredentialRelativePath();
}
