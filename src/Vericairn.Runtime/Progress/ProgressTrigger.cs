// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Progress;

/// <summary>
/// From 0.9.4 (WATCH-06): one trigger of a progress target, read from the event journal (decision 01M784PSWP4N4J9EKZQ3PN6X8G): an
/// event kind and filters that narrow it, and optionally a condition the records must show. An event is a hint, never authority: the
/// envelope filters choose which events wake the target, and a condition is read from the records before anything is placed.
/// </summary>
/// <param name="Kind">The event kind: record, attention, ask, step, request, due, run or presence.</param>
/// <param name="Types">Subject types, such as record types; empty admits every type.</param>
/// <param name="Changes">Changes the event names; empty admits every change.</param>
/// <param name="Categories">Attention categories; empty admits every category, and none.</param>
/// <param name="To">The principal concerned: operator, person, agent, service or a principal's id; null admits anyone. With the waiting
/// condition, the principal that is waited on.</param>
/// <param name="When">The condition: state-changed (a record whose status differs from the revision it supersedes), merged (a
/// change-merge operation observed successful), or waiting (a step, operator request or ask that now waits on its principal and did not
/// when the target last looked); null for none.</param>
internal sealed record ProgressTrigger(string Kind, ImmutableArray<string> Types, ImmutableArray<string> Changes, ImmutableArray<string> Categories, string? To, string? When)
{
    /// <summary>The conditions a trigger may name.</summary>
    internal static readonly ImmutableArray<string> Conditions = ["state-changed", "merged", "waiting"];

    /// <summary>The kinds of principal a trigger's to may name, besides a principal's id.</summary>
    internal static readonly ImmutableArray<string> Principals = ["operator", "person", "agent", "service"];

    /// <summary>The event kinds whose subject can wait on a principal.</summary>
    internal static readonly ImmutableArray<string> Waiters = ["step", "request", "ask"];

    /// <summary>The named triggers, each the text form it stands for.</summary>
    internal static readonly ImmutableSortedDictionary<string, string> Named = new Dictionary<string, string>
    {
        ["release-state"] = "record type=release when=state-changed",
        ["work-order-merged"] = "record type=operation when=merged",
        ["result-or-acceptance"] = "record type=acceptance,result",
        ["step-waiting-on-person"] = "step to=person when=waiting",
        ["step-waiting-on-operator"] = "step to=operator when=waiting",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>
    /// What refreshes a target that names nothing: a release's state change, a merged work order, a recorded result or acceptance and a
    /// step that waits on a person.
    /// </summary>
    internal static readonly ImmutableArray<ProgressTrigger> DefaultRefresh =
        [Parse("release-state"), Parse("work-order-merged"), Parse("result-or-acceptance"), Parse("step-waiting-on-person")];

    /// <summary>What adds a comment to a target that names nothing: a step that waits on the operator and a release's state change.</summary>
    internal static readonly ImmutableArray<ProgressTrigger> DefaultComment = [Parse("step-waiting-on-operator"), Parse("release-state")];

    /// <summary>A trigger's text form, or one of the named triggers; throws <see cref="FormatException"/> with what is wrong.</summary>
    internal static ProgressTrigger Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] words = (Named.TryGetValue(text.Trim(), out string? named) ? named : text).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) throw new FormatException("A trigger names an event kind, or is one of " + string.Join(", ", Named.Keys) + ".");
        string kind = words[0];
        if (!EventVocabulary.Kinds.Contains(kind, StringComparer.Ordinal))
            throw new FormatException($"'{kind}' is neither an event kind ({string.Join(", ", EventVocabulary.Kinds)}) nor a named trigger ({string.Join(", ", Named.Keys)}).");
        ImmutableArray<string> types = [], changes = [], categories = [];
        string? to = null, when = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string word in words.Skip(1))
        {
            int equals = word.IndexOf('=', StringComparison.Ordinal);
            string key = equals < 0 ? word : word[..equals];
            string value = equals < 0 ? string.Empty : word[(equals + 1)..];
            if (!seen.Add(key)) throw new FormatException($"A trigger names {key} once.");
            ImmutableArray<string> values = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            if (values.Length == 0) throw new FormatException($"'{word}' names no value; write {key}=<value>.");
            switch (key)
            {
                case "type":
                    types = values.All(v => v.Length <= 64 && Patterns.Slug.IsMatch(v)) ? values : throw new FormatException("A type is a lower-case kebab-case record type or subject kind.");
                    break;
                case "change":
                    changes = values.All(v => EventVocabulary.Changes.Contains(v, StringComparer.Ordinal)) ? values : throw new FormatException("A change is one of " + string.Join(", ", EventVocabulary.Changes) + ".");
                    break;
                case "category":
                    categories = values.All(v => EventVocabulary.Categories.Contains(v, StringComparer.Ordinal)) ? values : throw new FormatException("A category is one of " + string.Join(", ", EventVocabulary.Categories) + ".");
                    break;
                case "to":
                    to = values.Length == 1 && (Principals.Contains(values[0], StringComparer.Ordinal) || Records.Identity.Ulid.IsValid(values[0])) ? values[0]
                        : throw new FormatException("to names one of " + string.Join(", ", Principals) + ", or one principal's id.");
                    break;
                case "when":
                    when = values.Length == 1 && Conditions.Contains(values[0], StringComparer.Ordinal) ? values[0] : throw new FormatException("when names one of " + string.Join(", ", Conditions) + ".");
                    break;
                default:
                    throw new FormatException($"A trigger's filters are type, change, category, to and when, not '{key}'.");
            }
        }

        if (when is "state-changed" or "merged" && kind != "record") throw new FormatException($"when={when} reads a record, so its kind is record.");
        if (when == "waiting" && !Waiters.Contains(kind, StringComparer.Ordinal)) throw new FormatException("when=waiting reads what waits on a principal, so its kind is step, request or ask.");
        return new ProgressTrigger(kind, types, changes, categories, to, when);
    }

    /// <summary>Whether an event's envelope wakes this trigger; a condition is then read from the records.</summary>
    internal bool Admits(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Kind == Kind
            && (Types.IsEmpty || Types.Contains(envelope.Subject.Type, StringComparer.Ordinal))
            && (Changes.IsEmpty || Changes.Contains(envelope.Change, StringComparer.Ordinal))
            && (Categories.IsEmpty || envelope.Category is not null && Categories.Contains(envelope.Category, StringComparer.Ordinal));
    }

    /// <summary>The text form, which <see cref="Parse"/> reads back to an equal trigger.</summary>
    public override string ToString()
    {
        var text = new StringBuilder(Kind);
        if (!Types.IsEmpty) text.Append(" type=").Append(string.Join(',', Types));
        if (!Changes.IsEmpty) text.Append(" change=").Append(string.Join(',', Changes));
        if (!Categories.IsEmpty) text.Append(" category=").Append(string.Join(',', Categories));
        if (To is not null) text.Append(" to=").Append(To);
        if (When is not null) text.Append(" when=").Append(When);
        return text.ToString();
    }

    /// <summary>Equal when the text forms are equal.</summary>
    public bool Equals(ProgressTrigger? other) => other is not null && ToString() == other.ToString();

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());
}
