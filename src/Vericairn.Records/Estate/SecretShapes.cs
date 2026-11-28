// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Vericairn.Records.Estate;

/// <summary>
/// The shapes the estate check refuses in an estate: credentials by their known formats, property names that hold secrets, and
/// absolute machine paths. The support bundle redacts by the same shapes, so what one finds the other removes.
/// </summary>
internal static partial class SecretShapes
{
    internal const int ProvisionFragmentLength = 12;
    private const string ClaudeSetupStem = "sk-ant-" + "oat01";
    private const string ClaudeSetupPrefix = ClaudeSetupStem + "-";
    private const string ClaudeSetupTokenPattern = ClaudeSetupStem + "[A-Za-z0-9_-]{32,}";
    private static readonly SearchValues<char> TokenCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    /// <summary>The start of an absolute machine path on macOS, Linux or Windows.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_./-])(?:/Users/|/home/[A-Za-z0-9_-]+/|/Volumes/|/private/var/|/mnt/[a-z]/|[A-Za-z]:\\)")]
    internal static partial Regex AbsolutePath();

    /// <summary>A credential in one of the formats the estate check knows.</summary>
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{10,}|" + ClaudeSetupTokenPattern + @"|\bsk-[A-Za-z0-9_-]{32,}|AIza[0-9A-Za-z_-]{35}|eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}")]
    internal static partial Regex Secret();

    /// <summary>A setup-token sign-in even when adjacent to ordinary text in event or diagnostic output.</summary>
    [GeneratedRegex(ClaudeSetupTokenPattern)]
    internal static partial Regex ClaudeSetupToken();

    /// <summary>Refuses a setup-token shape or terminal text whose safety cannot be established.</summary>
    internal static bool HasClaudeSetupToken(string text, Func<string, bool>? containsProvision = null)
    {
        string? normalized = NormalizeOutput(text);
        return normalized is null || UnresolvedTerminal().IsMatch(normalized) || ClaudeSetupToken().IsMatch(text)
            || CompactOutput(text, normalized).Contains(ClaudeSetupStem, StringComparison.Ordinal)
            || HasProvision(text, normalized, containsProvision);
    }

    internal static string RedactOutput(string text, string replacement = "<secret>", Func<string, bool>? containsProvision = null) =>
        RedactOutput(text, replacement, out _, containsProvision);

    internal static string RedactOutput(string text, string replacement, out int secrets, Func<string, bool>? containsProvision = null)
    {
        string? normalized = NormalizeOutput(text);
        string? compact = normalized is null ? null : CompactOutput(text, normalized);
        return RedactOutput(text, normalized, compact, replacement, out secrets, containsProvision);
    }

    private static string RedactOutput(string text, string? normalized, string? compact, string replacement, out int secrets, Func<string, bool>? containsProvision)
    {
        if (compact is null || (normalized is not null && UnresolvedTerminal().IsMatch(normalized))
            || (compact.Contains(ClaudeSetupStem, StringComparison.Ordinal)
            && (normalized != text || OutputControls().IsMatch(text) || HasSeparatedSetupTail(text)
                || compact.AsSpan().Count(ClaudeSetupStem) > ClaudeSetupToken().Count(text)))
            || (normalized is not null && normalized != text && Secret().IsMatch(normalized)))
        {
            // Raw offsets are ambiguous after decoding or joining fragments. Withhold the entire text in that case.
            secrets = 1;
            return replacement;
        }

        int count = 0;
        string safe = Secret().Replace(text, _ => { count++; return replacement; });
        if (HasProvision(safe, normalized != text ? normalized : null, containsProvision))
        {
            secrets = 1;
            return replacement;
        }
        secrets = count;
        return safe;
    }

    // Compare all 12-character windows, including the prefix. No substring of the provision is allocated or cached.
    // Searching spans compares ordinally and uses the runtime's vectorized substring search on long lines.
    internal static bool ContainsProvisionFragment(string text, ReadOnlySpan<char> provision)
    {
        ReadOnlySpan<char> output = text;
        if (output.Length < ProvisionFragmentLength) return false;
        for (int offset = 0; offset <= provision.Length - ProvisionFragmentLength; offset++)
        {
            if (output.IndexOf(provision.Slice(offset, ProvisionFragmentLength), StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }

    private static bool HasProvision(string text, string? normalized, Func<string, bool>? containsProvision) =>
        containsProvision is not null && (containsProvision(text) || normalized is not null && normalized != text && containsProvision(normalized));

    // Recognize ASCII character escapes and terminal decoration only in the comparison text. Repeating the pass handles
    // encodings nested inside one another. Beyond the bound there is no reliable comparison, so callers withhold the text.
    private static string? NormalizeOutput(string text)
    {
        if (text.AsSpan().IndexOfAny('\\', '%', '&') < 0 && !text.Contains('\u001b') && !text.Contains('\u009b')) return text;
        for (int i = 0; i < 8; i++)
        {
            string normalized = OutputEscape().Replace(text, static match =>
            {
                if (match.Groups["amp"].Success) return "&";
                if (match.Groups["space"].Success) return " ";
                if (match.Groups["terminal"].Success) return "";
                Group decimalCode = match.Groups["decimal"];
                return int.TryParse(decimalCode.Success ? decimalCode.ValueSpan : match.Groups["hex"].ValueSpan,
                    decimalCode.Success ? NumberStyles.None : NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                    && value is >= 0 and <= 127 ? ((char)value).ToString() : match.Value;
            });
            if (normalized == text) return text;
            text = normalized;
        }
        return null;
    }

    [GeneratedRegex(@"(?:(?<!\\)\\+(?:u00|x)|%)(?<hex>[0-7][a-f0-9])|&#x(?<hex>[0-9a-f]{1,6});|&#(?<decimal>[0-9]{1,7});|(?<amp>&amp;)|(?<space>(?<!\\)\\+[nrtfv])|(?<terminal>\x1b(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1b]*(?:\x07|\x1b\\)|[PX^_][^\x1b]*\x1b\\|[()][0-~]|[=>78])|\u009b[0-?]*[ -/]*[@-~])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OutputEscape();

    [GeneratedRegex(@"[\s\p{Cc}\p{Cf}]+")]
    private static partial Regex OutputSeparators();

    [GeneratedRegex(@"[\p{Cc}\p{Cf}]")]
    private static partial Regex OutputControls();

    [GeneratedRegex(@"[\x1b\u0090\u0098\u009b\u009d\u009e\u009f]")]
    private static partial Regex UnresolvedTerminal();

    [GeneratedRegex(@"(?:\\{1,2}|[`^])[ \t\r]*\n")]
    private static partial Regex PhysicalOutputContinuation();

    // Encodings and a line wrap can split the escape itself. The normal pass above preserves boundaries for matching
    // other credential shapes; only the distinctive setup-token prefix is sought after removing those boundaries.
    private static string CompactOutput(string text, string normalized)
    {
        string compact = OutputSeparators().Replace(normalized, "");
        if (compact.Contains(ClaudeSetupPrefix, StringComparison.Ordinal) || !text.Contains('\n'))
            return compact;
        if (text.AsSpan().IndexOfAny('\\', '%', '&') >= 0)
        {
            string joined = OutputSeparators().Replace(text, "");
            if (NormalizeOutput(joined) is string decoded)
            {
                compact = OutputSeparators().Replace(decoded, "");
                if (compact.Contains(ClaudeSetupPrefix, StringComparison.Ordinal)) return compact;
            }
        }
        string unwrapped = PhysicalOutputContinuation().Replace(normalized, "");
        return unwrapped == normalized ? compact : OutputSeparators().Replace(unwrapped, "");
    }

    [GeneratedRegex(@"(?:\\{1,8}(?:u(?:0(?:0[0-7]?)?)?|x[0-7]?)?|%[0-7]?|&(?:a(?:m(?:p)?)?|#(?:[0-9]{0,7}|x[0-9a-f]{0,6}))?)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IncompleteOutputEscape();

    private static bool HasSeparatedSetupTail(string text)
    {
        ReadOnlySpan<char> rest = text.AsSpan();
        while (true)
        {
            int prefix = rest.IndexOf(ClaudeSetupPrefix, StringComparison.Ordinal);
            if (prefix < 0) return false;
            rest = rest[(prefix + ClaudeSetupPrefix.Length)..];
            int end = rest.IndexOfAnyExcept(TokenCharacters);
            if (end < 0) return false;
            rest = rest[end..];
            if (char.IsWhiteSpace(rest[0]))
            {
                rest = rest.TrimStart();
                if (!rest.IsEmpty && TokenCharacters.Contains(rest[0])) return true;
            }
        }
    }

    // Process physical lines in order. Only a partial setup-token prefix at the end of a line needs lookahead.
    // Once the prefix is known, subsequent token-like text may be continuation and is withheld until a delimiter.
    internal sealed class OutputRedactor(string replacement = "<secret>", Func<string, bool>? containsProvision = null)
    {
        private readonly List<(string Text, int Secrets)> pending = [];
        private string prefix = "";
        private bool continuation;
        private bool unresolvedTerminal;

        internal IEnumerable<(string Text, int Secrets)> Push(string line) => Push(line, withhold: false);

        internal IEnumerable<(string Text, int Secrets)> Push(string line, bool withhold)
        {
            string? normalized = NormalizeOutput(line);
            string? compact = normalized is null ? null : CompactOutput(line, normalized);
            if (prefix.Length > 0)
            {
                string? joined = compact is not null && NormalizeOutput(prefix + compact) is string decoded
                    ? OutputSeparators().Replace(decoded, "") : null;
                // A trailing marker can be a physical line continuation. Preserve a valid join of a split escape.
                int markerLength = ContinuationMarkerLength(prefix);
                if (markerLength > 0 && joined is not null && !joined.StartsWith(ClaudeSetupPrefix, StringComparison.Ordinal))
                {
                    string physical = prefix[..^markerLength] + compact;
                    if (physical.StartsWith(ClaudeSetupPrefix, StringComparison.Ordinal) || IncompletePrefix(physical) == physical)
                        joined = physical;
                }
                if (joined is null || joined.StartsWith(ClaudeSetupPrefix, StringComparison.Ordinal))
                {
                    foreach (var _ in pending) yield return (replacement, 1);
                    pending.Clear();
                    prefix = "";
                    continuation = joined is null || Continues(joined, ClaudeSetupPrefix.Length);
                    yield return (replacement, 1);
                    yield break;
                }

                if (joined.Length > 0 && IncompletePrefix(joined) == joined)
                {
                    prefix = joined;
                    string pendingText = RedactOutput(line, normalized, compact, replacement, out int pendingSecrets, containsProvision);
                    pending.Add(withhold ? (replacement, 1) : (pendingText, pendingSecrets));
                    // Blank lines can extend lookahead indefinitely. Bound it while keeping the candidate prefix in memory.
                    if (pending.Count == ClaudeSetupPrefix.Length)
                    {
                        foreach (var _ in pending) yield return (replacement, 1);
                        pending.Clear();
                    }
                    yield break;
                }

                foreach (var item in Complete()) yield return item;
            }

            bool wasContinuation = continuation;
            bool wasUnresolvedTerminal = unresolvedTerminal;
            // A terminal sequence can itself cross a physical line. Withhold the next fragment when its end is unclear.
            unresolvedTerminal = normalized is null || UnresolvedTerminal().IsMatch(normalized)
                || (wasUnresolvedTerminal && compact is { Length: 0 });
            int start = compact?.LastIndexOf(ClaudeSetupPrefix, StringComparison.Ordinal) ?? -1;
            continuation = compact is null
                || (start >= 0 && Continues(compact, start + ClaudeSetupPrefix.Length))
                || (wasContinuation && Continues(compact, 0))
                || (wasUnresolvedTerminal && compact is { Length: > 0 } && TokenCharacters.Contains(compact[^1]));

            string safe = RedactOutput(line, normalized, compact, replacement, out int secrets, containsProvision);
            if (withhold || wasUnresolvedTerminal
                || (wasContinuation && !string.IsNullOrEmpty(compact) && (TokenCharacters.Contains(compact[0]) || compact[0] is '\\' or '%' or '&')))
            {
                safe = replacement;
                secrets = 1;
            }

            string candidate = compact is null ? "" : IncompletePrefix(compact);
            if (candidate.Length > 0)
            {
                prefix = candidate;
                pending.Add((safe, secrets));
                yield break;
            }

            yield return (safe, secrets);
        }
        internal IEnumerable<(string Text, int Secrets)> Complete()
        {
            bool distinctive = prefix.StartsWith("sk-" + "ant-", StringComparison.Ordinal);
            foreach (var item in pending) yield return distinctive ? (replacement, 1) : item;
            pending.Clear();
            prefix = "";
        }

        private static string IncompletePrefix(string text)
        {
            Match escape = IncompleteOutputEscape().Match(text);
            int end = escape.Success ? escape.Index : text.Length - ContinuationMarkerLength(text);
            int length = Math.Min(ClaudeSetupPrefix.Length - 1, end);
            while (length > 0 && !text.AsSpan(end - length, length).SequenceEqual(ClaudeSetupPrefix.AsSpan(0, length))) length--;
            if (length == 0 && !escape.Success) return "";
            return text[(end - length)..];
        }

        private static int ContinuationMarkerLength(string text)
        {
            if (text.EndsWith("\\\\", StringComparison.Ordinal)) return 2;
            return text.Length > 0 && text[^1] is '\\' or '^' or '`' ? 1 : 0;
        }

        private static bool Continues(string text, int start)
        {
            Match escape = IncompleteOutputEscape().Match(text, start);
            int end = escape.Success ? escape.Index : text.Length;
            return text.AsSpan(start, end - start).IndexOfAnyExcept(TokenCharacters) < 0;
        }

    }

    /// <summary>
    /// One element of inline data on a line, a data: URI from its scheme to the end of its data (FIX-172): a credential-shaped match is
    /// placed by the ordinal of the element it lies in, so that a reviewer finds it among several on one long line. The scheme counts only
    /// where no letter, digit, plus, dot or hyphen comes before it, so that "metadata:" is none; the data ends at whitespace, a quote, an
    /// angle bracket, a parenthesis or a backslash, as it does in an attribute, a style, a link or a JSON string.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9+.-])(?i:data):[A-Za-z0-9!#$&^_.+-]*(?:/[A-Za-z0-9!#$&^_.+-]+)?(?:;[A-Za-z0-9!#$&^_.+=-]+)*,[^\s""'<>()\\]*")]
    internal static partial Regex InlineData();

    /// <summary>A property name that holds a secret value.</summary>
    [GeneratedRegex(@"^(?i)(secret|token|password|passphrase|api[_-]?key|private[_-]?key|access[_-]?key|client[_-]?secret)$")]
    internal static partial Regex SecretKeyName();
}
