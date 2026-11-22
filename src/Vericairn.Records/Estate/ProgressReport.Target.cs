// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (WATCH-06): what a progress target carries, composed by the runtime and never by an adapter: its form, its text, and
/// every record whose text the text carries, which an outward effect is charged with (design/holds-reads-sends.md § 7).
/// </summary>
/// <param name="Form">private or public.</param>
/// <param name="Text">The Markdown the target is given.</param>
/// <param name="Carried">Each record whose text it carries, once, in the order the text first carries it.</param>
internal sealed record ProgressPayload(string Form, string Text, IReadOnlyList<RecordReference> Carried)
{
    /// <summary>The keys of the work orders the public form left out because a disclosure rule holds them; never part of the text.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];
}

public static partial class ProgressReport
{
    /// <summary>The form of a target that only the estate's own people read: the report's sections as the records give them.</summary>
    internal const string PrivateForm = "private";

    /// <summary>The form of a target anyone may read: the same sections with nothing a disclosure rule holds.</summary>
    internal const string PublicForm = "public";

    /// <summary>The forms a progress target takes.</summary>
    internal static readonly ImmutableArray<string> TargetForms = [PrivateForm, PublicForm];

    /// <summary>The most rows one table of a target carries; the rest are counted, and vericairn progress shows them.</summary>
    internal const int TargetRows = 150;

    /// <summary>The most published releases a target lists, newest first.</summary>
    internal const int PublishedRows = 12;

    /// <summary>The most asks the private form lists with their question and recommendation, and from 0.9.4 (OPS-22) the most possible todos.</summary>
    internal const int AskRows = 20;

    /// <summary>
    /// The progress report's sections for a progress target, as Markdown, from the same derivation as <see cref="Render"/>: the releases,
    /// the next release's work orders by state, the work in flight, the milestones and what waits on the operator, with, from 0.9.4
    /// (OPS-22), the possible todos, the text ending with <paramref name="marker"/> as an HTML comment, by which an adapter finds the
    /// place it keeps. The private form carries what the report carries for these sections. The public form follows the disclosure
    /// rules: no work order held from disclosure, nor any line whose text would disclose one or carry a blocked term, a record
    /// identifier, a machine path, a credential or a contact address; no principal's name, no record identifier, and of what waits on
    /// the operator only how many, never an ask's or a possible todo's text. Every count in the public form counts only what it shows.
    /// Its lines are screened as of <paramref name="screenedAt"/>, when the estate's blocked terms are read; <see cref="TargetRefusal"/>
    /// checks the whole text at the same time before anything is placed.
    /// </summary>
    internal static ProgressPayload RenderTarget(EstateModel model, string form, string? title, string marker, DateTimeOffset screenedAt)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(marker);
        if (!TargetForms.Contains(form, StringComparer.Ordinal)) throw new ArgumentException("A progress target's form is private or public.", nameof(form));
        bool open = form == PublicForm;
        Report report = Take(model, null);
        DateTimeOffset at = FlowStops.RecordedTime(model);
        IReadOnlyList<DisclosureHolds.Hold> holds = DisclosureHolds.Active(model);
        var carried = new Carrying();
        var leftOut = new SortedSet<string>(StringComparer.Ordinal);

        // A line the public form may carry: its own text passes every check the whole text must pass.
        var screen = new Screen(model, PublicForm, screenedAt);
        bool Clear(string line) => screen.Refusal(line) is null;
        bool Held(RecordEnvelope order) => holds.Any(h => model.Chain(order).Contains(h.WorkOrder, StringComparer.Ordinal));
        bool Shown(OrderLine order)
        {
            if (!open) return true;
            if (!Held(order.Order) && Clear(order.Item.Key + " " + order.Item.Title)) return true;
            leftOut.Add(order.Item.Key);
            return false;
        }

        string heading = title ?? "Progress of " + (report.Charter is null ? "the estate" : EstateModel.BodyString(report.Charter, "name"));
        if (title is null) carried.Add(report.Charter);
        var text = new StringBuilder();
        text.Append("# ").Append(Cell(heading)).Append("\n\n");
        text.Append("_Vericairn renders this from the estate's records, as of ").Append(at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" UTC, and replaces it at each refresh. ")
            .Append(open ? "This is the public form, which leaves out what the disclosure rules hold." : "This is the private form, for the estate's own people.")
            .Append("_\n");

        // The releases: each release work order not yet accepted, with how many of the work orders it carries are accepted, then the
        // published releases, newest first.
        var releases = new List<string[]>();
        List<Upcoming> upcoming = [.. report.Upcoming.Where(u => Shown(u.Order))];
        foreach (Upcoming release in upcoming)
        {
            List<OrderLine> shown = [.. release.Carried.Where(Shown)];
            releases.Add([release.Order.Item.Key + " " + release.Order.Item.Title, release.Order.Status, $"{shown.Count(c => c.Status == "accepted")} of {shown.Count} accepted", "—"]);
            carried.Add(release.Order.Order);
        }

        foreach (ReleaseLine line in report.Releases.AsEnumerable().Reverse().Take(PublishedRows))
        {
            RecordEnvelope release = line.Published.Release;
            string identity = release.Subject is null ? string.Empty : LivingDocuments.ProductIdentity(model, release.Subject.Id);
            RecordEnvelope? product = model.ById.TryGetValue(identity, out RecordEnvelope? first) ? model.Latest(first) : null;
            string name = (product is null ? string.Empty : EstateModel.BodyString(product, "name") + " ") + EstateModel.BodyString(release, "version");
            if (open && !Clear(name)) continue;
            int accepted = line.Accepted.Count(a => !open || a.Order is null || !Held(a.Order));
            string date = EstateModel.BodyString(release, "publishedAt") is { Length: >= 10 } published ? published[..10] : "?";
            releases.Add([name, "published", Count(accepted, "work order") + " accepted", date]);
            carried.Add(product);
            carried.Add(release);
        }

        text.Append("\n## Releases\n\n");
        Table(text, ["Release", "State", "Work orders", "Published"], releases, "No release is recorded.");

        // The next release: the first active release work order, else the first waiting for its acceptance, and its work orders by state.
        Upcoming? next = upcoming.FirstOrDefault(u => u.Order.Status == "active") ?? upcoming.FirstOrDefault();
        List<string> vocabulary = [.. GoalPredicates.States["work-order"]];
        if (next is null)
        {
            text.Append("\n## Next release\n\nNo release work order waits for its acceptance.\n");
        }
        else
        {
            List<OrderLine> orders = [.. next.Carried.Where(Shown)
                .OrderBy(o => vocabulary.IndexOf(o.Status) is int rank && rank >= 0 ? rank : vocabulary.Count).ThenBy(o => o.Status, StringComparer.Ordinal).ThenBy(o => o.Item.Key, StringComparer.Ordinal)];
            text.Append("\n## Next: ").Append(Cell(next.Order.Item.Key + " " + next.Order.Item.Title)).Append(", ")
                .Append(orders.Count(o => o.Status == "accepted").ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(Count(orders.Count, "work order")).Append(" accepted\n\n");
            Table(text, ["Work order", "What", "State"], [.. orders.Select(o => new[] { o.Item.Key, o.Item.Title, o.Status })], "It carries no work order yet.");
            foreach (OrderLine order in orders) carried.Add(order.Order);
        }

        // The work in flight: each assignment with no result yet, or a result awaiting review, of a work order not accepted or retired.
        var flight = new List<string[]>();
        var seen = new HashSet<(string, string)>();
        foreach (WorkView.AssignmentItem assignment in report.Assignments.Where(a => a.State is "no attempt" or "in progress" or "awaiting review"))
        {
            OrderLine? order = report.Orders.FirstOrDefault(o => o.Item.Key == assignment.Key);
            if (order is null || order.Status is "accepted" or "retired" || !Shown(order)) continue;
            if (open)
            {
                if (seen.Add((order.Item.Key, assignment.State))) flight.Add([order.Item.Key, order.Item.Title, assignment.State]);
            }
            else
            {
                flight.Add([order.Item.Key, order.Item.Title, assignment.State, assignment.ContributorName]);
                carried.Add(model.CurrentPrincipal(assignment.ContributorId));
            }

            carried.Add(order.Order);
        }

        text.Append("\n## In flight\n\n");
        Table(text, open ? ["Work order", "What", "State"] : ["Work order", "What", "State", "Who"], flight, "Nothing is in flight.");

        // The milestones, with the state status derives.
        List<OrderLine> milestones = [.. report.Orders.Where(o => o.Item.Kind == "milestone" && Shown(o))];
        text.Append("\n## Milestones\n\n");
        Table(text, ["Milestone", "What", "State"], [.. milestones.Select(o => new[] { o.Item.Key, o.Item.Title, o.Status })], "The estate records no milestone.");
        foreach (OrderLine milestone in milestones) carried.Add(milestone.Order);

        // What waits on the operator: the private form as the report gives it; the public form counts only, since an open ask is not
        // public before its answer is. From 0.9.4 (OPS-22) the possible todos follow, listed in the private form and counted in the
        // public one, only when one exists.
        Acceptances due = report.Acceptances;
        int acceptances = due.Submitted.Count + due.Results.Count + due.Candidates.Count + due.Goals.Count;
        text.Append("\n## What waits on the operator\n\n");
        if (open)
        {
            text.Append("- Asks awaiting an answer: ").Append(report.Asks.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("- Acceptances only the operator gives: ").Append(acceptances.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (report.Todos.Count > 0) text.Append("- Possible todos: ").Append(report.Todos.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        else
        {
            text.Append("- **Asks awaiting an answer: ").Append(report.Asks.Count.ToString(CultureInfo.InvariantCulture)).Append("**\n");
            foreach (AskLine ask in report.Asks.Take(AskRows))
            {
                text.Append("  - ").Append(Cell(EstateModel.BodyString(ask.Ask, "question"))).Append(" (to ").Append(Cell(ask.ToName)).Append(", ").Append(ask.State)
                    .Append(")\n    recommendation: ").Append(Cell(EstateModel.BodyString(ask.Ask, "recommendation"))).Append('\n');
                carried.Add(ask.Ask);
                carried.Add(model.CurrentPrincipal(ask.To));
            }

            if (report.Asks.Count > AskRows) text.Append("  - and ").Append((report.Asks.Count - AskRows).ToString(CultureInfo.InvariantCulture)).Append(" more, which vericairn ask shows\n");
            text.Append("- **Acceptances only the operator gives: ").Append(acceptances.ToString(CultureInfo.InvariantCulture)).Append("**\n");
            foreach (OrderLine order in due.Submitted)
            {
                text.Append("  - milestone ").Append(Cell(order.Item.Key + " " + order.Item.Title)).Append(", submitted\n");
                carried.Add(order.Order);
            }

            foreach (ResultDue result in due.Results)
            {
                text.Append("  - a result toward ").Append(Cell(result.Order.Item.Key + " " + result.Order.Item.Title)).Append(", ").Append(Cell(result.Assignment.Outcome))
                    .Append(" by ").Append(Cell(result.Assignment.ContributorName)).Append('\n');
                carried.Add(result.Order.Order);
                carried.Add(result.Result);
                carried.Add(model.CurrentPrincipal(result.Assignment.ContributorId));
            }

            foreach (RecordEnvelope candidate in due.Candidates)
            {
                text.Append("  - release candidate ").Append(Cell(EstateModel.BodyString(candidate, "version"))).Append(" from commit ")
                    .Append(EstateModel.BodyString(candidate, "commit") is { Length: >= 12 } c ? c[..12] : "?").Append('\n');
                carried.Add(candidate);
            }

            foreach (GoalDue goal in due.Goals)
            {
                text.Append("  - goal ").Append(Cell(EstateModel.BodyString(goal.Goal, "title"))).Append(", accepted by ").Append(Cell(goal.Accepter)).Append('\n');
                carried.Add(goal.Goal);
            }

            if (report.Todos.Count > 0)
            {
                text.Append("- **Possible todos: ").Append(report.Todos.Count.ToString(CultureInfo.InvariantCulture)).Append("**, which hold no release's closing and no goal's evaluation\n");
                foreach (RecordEnvelope todo in report.Todos.Take(AskRows))
                {
                    string? to = EstateModel.BodyString(todo, "to");
                    text.Append("  - ").Append(Cell(EstateModel.BodyString(todo, "action"))).Append(" (to ").Append(Cell(Name(model, to)))
                        .Append(")\n    recommendation: ").Append(Cell(EstateModel.BodyString(todo, "recommendation"))).Append('\n');
                    carried.Add(todo);
                    carried.Add(model.CurrentPrincipal(to));
                }

                if (report.Todos.Count > AskRows) text.Append("  - and ").Append((report.Todos.Count - AskRows).ToString(CultureInfo.InvariantCulture)).Append(" more, which vericairn operator list shows\n");
            }
        }

        text.Append("\n<!-- ").Append(marker).Append(" -->\n");
        return new ProgressPayload(form, text.ToString(), carried.References) { LeftOut = [.. leftOut] };
    }

    /// <summary>
    /// Why a target may not be given a text, or null when it may: in every form, a credential, a machine path or a contact address; in
    /// the public form also anything a disclosure hold in force holds (a held work order's key or distinctive phrase), a blocked term of
    /// the estate's policies at <paramref name="at"/>, or a record identifier. The text is read as written and with its Markdown escapes
    /// removed, so an escape cannot hide a match.
    /// </summary>
    internal static string? TargetRefusal(EstateModel model, string form, string text, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        return new Screen(model, form, at).Refusal(text);
    }

    /// <summary>
    /// A value as one Markdown table cell or list item: on one line, its Markdown punctuation escaped so it cannot format, link or hide
    /// text, and an at sign kept from mentioning anyone. An empty value is a dash.
    /// </summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        var cell = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            if (c is '\r' or '\n' or '\t') cell.Append(' ');
            else if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '|' or '~') cell.Append('\\').Append(c);
            else if (c == '@') cell.Append('@').Append('⁠');
            else cell.Append(c);
        }

        return cell.ToString();
    }

    // The text with the escapes Cell adds removed.
    private static string Plain(string text) => EscapedPunctuation().Replace(text, "$1").Replace("⁠", string.Empty, StringComparison.Ordinal);

    private static void Table(StringBuilder text, string[] head, List<string[]> rows, string empty)
    {
        if (rows.Count == 0)
        {
            text.Append(empty).Append('\n');
            return;
        }

        text.Append("| ").Append(string.Join(" | ", head)).Append(" |\n|").Append(string.Concat(Enumerable.Repeat(" --- |", head.Length))).Append('\n');
        foreach (string[] row in rows.Take(TargetRows))
        {
            text.Append("| ").Append(string.Join(" | ", row.Select(Cell))).Append(" |\n");
        }

        if (rows.Count > TargetRows) text.Append("\nand ").Append((rows.Count - TargetRows).ToString(CultureInfo.InvariantCulture)).Append(" more, which vericairn progress shows.\n");
    }

    [GeneratedRegex(@"\\([\\`*_\[\]<>|~])")]
    private static partial Regex EscapedPunctuation();

    // A record's identifier: a ULID standing alone.
    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-7][0-9A-HJKMNP-TV-Z]{25}(?![0-9A-Za-z])")]
    private static partial Regex RecordIdentifier();

    // An e-mail address.
    [GeneratedRegex(@"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?![A-Za-z0-9-])")]
    private static partial Regex ContactAddress();

    /// <summary>The checks of <see cref="TargetRefusal"/> for one form, reading the holds and blocked terms once for many texts.</summary>
    private sealed class Screen(EstateModel model, string form, DateTimeOffset at)
    {
        private IReadOnlyList<DisclosureHolds.Hold>? holds;
        private ImmutableArray<string>? terms;

        public string? Refusal(string text)
        {
            foreach (string candidate in new[] { text, Plain(text) })
            {
                if (SecretShapes.Secret().IsMatch(candidate)) return "a credential";
                if (SecretShapes.AbsolutePath().IsMatch(candidate)) return "a machine path";
                if (ContactAddress().IsMatch(candidate)) return "a contact address";
                if (form != PublicForm) continue;
                if (DisclosureHolds.Disclosed(holds ??= DisclosureHolds.Active(model), candidate) is string key) return $"held work ({key}) before the release that carries it is published";
                if (BlockedTerms.Introduced(terms ??= BlockedTerms.In(model, at), string.Empty, candidate) is string term) return $"the blocked term \"{term}\"";
                if (RecordIdentifier().IsMatch(candidate)) return "a record identifier";
            }

            return null;
        }
    }

    /// <summary>The records a payload carries, each once, in the order the text first carries it.</summary>
    private sealed class Carrying
    {
        private readonly HashSet<string> ids = new(StringComparer.Ordinal);
        private readonly List<RecordReference> references = [];

        public IReadOnlyList<RecordReference> References => references;

        public void Add(RecordEnvelope? record)
        {
            if (record is not null && ids.Add(record.Id)) references.Add(new RecordReference(record.Type, record.Id, record.ComputedHash));
        }
    }
}
