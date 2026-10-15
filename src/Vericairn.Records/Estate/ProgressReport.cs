// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The progress report the operator asked for (decision 01M4ZSF918H5D7N99VNMK8YK81), rendered from the records alone and the same on
/// every machine for the same records: where the estate stands, its work orders by state, each published release and what it contained,
/// what comes next by release, the next eligible work, and what waits on the operator. Every line names the records it rests on. Every
/// state it shows is the one status derives (work orders, assignments, asks), what a release contained is the release notes' own
/// derivation (<see cref="ReleaseContents"/>) and the next eligible work is <see cref="NextEligible"/>, so the report never contradicts
/// status or the notes and holds no figure the records do not hold: its numbers are counts of records, each beside its denominator.
/// </summary>
public static class ProgressReport
{
    /// <summary>The machine form's format.</summary>
    public const string Format = "vericairn.progress/1";

    /// <summary>The key prefix that marks a release work order, until goals plan releases (GOAL-06).</summary>
    public const string ReleaseKeyPrefix = "REL-";

    /// <summary>What the check covered: every record and the repository, as <c>vericairn estate check</c> runs it.</summary>
    public const string RecordsAndRepository = "records and repository";

    /// <summary>What the check covered in a generated region: every record, leaving out the repository checks, which read the region itself.</summary>
    public const string RecordsOnly = "records";

    /// <summary>
    /// The report as text. <paramref name="repository"/> holds the repository checks (<see cref="EstateChecks.Run"/>) when they ran;
    /// null leaves them out and says so, as a generated region must, since those checks read the document that carries it.
    /// </summary>
    public static string Render(EstateModel model, FindingList? repository)
    {
        Report report = Take(model, repository);
        var text = new StringBuilder();
        Header(text, report);
        Standing(text, report);
        WorkOrders(text, report);
        Releases(text, report);
        Next(text, report);
        Eligible(text, report);
        Operator(text, report);
        return text.ToString();
    }

    /// <summary>The report in canonical pretty JSON, byte-identical on every machine for the same records.</summary>
    public static string RenderJson(EstateModel model, FindingList? repository)
    {
        Report report = Take(model, repository);
        var json = new JsonObject
        {
            ["format"] = Format,
            ["estate"] = report.Charter is null ? null : new JsonObject { ["id"] = report.Charter.Id, ["name"] = EstateModel.BodyString(report.Charter, "name"), ["slug"] = EstateModel.BodyString(report.Charter, "slug") },
            ["standing"] = StandingJson(report),
            ["workOrders"] = new JsonObject
            {
                ["total"] = report.Orders.Count,
                ["states"] = new JsonArray(report.States.Select(s => (JsonNode)new JsonObject
                {
                    ["state"] = s.State,
                    ["count"] = s.Orders.Count,
                    ["workOrders"] = new JsonArray(s.Orders.Select(OrderJson).ToArray()),
                }).ToArray()),
            },
            ["releases"] = ReleasesJson(report),
            ["next"] = new JsonObject
            {
                ["source"] = "release work orders",
                ["keyPrefix"] = ReleaseKeyPrefix,
                ["releases"] = new JsonArray(report.Upcoming.Select(u => (JsonNode)new JsonObject
                {
                    ["workOrder"] = OrderJson(u.Order),
                    ["carries"] = new JsonArray(u.Carried.Select(OrderJson).ToArray()),
                    ["accepted"] = u.Carried.Count(c => c.Status == "accepted"),
                }).ToArray()),
            },
            ["eligible"] = new JsonObject
            {
                ["planned"] = report.Planned.Count,
                ["eligible"] = new JsonArray(report.Planned.Where(p => p.Item.Eligible).Select(PlannedJson).ToArray()),
                ["held"] = new JsonArray(report.Planned.Where(p => !p.Item.Eligible).Select(PlannedJson).ToArray()),
            },
            ["operator"] = OperatorJson(report),
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static Report Take(EstateModel model, FindingList? repository)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Where the estate stands: its pin and format as the manifest states them, the records that set them, and the check.
        EstateManifest? manifest = model.Manifest;
        List<RecordEnvelope> migrations = model.Current("migration").OrderBy(m => m.At, StringComparer.Ordinal).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
        RecordEnvelope? pinned = manifest is { Mode: "release" }
            ? model.Current("release").Where(r => EstateModel.BodyString(r, "version") == manifest.Version && EstateModel.BodyString(r, "repository") == manifest.Repository).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault()
            : null;
        RecordEnvelope? pinMigration = manifest is null ? null : migrations.LastOrDefault(m => EstateModel.BodyString(m, "toPin") == manifest.Describe());
        RecordEnvelope? formatMigration = manifest is null ? null : migrations.LastOrDefault(m => Number(m.Body, "toFormat") == manifest.RecordFormat && Number(m.Body, "fromFormat") != manifest.RecordFormat);
        var findings = new FindingList();
        foreach (Finding finding in repository is null ? model.Findings : model.Findings.Concat(repository))
        {
            findings.Add(finding);
        }

        // The work orders, each with the state status derives and the record that state rests on.
        Dictionary<string, OrderLine> orders = EstateStatus.WorkItems(model)
            .Select(i => new OrderLine(i, model.ById[i.Id], EstateStatus.StatusRecord(model, model.ById[i.Id])))
            .ToDictionary(o => o.Item.Id, StringComparer.Ordinal);
        List<OrderLine> sorted = orders.Values.OrderBy(o => o.Item.Key, StringComparer.Ordinal).ThenBy(o => o.Item.Id, StringComparer.Ordinal).ToList();
        List<string> vocabulary = [.. GoalPredicates.States["work-order"]];
        var states = sorted.GroupBy(o => o.Status, StringComparer.Ordinal)
            .OrderBy(g => vocabulary.IndexOf(g.Key) is int rank && rank >= 0 ? rank : vocabulary.Count).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new StateGroup(g.Key, g.ToList())).ToList();

        // Each published release and what it contained: the release notes' derivation over the window from the publication before it.
        var releases = ReleaseContents.PublishedReleases(model).Select(p =>
        {
            (IReadOnlyList<ReleaseContents.Accepted> accepted, IReadOnlyList<RecordEnvelope> notAccepted) = ReleaseContents.Results(model, p.Window);
            return new ReleaseLine(p, accepted, notAccepted.Select(r => (r, ReleaseContents.OrderOf(model, r))).ToList());
        }).ToList();

        // What comes next: each release work order not yet accepted, and the work orders among its inputs, until goals plan releases.
        var upcoming = sorted
            .Where(o => o.Item.Key.StartsWith(ReleaseKeyPrefix, StringComparison.Ordinal) && o.Status is not ("accepted" or "retired"))
            .Select(o => new Upcoming(o, Carried(model, o.Order, orders)))
            .ToList();

        // The next eligible work, with the current assignments of each planned work order.
        IReadOnlyList<WorkView.AssignmentItem> assignments = WorkView.AssignmentItems(model);
        var planned = NextEligible.Derive(model).Select(item =>
        {
            IReadOnlyList<string> chain = model.Chain(item.Order);
            return new Planned(orders[item.Order.Id], item, assignments.Where(a => model.ById.TryGetValue(a.AssignmentId, out RecordEnvelope? assignment) && assignment.Subject is not null && chain.Contains(assignment.Subject.Id)).ToList());
        }).ToList();

        return new Report(model, model.CurrentEstate, manifest, pinned, pinMigration, formatMigration, repository is null ? RecordsOnly : RecordsAndRepository, findings,
            sorted, states, model.Current("release").Count(), releases, upcoming, planned, Asked(model), Due(model, orders, assignments));
    }

    /// <summary>The work orders a release work order carries: those among its inputs, each once, in the order it names them.</summary>
    private static List<OrderLine> Carried(EstateModel model, RecordEnvelope release, Dictionary<string, OrderLine> orders)
    {
        var carried = new List<OrderLine>();
        if (!release.Body.TryGetProperty("inputs", out JsonElement inputs) || inputs.ValueKind != JsonValueKind.Array)
        {
            return carried;
        }

        foreach (JsonElement input in inputs.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
        {
            if (Text(input, "type") == "work-order" && Text(input, "id") is string id && model.ById.TryGetValue(id, out RecordEnvelope? order) && order.Type == "work-order"
                && orders.TryGetValue(model.Latest(order).Id, out OrderLine? line) && !carried.Contains(line))
            {
                carried.Add(line);
            }
        }

        return carried;
    }

    /// <summary>The asks still waiting on a principal who holds the operator role, oldest first.</summary>
    private static List<AskLine> Asked(EstateModel model) =>
        model.Current("ask")
            .Select(a => (Ask: a, State: Asks.State(model, a).State, To: EstateModel.BodyString(a, "to")))
            .Where(a => Asks.IsWaiting(a.State) && model.HasRole(a.To, Roles.Operator))
            .OrderBy(a => a.Ask.At, StringComparer.Ordinal).ThenBy(a => a.Ask.Id, StringComparer.Ordinal)
            .Select(a => new AskLine(a.Ask, a.State, a.To!, Name(model, a.To)))
            .ToList();

    /// <summary>The acceptances only an operator gives that the records show are due.</summary>
    private static Acceptances Due(EstateModel model, Dictionary<string, OrderLine> orders, IReadOnlyList<WorkView.AssignmentItem> assignments)
    {
        List<OrderLine> milestones = orders.Values.Where(o => o.Item.Kind == "milestone").OrderBy(o => o.Item.Key, StringComparer.Ordinal).ThenBy(o => o.Item.Id, StringComparer.Ordinal).ToList();
        List<OrderLine> submitted = milestones.Where(o => o.Status == "submitted").ToList();

        // A result toward a milestone not yet accepted, submitted and awaiting review: its acceptance is the operator's (AcceptanceType).
        var results = new List<ResultDue>();
        foreach (WorkView.AssignmentItem assignment in assignments.Where(a => a.State == "awaiting review" && a.LatestResultId is not null))
        {
            RecordEnvelope? order = WorkView.OrderOfAssignment(model, model.ById[assignment.AssignmentId]);
            if (order is not null && orders.TryGetValue(model.Latest(order).Id, out OrderLine? line) && line.Item.Kind == "milestone" && line.Status is not ("accepted" or "retired"))
            {
                results.Add(new ResultDue(line, assignment, model.ById[assignment.LatestResultId!]));
            }
        }

        // A release candidate with no acceptance of its exact hash by an operator, which its publication needs (ReleaseType).
        List<RecordEnvelope> candidates = model.Current("release")
            .Where(r => EstateModel.BodyString(r, "status") == "candidate"
                && !model.OfType("acceptance").Any(a => a.Subject?.Id == r.Id && a.Subject.Hash == r.ComputedHash && model.HasRole(a.By, Roles.Operator)))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

        // A goal ready for its acceptance whose accepter is an operator: any operator for a goal an agent sponsors, or a sponsor who is one.
        var goals = new List<GoalDue>();
        foreach (RecordEnvelope goal in model.Current("goal").OrderBy(g => g.At, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal))
        {
            GoalRules.Accepter accepter = GoalRules.AccepterOf(model, goal);
            if (GoalRules.Status(model, goal) != "achieved" && GoalView.NotReady(model, goal).Count == 0 && (accepter.Principal is null || model.HasRole(accepter.Principal, Roles.Operator)))
            {
                goals.Add(new GoalDue(goal, GoalRules.CurrentEvaluation(model, goal), accepter.Description));
            }
        }

        return new Acceptances(submitted, results, candidates, goals);
    }

    private static void Header(StringBuilder text, Report report)
    {
        text.Append("Progress of ").Append(report.Charter is null ? "an estate without a charter" : $"{EstateModel.BodyString(report.Charter, "name")} ({EstateModel.BodyString(report.Charter, "slug")})")
            .Append(", rendered from its records").Append(report.Charter is null ? string.Empty : Names(report.Charter)).Append('\n');
    }

    private static void Standing(StringBuilder text, Report report)
    {
        text.Append("\nWhere the estate stands\n");
        if (report.Manifest is null)
        {
            text.Append("  Pinned to nothing this build reads: the manifest did not load [").Append(EstateLayout.ManifestPath).Append("]\n");
        }
        else
        {
            text.Append("  Pinned to ").Append(report.Manifest.Describe()).Append(" [").Append(EstateLayout.ManifestPath);
            AppendIds(text, report.PinnedRelease, report.PinMigration);
            text.Append("]\n");
            text.Append("  Record format ").Append(report.Manifest.RecordFormat.ToString(CultureInfo.InvariantCulture)).Append(" [").Append(EstateLayout.ManifestPath);
            AppendIds(text, report.FormatMigration);
            text.Append("]\n");
        }

        text.Append("  Check: ").Append(Count(report.Findings.Errors, "error")).Append(", ").Append(Count(report.Findings.Warnings, "warning"))
            .Append(report.Scope == RecordsAndRepository
                ? " [every record and the repository, as vericairn estate check runs them]\n"
                : " in the records [every record; the repository checks, which read this document, are vericairn estate check's]\n");
        foreach (Finding finding in report.Findings.Sorted().Where(f => f.Severity != Severity.Info))
        {
            text.Append("    ").Append(finding).Append('\n');
        }
    }

    private static void WorkOrders(StringBuilder text, Report report)
    {
        text.Append("\nWork orders by state: ").Append(report.Orders.Count.ToString(CultureInfo.InvariantCulture)).Append(" current\n");
        int width = KeyWidth(report.Orders);
        foreach (StateGroup state in report.States)
        {
            text.Append("  ").Append(state.State).Append(": ").Append(state.Orders.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (OrderLine order in state.Orders)
            {
                text.Append("    ").Append(order.Item.Key.PadRight(width)).Append(order.Item.Kind.PadRight(11)).Append(order.Item.Title).Append(OrderNames(order)).Append('\n');
            }
        }
    }

    private static void Releases(StringBuilder text, Report report)
    {
        text.Append("\nPublished releases and what each contained: ").Append(report.Releases.Count.ToString(CultureInfo.InvariantCulture)).Append(" published of ")
            .Append(Count(report.Recorded, "release")).Append(" recorded\n");
        text.Append("  What a release contained is what vericairn docs notes derives from its window, the records after the publication before it up to its own: the work whose result an acceptance of its exact revision accepts.\n");
        string? product = null;
        foreach (ReleaseLine line in report.Releases)
        {
            RecordEnvelope release = line.Published.Release;
            string identity = release.Subject is null ? string.Empty : LivingDocuments.ProductIdentity(report.Model, release.Subject.Id);
            if (identity != product)
            {
                product = identity;
                RecordEnvelope? current = report.Model.ById.TryGetValue(identity, out RecordEnvelope? first) ? report.Model.Latest(first) : null;
                text.Append("  ").Append(current is null ? "A product the estate does not hold" : EstateModel.BodyString(current, "name")).Append(current is null ? string.Empty : Names(current)).Append('\n');
            }

            string commit = EstateModel.BodyString(release, "commit") is { Length: >= 12 } c ? c[..12] : "?";
            string date = EstateModel.BodyString(release, "publishedAt") is { Length: >= 10 } at ? at[..10] : "?";
            text.Append("    ").Append(EstateModel.BodyString(release, "version")).Append("  published ").Append(date).Append(" from commit ").Append(commit).Append(Names(release)).Append('\n');
            RecordEnvelope? previous = line.Published.Previous;
            text.Append("      ").Append(Count(line.Published.Window.Count, "record"))
                .Append(previous is null ? " from the estate's first record up to its publication" : $" after the publication of {EstateModel.BodyString(previous, "version")} up to its own")
                .Append(", ").Append(Count(line.Accepted.Count + line.NotAccepted.Count, "result")).Append(previous is null ? string.Empty : Names(previous)).Append('\n');
            foreach (ReleaseContents.Accepted accepted in line.Accepted)
            {
                text.Append("      accepted: ").Append(Work(accepted.Order)).Append(Names(accepted.Result, accepted.Acceptance)).Append('\n');
            }

            foreach ((RecordEnvelope result, RecordEnvelope? order) in line.NotAccepted)
            {
                text.Append("      recorded, not yet accepted: ").Append(Work(order)).Append(Names(result)).Append('\n');
            }
        }
    }

    private static void Next(StringBuilder text, Report report)
    {
        text.Append("\nWhat comes next, by release\n");
        text.Append("  Until goals plan releases (GOAL-06), the only source is each release work order not yet accepted, a work order whose key begins with ")
            .Append(ReleaseKeyPrefix).Append(", and the work orders it carries, which are its input work orders.\n");
        if (report.Upcoming.Count == 0)
        {
            text.Append("  No release work order waits for its acceptance.\n");
        }

        foreach (Upcoming upcoming in report.Upcoming)
        {
            text.Append("  ").Append(upcoming.Order.Item.Key).Append("  ").Append(upcoming.Order.Status).Append("  ").Append(upcoming.Order.Item.Title).Append(OrderNames(upcoming.Order)).Append('\n');
            text.Append("    ").Append(Count(upcoming.Carried.Count, "work order")).Append(", ").Append(upcoming.Carried.Count(c => c.Status == "accepted").ToString(CultureInfo.InvariantCulture)).Append(" accepted\n");
            int width = KeyWidth(upcoming.Carried);
            int statusWidth = upcoming.Carried.Select(c => c.Status.Length).DefaultIfEmpty(0).Max() + 2;
            foreach (OrderLine carried in upcoming.Carried)
            {
                text.Append("    ").Append(carried.Item.Key.PadRight(width)).Append(carried.Status.PadRight(statusWidth)).Append(carried.Item.Title).Append(OrderNames(carried)).Append('\n');
            }
        }
    }

    private static void Eligible(StringBuilder text, Report report)
    {
        List<Planned> eligible = report.Planned.Where(p => p.Item.Eligible).ToList();
        List<Planned> held = report.Planned.Where(p => !p.Item.Eligible).ToList();
        text.Append("\nThe next eligible work: ").Append(eligible.Count.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(Count(report.Planned.Count, "planned work order")).Append('\n');
        text.Append("  Planned work orders whose after work orders are all accepted and whose blockedBy holds nothing open; routing by roster and budget is GOAL-04's.\n");
        int width = KeyWidth(report.Planned.Select(p => p.Line));
        foreach (Planned planned in eligible)
        {
            PlannedLines(text, planned, width, "  ");
        }

        text.Append("  Held: ").Append(held.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (Planned planned in held)
        {
            PlannedLines(text, planned, width, "    ");
        }
    }

    private static void PlannedLines(StringBuilder text, Planned planned, int width, string indent)
    {
        text.Append(indent).Append(planned.Line.Item.Key.PadRight(width)).Append(planned.Line.Item.Kind.PadRight(11)).Append(planned.Line.Item.Title).Append(OrderNames(planned.Line)).Append('\n');
        foreach (NextEligible.Relation relation in planned.Item.Relations)
        {
            text.Append(indent).Append("  ").Append(relation.Kind).Append(' ').Append(relation.Key ?? relation.Type + " " + relation.Id).Append(": ").Append(relation.State)
                .Append(relation.Open.Count > 0 ? " (" + string.Join(", ", relation.Open) + ")" : string.Empty)
                .Append(relation.Holds ? ", holds it" : string.Empty)
                .Append(relation.Holder is null ? $" [{relation.Type} {relation.Id}, which the estate does not hold]" : Names([relation.Holder, .. relation.Basis])).Append('\n');
        }

        foreach (WorkView.AssignmentItem assignment in planned.Assignments)
        {
            text.Append(indent).Append("  assigned to ").Append(assignment.ContributorName).Append(": ").Append(assignment.State).Append(" [assignment ").Append(assignment.AssignmentId).Append("]\n");
        }
    }

    private static void Operator(StringBuilder text, Report report)
    {
        text.Append("\nWhat waits on the operator\n");
        text.Append("  Asks awaiting an answer: ").Append(report.Asks.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (IGrouping<string, AskLine> principal in report.Asks.GroupBy(a => a.To, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append("    to ").Append(principal.First().ToName).Append(", operator [principal ").Append(principal.Key).Append("]: ").Append(principal.Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (AskLine ask in principal)
            {
                text.Append("    ").Append(ask.State).Append(": ").Append(EstateModel.BodyString(ask.Ask, "question")).Append(Names(ask.Ask)).Append('\n');
                text.Append("      recommendation: ").Append(EstateModel.BodyString(ask.Ask, "recommendation")).Append('\n');
            }
        }

        Acceptances due = report.Acceptances;
        text.Append("  Acceptances only the operator gives: ").Append((due.Submitted.Count + due.Results.Count + due.Candidates.Count + due.Goals.Count).ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("    milestone work orders submitted: ").Append(due.Submitted.Count == 0 ? "none" : due.Submitted.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (OrderLine order in due.Submitted)
        {
            text.Append("      ").Append(order.Item.Key).Append("  ").Append(order.Item.Title).Append(OrderNames(order)).Append('\n');
        }

        text.Append("    results submitted toward milestone work orders: ").Append(due.Results.Count == 0 ? "none" : due.Results.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (ResultDue result in due.Results)
        {
            text.Append("      ").Append(result.Order.Item.Key).Append("  result ").Append(result.Assignment.Outcome).Append(" by ").Append(result.Assignment.ContributorName).Append(", ").Append(result.Assignment.State)
                .Append(Names(result.Result, report.Model.ById[result.Assignment.AssignmentId])).Append('\n');
        }

        text.Append("    release candidates recorded and not accepted: ").Append(due.Candidates.Count == 0 ? "none" : due.Candidates.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (RecordEnvelope candidate in due.Candidates)
        {
            text.Append("      ").Append(EstateModel.BodyString(candidate, "version")).Append(" from commit ").Append(EstateModel.BodyString(candidate, "commit") is { Length: >= 12 } c ? c[..12] : "?").Append(Names(candidate)).Append('\n');
        }

        text.Append("    goals ready for acceptance by an operator: ").Append(due.Goals.Count == 0 ? "none" : due.Goals.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (GoalDue goal in due.Goals)
        {
            text.Append("      ").Append(EstateModel.BodyString(goal.Goal, "title")).Append(", accepted by ").Append(goal.Accepter).Append(Names(goal.Goal, goal.Evaluation)).Append('\n');
        }
    }

    private static JsonObject StandingJson(Report report) => new()
    {
        ["pin"] = report.Manifest is null ? null : new JsonObject
        {
            ["describe"] = report.Manifest.Describe(),
            ["mode"] = report.Manifest.Mode,
            ["repository"] = report.Manifest.Repository,
            ["version"] = report.Manifest.Version,
            ["commit"] = report.Manifest.Commit,
            ["release"] = report.PinnedRelease?.Id,
            ["migration"] = report.PinMigration?.Id,
        },
        ["recordFormat"] = report.Manifest is null ? null : new JsonObject { ["format"] = report.Manifest.RecordFormat, ["migration"] = report.FormatMigration?.Id },
        ["check"] = new JsonObject
        {
            ["scope"] = report.Scope,
            ["errors"] = report.Findings.Errors,
            ["warnings"] = report.Findings.Warnings,
            ["findings"] = new JsonArray(report.Findings.Sorted().Where(f => f.Severity != Severity.Info)
                .Select(f => (JsonNode)new JsonObject { ["severity"] = f.Severity.ToString().ToLowerInvariant(), ["code"] = f.Code, ["location"] = f.Location, ["message"] = f.Message }).ToArray()),
        },
    };

    private static JsonObject ReleasesJson(Report report) => new()
    {
        ["recorded"] = report.Recorded,
        ["published"] = new JsonArray(report.Releases.Select(line =>
        {
            RecordEnvelope release = line.Published.Release;
            RecordEnvelope? previous = line.Published.Previous;
            return (JsonNode)new JsonObject
            {
                ["version"] = EstateModel.BodyString(release, "version"),
                ["id"] = release.Id,
                ["product"] = release.Subject is null ? null : LivingDocuments.ProductIdentity(report.Model, release.Subject.Id),
                ["commit"] = EstateModel.BodyString(release, "commit"),
                ["publishedAt"] = EstateModel.BodyString(release, "publishedAt"),
                ["window"] = new JsonObject
                {
                    ["after"] = previous is null ? null : new JsonObject { ["version"] = EstateModel.BodyString(previous, "version"), ["id"] = previous.Id, ["at"] = previous.At },
                    ["upTo"] = release.At,
                    ["records"] = line.Published.Window.Count,
                },
                ["accepted"] = new JsonArray(line.Accepted.Select(a => (JsonNode)WorkJson(a.Order, a.Result, a.Acceptance)).ToArray()),
                ["notAccepted"] = new JsonArray(line.NotAccepted.Select(n => (JsonNode)WorkJson(n.Order, n.Result, null)).ToArray()),
            };
        }).ToArray()),
    };

    private static JsonObject OperatorJson(Report report) => new()
    {
        ["asks"] = new JsonArray(report.Asks.Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.Ask.Id,
            ["to"] = a.To,
            ["toName"] = a.ToName,
            ["state"] = a.State,
            ["question"] = EstateModel.BodyString(a.Ask, "question"),
            ["recommendation"] = EstateModel.BodyString(a.Ask, "recommendation"),
            ["consequence"] = EstateModel.BodyString(a.Ask, "consequence"),
        }).ToArray()),
        ["acceptances"] = new JsonObject
        {
            ["milestoneWorkOrders"] = new JsonArray(report.Acceptances.Submitted.Select(OrderJson).ToArray()),
            ["milestoneResults"] = new JsonArray(report.Acceptances.Results.Select(r => (JsonNode)new JsonObject
            {
                ["workOrder"] = OrderJson(r.Order),
                ["result"] = r.Result.Id,
                ["assignment"] = r.Assignment.AssignmentId,
                ["contributor"] = r.Assignment.ContributorId,
                ["outcome"] = r.Assignment.Outcome,
            }).ToArray()),
            ["releaseCandidates"] = new JsonArray(report.Acceptances.Candidates.Select(c => (JsonNode)new JsonObject
            {
                ["version"] = EstateModel.BodyString(c, "version"),
                ["id"] = c.Id,
                ["hash"] = c.ComputedHash,
                ["commit"] = EstateModel.BodyString(c, "commit"),
            }).ToArray()),
            ["goals"] = new JsonArray(report.Acceptances.Goals.Select(g => (JsonNode)new JsonObject
            {
                ["id"] = g.Goal.Id,
                ["title"] = EstateModel.BodyString(g.Goal, "title"),
                ["evaluation"] = g.Evaluation?.Id,
                ["accepter"] = g.Accepter,
            }).ToArray()),
        },
    };

    private static JsonNode OrderJson(OrderLine order) => new JsonObject
    {
        ["key"] = order.Item.Key,
        ["kind"] = order.Item.Kind,
        ["title"] = order.Item.Title,
        ["id"] = order.Item.Id,
        ["status"] = order.Status,
        ["basis"] = order.Basis is null ? null : new JsonObject { ["type"] = order.Basis.Type, ["id"] = order.Basis.Id },
    };

    private static JsonNode PlannedJson(Planned planned)
    {
        var json = (JsonObject)OrderJson(planned.Line);
        json["relations"] = new JsonArray(planned.Item.Relations.Select(r => (JsonNode)new JsonObject
        {
            ["relation"] = r.Kind,
            ["type"] = r.Type,
            ["id"] = r.Id,
            ["holder"] = r.Holder?.Id,
            ["key"] = r.Key,
            ["state"] = r.State,
            ["holds"] = r.Holds,
            ["open"] = new JsonArray(r.Open.Select(o => (JsonNode)o).ToArray()),
            ["basis"] = new JsonArray(r.Basis.Select(b => (JsonNode)new JsonObject { ["type"] = b.Type, ["id"] = b.Id }).ToArray()),
        }).ToArray());
        json["assignments"] = new JsonArray(planned.Assignments.Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.AssignmentId,
            ["contributor"] = a.ContributorId,
            ["contributorName"] = a.ContributorName,
            ["state"] = a.State,
        }).ToArray());
        return json;
    }

    private static JsonObject WorkJson(RecordEnvelope? order, RecordEnvelope result, RecordEnvelope? acceptance) => new()
    {
        ["key"] = order is null ? null : EstateModel.BodyString(order, "key"),
        ["title"] = order is null ? null : EstateModel.BodyString(order, "title"),
        ["workOrder"] = order?.Id,
        ["result"] = result.Id,
        ["outcome"] = EstateModel.BodyString(result, "outcome"),
        ["acceptance"] = acceptance?.Id,
    };

    private static string Work(RecordEnvelope? order) =>
        order is null ? "a result toward no work order the estate holds" : $"{EstateModel.BodyString(order, "key")} {EstateModel.BodyString(order, "title")}";

    private static string OrderNames(OrderLine order) => order.Basis is null ? Names(order.Order) : Names(order.Order, order.Basis);

    /// <summary>The records a line rests on, by type and id, in brackets.</summary>
    private static string Names(params RecordEnvelope?[] records)
    {
        var text = new StringBuilder();
        AppendIds(text, records);
        return text.Length == 0 ? string.Empty : " [" + text.ToString(2, text.Length - 2) + "]";
    }

    private static void AppendIds(StringBuilder text, params RecordEnvelope?[] records)
    {
        foreach (RecordEnvelope record in records.OfType<RecordEnvelope>())
        {
            text.Append(", ").Append(record.Type).Append(' ').Append(record.Id);
        }
    }

    private static string Count(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");

    private static int KeyWidth(IEnumerable<OrderLine> orders) => orders.Select(o => o.Item.Key.Length).DefaultIfEmpty(0).Max() + 2;

    private static string Name(EstateModel model, string? principal) =>
        model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "name") ?? principal ?? "?" : principal ?? "?";

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;

    /// <summary>A work order with the state status derives and the record that state rests on.</summary>
    private sealed record OrderLine(EstateStatus.WorkItem Item, RecordEnvelope Order, RecordEnvelope? Basis)
    {
        public string Status => Item.Status;
    }

    private sealed record StateGroup(string State, IReadOnlyList<OrderLine> Orders);

    private sealed record ReleaseLine(ReleaseContents.Published Published, IReadOnlyList<ReleaseContents.Accepted> Accepted, IReadOnlyList<(RecordEnvelope Result, RecordEnvelope? Order)> NotAccepted);

    private sealed record Upcoming(OrderLine Order, IReadOnlyList<OrderLine> Carried);

    private sealed record Planned(OrderLine Line, NextEligible.Item Item, IReadOnlyList<WorkView.AssignmentItem> Assignments);

    private sealed record AskLine(RecordEnvelope Ask, string State, string To, string ToName);

    private sealed record ResultDue(OrderLine Order, WorkView.AssignmentItem Assignment, RecordEnvelope Result);

    private sealed record GoalDue(RecordEnvelope Goal, RecordEnvelope? Evaluation, string Accepter);

    private sealed record Acceptances(IReadOnlyList<OrderLine> Submitted, IReadOnlyList<ResultDue> Results, IReadOnlyList<RecordEnvelope> Candidates, IReadOnlyList<GoalDue> Goals);

    private sealed record Report(
        EstateModel Model,
        RecordEnvelope? Charter,
        EstateManifest? Manifest,
        RecordEnvelope? PinnedRelease,
        RecordEnvelope? PinMigration,
        RecordEnvelope? FormatMigration,
        string Scope,
        FindingList Findings,
        IReadOnlyList<OrderLine> Orders,
        IReadOnlyList<StateGroup> States,
        int Recorded,
        IReadOnlyList<ReleaseLine> Releases,
        IReadOnlyList<Upcoming> Upcoming,
        IReadOnlyList<Planned> Planned,
        IReadOnlyList<AskLine> Asks,
        Acceptances Acceptances);
}
