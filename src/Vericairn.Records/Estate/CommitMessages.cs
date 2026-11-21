// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The estate's check of the commit messages a checked push carries (CODE-81): every message, trailers included, is matched against
/// the estate's blocked terms (<see cref="BlockedTerms"/>), and a match refuses the push or, where an estate policy of
/// <see cref="Kind"/> sets <c>blockedTerms</c> to <c>warn</c>, warns; and every pushed commit carries each exact trailer line such a
/// policy requires, refused when it is missing or altered. A checked merge matches the message of the merge commit it would create
/// against the same terms at the same level. A finding names the commit and the policy, never the matched text or the trailer, so
/// that the operation recording it never repeats a term.
/// </summary>
internal static partial class CommitMessages
{
    /// <summary>The estate-policy rule kind that configures the level and the required trailers.</summary>
    internal const string Kind = "vericairn.estate/commit-messages";

    /// <summary>The level at which a blocked term in a commit message refuses: the default.</summary>
    internal const string Refuse = "refuse";

    /// <summary>The level at which a blocked term in a commit message is reported and the change goes ahead.</summary>
    internal const string Warn = "warn";

    /// <summary>The longest trailer line a policy may require.</summary>
    internal const int MaximumTrailer = 500;

    /// <summary>A finding for a blocked term in a message.</summary>
    internal const string Term = "blocked-term";

    /// <summary>A finding for a required trailer line a message lacks.</summary>
    internal const string Missing = "trailer-missing";

    /// <summary>A finding for a trailer of the required line's token whose text differs from it.</summary>
    internal const string Altered = "trailer-altered";

    /// <summary>One required trailer line and the identity of the policy that requires it.</summary>
    internal sealed record Trailer(string Line, string Policy)
    {
        internal string Token => Line[..Line.IndexOf(':', StringComparison.Ordinal)];
    }

    /// <summary>One blocked-terms list: the identity of the policy that declares it and its normalized terms.</summary>
    internal sealed record TermList(string Policy, ImmutableArray<string> Terms);

    /// <summary>What the estate checks: the level for blocked terms, with the policies that set it, the lists, and the required trailers.</summary>
    internal sealed record Settings(string Level, ImmutableArray<string> LevelPolicies, ImmutableArray<TermList> Lists, ImmutableArray<Trailer> Trailers)
    {
        /// <summary>Whether there is anything to check: a blocked term or a required trailer.</summary>
        internal bool Checks => !Lists.IsEmpty || !Trailers.IsEmpty;

        /// <summary>Settings that check nothing.</summary>
        internal static Settings None { get; } = new(Refuse, [], [], []);
    }

    /// <summary>
    /// One finding about one message: its commit (or what names the message, for a merge commit not yet created), its kind and the
    /// identity of the policy whose term or trailer it concerns.
    /// </summary>
    internal sealed record Finding(string Commit, string Kind, string Policy);

    /// <summary>
    /// What the estate checks now: the lists of <see cref="BlockedTerms.Lists"/>, and from each current, active policy of the estate
    /// that has started and declares a well-formed <see cref="Kind"/> rule, its level and trailer. One policy at <c>refuse</c> outweighs
    /// any at <c>warn</c>; every required trailer is required.
    /// </summary>
    internal static Settings In(EstateModel model, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);
        ImmutableArray<TermList> lists = [.. BlockedTerms.Lists(model, now).Select(l => new TermList(model.Chain(l.Policy)[0], l.Terms))];
        var refusing = new List<string>();
        var warning = new List<string>();
        var trailers = new List<Trailer>();
        foreach (RecordEnvelope policy in model.Current("estate-policy").OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (EstateModel.BodyString(policy, "state") != "active" || !AppliesToEstate(policy.Body)
                || EstateModel.BodyString(policy, "startsAt") is string starts
                    && DateTimeOffset.TryParse(starts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset start) && start > now) continue;
            string identity = model.Chain(policy)[0];
            foreach (JsonElement parameters in Rules(policy.Body))
            {
                if (Validate(parameters) is not null) continue;
                if (parameters.TryGetProperty("blockedTerms", out JsonElement level)) (level.GetString() == Warn ? warning : refusing).Add(identity);
                if (parameters.TryGetProperty("trailer", out JsonElement line) && !trailers.Any(t => t.Line == line.GetString()))
                    trailers.Add(new(line.GetString()!, identity));
            }
        }
        bool warned = warning.Count > 0 && refusing.Count == 0;
        return new(warned ? Warn : Refuse, [.. (warned ? warning : refusing).Distinct(StringComparer.Ordinal)], lists, [.. trailers]);
    }

    /// <summary>Why a rule's parameters are malformed, or null when they are well formed.</summary>
    internal static string? Validate(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return "Rule parameters must be an object.";
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (property.Name is not ("blockedTerms" or "trailer")) return $"Unknown commit-messages parameter: {property.Name}.";
        }
        bool level = parameters.TryGetProperty("blockedTerms", out JsonElement value);
        bool trailer = parameters.TryGetProperty("trailer", out JsonElement line);
        if (!level && !trailer) return "A commit-messages rule sets blockedTerms, trailer or both.";
        if (level && (value.ValueKind != JsonValueKind.String || value.GetString() is not (Refuse or Warn)))
            return "blockedTerms is refuse or warn.";
        if (trailer && (line.ValueKind != JsonValueKind.String || line.GetString()!.Length > MaximumTrailer || !TrailerLine().IsMatch(line.GetString()!)))
            return $"trailer is one exact trailer line of at most {MaximumTrailer} characters, a token, a colon, a space and its value, such as \"Signed-off-by: A Person <person@example.org>\".";
        return null;
    }

    /// <summary>
    /// The first finding that refuses the messages, if any, and every blocked-term finding the warn level lets through, commit by commit
    /// in the given order. A blocked term is found before a trailer; each exact required trailer line is exempt from the terms, since the
    /// estate requires its exact text.
    /// </summary>
    internal static (Finding? Refusal, IReadOnlyList<Finding> Warnings) Check(Settings settings, IEnumerable<(string Commit, string Message)> messages, bool trailers = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(messages);
        var warnings = new List<Finding>();
        foreach ((string commit, string message) in messages)
        {
            string[] lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).ToArray();
            string text = BlockedTerms.Normalize(string.Join('\n', lines.Where(l => !settings.Trailers.Any(t => t.Line == l))));
            if (settings.Lists.FirstOrDefault(list => list.Terms.Any(term => BlockedTerms.Count(text, term) > 0)) is TermList matched)
            {
                Finding found = new(commit, Term, matched.Policy);
                if (settings.Level != Warn) return (found, warnings);
                warnings.Add(found);
            }
            if (!trailers) continue;
            string[] block = Trailers(lines);
            foreach (Trailer required in settings.Trailers)
            {
                if (block.Contains(required.Line, StringComparer.Ordinal)) continue;
                bool altered = block.Any(l => l.StartsWith(required.Token + ":", StringComparison.OrdinalIgnoreCase));
                return (new(commit, altered ? Altered : Missing, required.Policy), warnings);
            }
        }
        return (null, warnings);
    }

    /// <summary>The sentence a finding states: it names the commit and the policy, never the term or the trailer.</summary>
    internal static string Describe(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Kind switch
        {
            Term => $"{finding.Commit} carries a term of the estate's blocked terms (estate policy {finding.Policy}) in its message, trailers included.",
            Altered => $"{finding.Commit} carries an altered form of the trailer line estate policy {finding.Policy} requires on every pushed commit.",
            _ => $"{finding.Commit} lacks the trailer line estate policy {finding.Policy} requires on every pushed commit, in its closing trailers.",
        };
    }

    // The message's trailers: the lines of its last paragraph, when it has more than its subject paragraph.
    private static string[] Trailers(string[] lines)
    {
        int end = lines.Length;
        while (end > 0 && lines[end - 1].Length == 0) end--;
        int start = end;
        while (start > 0 && lines[start - 1].Length > 0) start--;
        bool subjectOnly = !lines.Take(start).Any(l => l.Length > 0);
        return subjectOnly ? [] : lines[start..end];
    }

    private static bool AppliesToEstate(JsonElement body) =>
        body.TryGetProperty("appliesTo", out JsonElement selection) && selection.ValueKind == JsonValueKind.Object
        && selection.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "estate";

    private static IEnumerable<JsonElement> Rules(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("rules", out JsonElement rules) || rules.ValueKind != JsonValueKind.Array) yield break;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == Kind && rule.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt64(out long v) && v == 1 && rule.TryGetProperty("parameters", out JsonElement parameters)) yield return parameters;
        }
    }

    // A token of letters, digits and hyphens, a colon, one space and a value without control characters or surrounding whitespace.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*: [^\s\p{Cc}](?:[^\p{Cc}]*[^\s\p{Cc}])?$")]
    private static partial Regex TrailerLine();
}
