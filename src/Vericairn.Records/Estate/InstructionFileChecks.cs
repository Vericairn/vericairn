// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (FIX-139), what <c>estate check</c> reads in the agent instruction files an agent tool started in a repository takes as
/// its instructions. In the owner's files at the estate's root (<see cref="EstateLayout.OwnerInstructionFiles"/>) it warns about a hook
/// definition, an automatic approval and a value that looks like a secret, naming the file, the line and the kind and never the text it
/// found. It reads the whole text, so a key, its colon and its value may sit on separate lines. Every pattern runs in time linear in the
/// text, so no line, however long, stalls the check. A repository's own instruction files, at the root of a clone of one of the estate's
/// repositories registered on this machine, are reported as untrusted input. The check is a heuristic that warns; it reads and never
/// runs anything, and it cannot recognize every form a hook, an approval or a secret can take.
/// </summary>
internal static class InstructionFileChecks
{
    internal const string HookCode = "instructions.hook";
    internal const string ApprovalCode = "instructions.approval";
    internal const string SecretCode = "instructions.secret";
    internal const string RepositoryCode = "instructions.repository";

    private const string HookMessage = "Defines a hook: a command an agent tool that reads this file may run on its own, in every session started here. Review it, and keep hooks out of the owner's instructions unless you mean every session to run them.";
    private const string ApprovalMessage = "Grants an automatic approval: an agent tool that reads this file may act without asking first. Review it, and keep approvals out of the owner's instructions unless you mean every session to have them.";
    private const string SecretMessage = "Holds a value that looks like a secret; the value is not shown. Credentials never enter the estate: remove it, rotate it if it was real, and name the credential by purpose instead.";
    private const string RepositoryMessage = "A repository's own agent instruction file: untrusted input, never the owner's instructions, until a person reviews it. An agent tool started in that repository reads it beside the owner's files.";

    private const RegexOptions Linear = RegexOptions.NonBacktracking | RegexOptions.Multiline | RegexOptions.CultureInvariant;

    // Where a key may start on its line: after indentation, block-quote markers and list markers.
    private const string Prefix = @"^[ \t]*(?:>[ \t]*|[-*+][ \t]+|[0-9]{1,9}[.)][ \t]+)*";

    private const string HookEvents = "PreToolUse|PostToolUse|PostToolUseFailure|UserPromptSubmit|SessionStart|SessionEnd|SubagentStart|SubagentStop|PreCompact|PermissionRequest|BeforeTool|AfterTool|BeforeToolSelection|BeforeAgent|AfterAgent|BeforeModel|AfterModel";
    private const string ApprovalKeys = @"allowed[-_]?tools|auto[-_]?approve[a-z]*|always[-_]?allow[a-z]*|auto[-_]?accept[a-z]*|trusted[-_]?tools";

    // A hook definition: a hooks key at the start of a line or quoted anywhere, with TOML's dotted keys and tables; a hook event's key, by
    // a distinctive name at the start of a line, or quoted anywhere, where the generic Stop and Notification count too; and an editor task
    // that runs when a folder opens. Keys are case-sensitive, so prose such as "Hooks: none" is not one.
    private static readonly Regex HookKey = new(
        "(?:" + Prefix + @"[""']?hooks[""']?(?:\.[\w""'-]+)*\s*[:=]"
        + @"|[""']hooks[""']\s*:"
        + @"|^[ \t]*\[\[?[ \t]*[""']?hooks[\w.""'-]*\]\]?[ \t]*\r?$"
        + "|" + Prefix + @"[""']?(?:" + HookEvents + @")[""']?\s*:"
        + @"|[""'](?:" + HookEvents + @"|Stop|Notification)[""']\s*:"
        + @"|[""']runOn[""']\s*:\s*[""']folderOpen[""'])", Linear);

    // Git's hooks path, named anywhere: a mention, so a line that forbids it is not reported.
    private static readonly Regex HookMention = new(@"core\.hooksPath", Linear | RegexOptions.IgnoreCase);

    // An automatic approval written as configuration: an approving key at the start of a line or quoted anywhere (with a dotted prefix in
    // the quotes), a permission allow list whose items may follow on the next lines, and a trust setting.
    private static readonly Regex ApprovalKey = new(
        "(?:" + Prefix + @"[""']?(?:" + ApprovalKeys + @")[""']?\s*[:=]"
        + @"|[""'](?:[\w-]+\.)*(?:" + ApprovalKeys + @")[""']\s*:"
        + @"|[""'](?-i:allow)[""']\s*:\s*\["
        + "|" + Prefix + @"(?-i:allow)[ \t]*:[ \t]*(?:\[|\r?\n[ \t]*-[ \t])"
        + "|" + Prefix + @"[""']?trust[""']?\s*[:=]\s*[""']?true\b"
        + @"|[""']trust[""']\s*:\s*true\b)", Linear | RegexOptions.IgnoreCase);

    // An automatic approval named anywhere: a launch flag that skips or bypasses approvals, a mode or policy that approves without asking,
    // and the distinctive values of such settings. A mention, so a line that forbids it is not reported.
    private static readonly Regex ApprovalMention = new(
        @"(?:--(?:dangerously-[a-z-]+|yolo|full-auto|allow-all-tools|trust-all-tools|auto-approve|allowed[-_]?tools|allow-tool"
        + @"|permission-mode[= \t]+(?:bypassPermissions|acceptEdits|dontAsk|auto)|approval-mode[= \t]+(?:yolo|auto[-_]?edit)|ask-for-approval[= \t]+never)\b"
        + @"|(?:^|[ \t])-a[ \t]+never\b"
        + @"|\b(?:approval|permission|default)[-_]?mode[""']?[ \t]*[:=][ \t]*[""']?(?:yolo|auto[-_]?edit|acceptEdits|bypassPermissions|dontAsk|auto|full[-_]?auto|never)\b"
        + @"|\bapproval[-_]?policy[""']?[ \t]*[:=][ \t]*[""']?never\b"
        + @"|\b(?:bypassPermissions|danger-full-access|acceptEdits|dontAsk)\b)", Linear | RegexOptions.IgnoreCase);

    // A secret-named key and its separator; its value, on the key's line, after a YAML block indicator or on the next line, is read after
    // it, so that a key on the next line is still a key of its own.
    private static readonly Regex SecretKey = new(
        @"\b(?:[a-z0-9]+[-_.])*(?:secret|token|pass(?:word|wd|phrase)?|pwd|api[-_ ]?key|apikey|private[-_]?key|access[-_]?key|client[-_]?secret|credentials?|auth[-_]?token)(?:[-_.][a-z0-9]+)*"
        + @"[""']?[ \t]*[:=]", Linear | RegexOptions.IgnoreCase);

    // A bearer or basic credential, a password in a URL's user information, and a user and password given to a client with -u.
    // None of these patterns captures a group: each value's place is read from its match, which keeps every match linear.
    private static readonly Regex Scheme = new(@"\b(?:bearer|basic)[ \t]+[a-z0-9._~+/-]{12,}=*", Linear | RegexOptions.IgnoreCase);
    private static readonly Regex UserInformation = new(@"\b[a-z][a-z0-9+.-]*://[^/\s:@]+:[^/\s@]+@", Linear | RegexOptions.IgnoreCase);
    private static readonly Regex ClientUser = new(@"(?:^|[ \t])(?:-u|--user)[ \t]+[^\s:""'`]+:[^\s""'`]+", Linear);

    // A long run of token characters, read without a pattern; it reads as random when it mixes cases and digits and changes between them
    // often.
    private const int TokenLength = 32;

    // Words a letters-only value often is when it names no secret.
    private static readonly HashSet<string> NotSecrets = new(StringComparer.OrdinalIgnoreCase)
    {
        "required", "optional", "provided", "mandatory", "example", "examples", "changeme", "redacted", "hidden", "unchanged", "placeholder",
        "password", "passwords", "passphrase", "secret", "secrets", "token", "tokens", "credential", "credentials", "environment", "variable",
        "variables", "reference", "keychain", "undefined", "disabled", "enabled", "inherited", "generated", "rotating", "supplied",
    };

    // The most of a value that is read, and how far a bracketed placeholder's closing bracket is looked for: a value longer than this is
    // judged by its start, so that a long line of keys stays linear.
    private const int ValueLimit = 128;

    // Words that open a line forbidding what it names.
    private static readonly string[] Prohibitions = ["never", "do not", "don't", "dont", "must not", "should not", "shall not", "avoid", "not", "no"];

    /// <summary>Scans the text of an owner's instruction file at the estate root, adding one warning for each kind found on a line.</summary>
    internal static void Scan(string text, string relative, FindingList findings)
    {
        int[] starts = LineStarts(text);
        var found = new SortedSet<(int Line, int Kind)>();
        var forbidding = new Dictionary<int, bool>();
        foreach (Match match in HookKey.Matches(text)) found.Add((Line(starts, match.Index), 0));
        foreach (Match match in HookMention.Matches(text)) Mention(text, starts, forbidding, match.Index, 0, found);
        foreach (Match match in ApprovalKey.Matches(text)) found.Add((Line(starts, match.Index), 1));
        foreach (Match match in ApprovalMention.Matches(text)) Mention(text, starts, forbidding, match.Index, 1, found);

        var secrets = new SortedSet<int>();
        foreach (Match match in SecretKey.Matches(text))
        {
            if (ValueAfter(text, match.Index + match.Length) is (int index, string value) && Assigned(text, index, value)) secrets.Add(Line(starts, index));
        }

        foreach (Match match in Scheme.Matches(text))
        {
            // The credential follows the scheme's name and the spaces after it.
            int index = match.Index + match.Value.IndexOfAny([' ', '\t']);
            while (text[index] is ' ' or '\t') index++;
            if (Credential(text, index, Bounded(text, index, match.Index + match.Length))) secrets.Add(Line(starts, index));
        }

        foreach (Match match in UserInformation.Matches(text))
        {
            // The password sits between the colon after the user's name and the closing @.
            int index = match.Index + match.Value.IndexOf(':', match.Value.IndexOf("://", StringComparison.Ordinal) + 3) + 1;
            if (!IsPlaceholder(text, index, Bounded(text, index, match.Index + match.Length - 1))) secrets.Add(Line(starts, index));
        }

        foreach (Match match in ClientUser.Matches(text))
        {
            // The password follows the first colon, which the user's name never holds.
            int index = match.Index + match.Value.IndexOf(':') + 1;
            if (!IsPlaceholder(text, index, Bounded(text, index, match.Index + match.Length))) secrets.Add(Line(starts, index));
        }

        for (int at = 0; at < text.Length;)
        {
            if (!IsTokenCharacter(text[at]))
            {
                at++;
                continue;
            }

            int end = at;
            while (end < text.Length && IsTokenCharacter(text[end])) end++;
            if (end - at >= TokenLength && Random(text, at, end)) secrets.Add(Line(starts, at));
            at = end;
        }

        // A credential in a known format is already refused as secrets.pattern on its line; this reads the shapes beside it.
        foreach (int line in secrets)
        {
            if (!SecretShapes.Secret().IsMatch(LineText(text, starts, line))) found.Add((line, 2));
        }

        foreach ((int line, int kind) in found)
        {
            string location = $"{relative}:{line}";
            switch (kind)
            {
                case 0: findings.Warning(HookCode, location, HookMessage); break;
                case 1: findings.Warning(ApprovalCode, location, ApprovalMessage); break;
                default: findings.Warning(SecretCode, location, SecretMessage); break;
            }
        }
    }

    /// <summary>
    /// Reports each repository's own instruction files at the root of its clone registered on this machine, for every current repository
    /// resource of the estate: each entry whose name is an instruction file's in any case, since a case-insensitive volume opens it under
    /// that name, and a link of that name wherever it leads. Without a registered clone, or with an unreadable registration, nothing is
    /// reported for it.
    /// </summary>
    internal static void CheckRepositories(EstateModel model, FindingList findings)
    {
        string? estate = model.Manifest?.Estate;
        if (estate is null)
        {
            return;
        }

        IReadOnlyDictionary<string, string> clones;
        try
        {
            clones = RepositoryClones.All(estate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return;
        }

        if (clones.Count == 0)
        {
            return;
        }

        foreach (RecordEnvelope resource in model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository").OrderBy(r => model.Chain(r)[0], StringComparer.Ordinal))
        {
            IReadOnlyList<string> chain = model.Chain(resource);
            string? clone = chain.Reverse().Select(id => clones.GetValueOrDefault(id)).FirstOrDefault(c => c is not null && Directory.Exists(c));
            if (clone is null)
            {
                continue;
            }

            string[] names;
            try
            {
                names = [.. Directory.EnumerateFileSystemEntries(clone)
                    .Where(entry => EstateLayout.OwnerInstructionFiles.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)
                        && (new FileInfo(entry).LinkTarget is not null || !Directory.Exists(entry)))
                    .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string name in names)
            {
                findings.Warning(RepositoryCode, $"resource:{chain[0]}/{name}", RepositoryMessage);
            }
        }
    }

    private static void Mention(string text, int[] starts, Dictionary<int, bool> forbidding, int index, int kind, SortedSet<(int Line, int Kind)> found)
    {
        int line = Line(starts, index);
        if (!forbidding.TryGetValue(line, out bool forbids))
        {
            forbids = Forbids(text, starts[line - 1], LineEnd(text, starts, line));
            forbidding[line] = forbids;
        }

        if (!forbids) found.Add((line, kind));
    }

    // The value after a key's separator: past spaces, a YAML block indicator and one line break with its indentation, and an opening
    // quote, up to the next space or quote.
    private static (int Index, string Value)? ValueAfter(string text, int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        if (at < text.Length && text[at] is '|' or '>')
        {
            at++;
            if (at < text.Length && text[at] is '-' or '+') at++;
            while (at < text.Length && text[at] is ' ' or '\t') at++;
        }

        if (at < text.Length && text[at] == '\r') at++;
        if (at < text.Length && text[at] == '\n')
        {
            at++;
            while (at < text.Length && text[at] is ' ' or '\t') at++;
        }

        if (at < text.Length && text[at] is '"' or '\'' or '`') at++;
        int start = at;
        while (at < text.Length && at - start < ValueLimit && !char.IsWhiteSpace(text[at]) && text[at] is not ('"' or '\'' or '`')) at++;
        return at > start ? (start, text[start..at]) : null;
    }

    // A secret-named key's value: not a placeholder, a reference, a path, a URL or a version; eight characters or more, with letters and
    // digits, or letters alone when it is the last word on its line and no word such a key commonly holds.
    private static bool Assigned(string text, int index, string raw)
    {
        if (IsPlaceholder(text, index, raw)) return false;
        string trimmed = raw.TrimEnd(',', ';', '.', ')', ']', '}');
        if (trimmed.Length < 8 || NotAValue(trimmed)) return false;
        if (trimmed.Any(char.IsAsciiLetter) && trimmed.Any(char.IsAsciiDigit)) return true;
        int after = index + raw.Length;
        while (after < text.Length && text[after] is ' ' or '\t' or '"' or '\'' or '`' or '\r') after++;
        return trimmed.Count(char.IsAsciiLetter) >= 8 && !NotSecrets.Contains(trimmed.Trim('-', '_'))
            && (after >= text.Length || text[after] == '\n');
    }

    // A value's text, up to the end of its match and at most ValueLimit characters.
    private static string Bounded(string text, int index, int end) => text[index..Math.Min(end, index + ValueLimit)];

    // A bearer or basic credential's value: letters and digits, base64 padding, or both cases over sixteen characters or more.
    private static bool Credential(string text, int index, string raw)
    {
        return !IsPlaceholder(text, index, raw) && !NotAValue(raw)
            && ((raw.Any(char.IsAsciiLetter) && raw.Any(char.IsAsciiDigit)) || raw.EndsWith('=') || (raw.Length >= 16 && raw.Any(char.IsAsciiLetterUpper) && raw.Any(char.IsAsciiLetterLower)));
    }

    // A long token that mixes upper case, lower case and digits and changes between them at least once in every three characters, which a
    // word-built identifier rarely does.
    private static bool Random(string text, int start, int end)
    {
        bool upper = false, lower = false, digit = false;
        int changes = 0;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            upper |= char.IsAsciiLetterUpper(c);
            lower |= char.IsAsciiLetterLower(c);
            digit |= char.IsAsciiDigit(c);
            if (i > start && Class(c) != Class(text[i - 1])) changes++;
        }

        return upper && lower && digit && changes * 3 >= end - start && !IsPlaceholder(text, start, Bounded(text, start, end));
    }

    private static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '+' or '=' or '-';

    private static int Class(char c) => char.IsAsciiLetterUpper(c) ? 0 : char.IsAsciiLetterLower(c) ? 1 : char.IsAsciiDigit(c) ? 2 : 3;

    // A placeholder or reference stands where a value would: a bracketed name such as <your key>, [token] or (redacted), a template, a
    // variable or a vault reference, a value cut short with an ellipsis or stars, or a run of one or two characters. A real value that only
    // starts with one of those characters is still a value.
    private static bool IsPlaceholder(string text, int index, string raw)
    {
        if (raw.Length == 0) return true;
        if (raw[0] is '<' or '[' or '(' or '{')
        {
            char close = raw[0] switch { '<' => '>', '[' => ']', '(' => ')', _ => '}' };
            int end = index + 1;
            while (end < text.Length && end - index < ValueLimit && text[end] != close && text[end] != '\n') end++;
            if (end < text.Length && text[end] == close)
            {
                string inner = text[(index + 1)..end].Trim('{', '}', ' ');
                if (inner.Any(char.IsWhiteSpace) || inner.Length == 0 || inner.All(c => char.IsAsciiLetter(c) || c is '_' or '-' or '.' or ' ')) return true;
            }
        }

        if (raw.StartsWith("${", StringComparison.Ordinal) || raw.StartsWith("{{", StringComparison.Ordinal)) return true;
        // A variable: $NAME in capitals and digits, or a name of letters alone; a value mixing lower case and digits after a $ is a value.
        string name = raw.TrimEnd(',', ';', '.')[1..];
        if (raw[0] == '$' && name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            && (!name.Any(char.IsAsciiLetterLower) || !name.Any(char.IsAsciiDigit))) return true;
        if (raw[0] == '%' && raw.Length > 2 && raw.TrimEnd(',', ';', '.').EndsWith('%') && raw.Trim('%', ',', ';', '.').All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return true;
        foreach (string reference in new[] { "env:", "vault:", "secret:", "secrets.", "op://", "keychain:", "ref:", "file:" })
            if (raw.StartsWith(reference, StringComparison.OrdinalIgnoreCase)) return true;
        return raw.Contains("...", StringComparison.Ordinal) || raw.Contains('…', StringComparison.Ordinal) || raw.Contains("***", StringComparison.Ordinal)
            || raw.Distinct().Count() <= 2;
    }

    // Not a value at all: a URL, a path or a version number.
    private static bool NotAValue(string value) =>
        value.Contains("://", StringComparison.Ordinal) || value[0] == '/' || value.StartsWith("./", StringComparison.Ordinal) || value.StartsWith("../", StringComparison.Ordinal)
        || value.StartsWith("~/", StringComparison.Ordinal) || IsVersion(value);

    private static bool IsVersion(string value)
    {
        int at = value[0] is 'v' or 'V' ? 1 : 0, dots = 0, digits = 0;
        while (at < value.Length && (char.IsAsciiDigit(value[at]) || value[at] == '.'))
        {
            if (value[at] == '.') dots++; else digits++;
            at++;
        }

        return dots >= 1 && digits >= 2 && (at == value.Length || value[at] is '-' or '+');
    }

    // Whether a line, past its indentation and its quote and list markers, opens by forbidding: never, do not, avoid and the like.
    private static bool Forbids(string text, int start, int end)
    {
        int at = start;
        while (at < end)
        {
            char c = text[at];
            if (c is ' ' or '\t' or '>' or '*' or '_' or '-' or '+') { at++; continue; }
            if (char.IsAsciiDigit(c))
            {
                int digits = at;
                while (digits < end && char.IsAsciiDigit(text[digits])) digits++;
                if (digits < end && text[digits] is '.' or ')') { at = digits + 1; continue; }
            }

            break;
        }

        foreach (string word in Prohibitions)
        {
            if (at + word.Length <= end && string.Compare(text, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (at + word.Length == end || !char.IsAsciiLetter(text[at + word.Length])))
                return true;
        }

        return false;
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1)) starts.Add(i + 1);
        return [.. starts];
    }

    // The 1-based line holding a character.
    private static int Line(int[] starts, int index)
    {
        int found = Array.BinarySearch(starts, index);
        return found >= 0 ? found + 1 : ~found;
    }

    private static int LineEnd(string text, int[] starts, int line) => line < starts.Length ? starts[line] - 1 : text.Length;

    private static string LineText(string text, int[] starts, int line) => text[starts[line - 1]..LineEnd(text, starts, line)];
}
