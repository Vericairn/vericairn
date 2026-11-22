// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Delegates a work order to a contributor under a brief, pinned to the exact work-order revision.</summary>
public sealed class AssignmentType : IRecordType
{
    /// <inheritdoc />
    public string Name => "assignment";

    /// <inheritdoc />
    public string Summary => "Delegates a work order to a contributor under a brief (outcome, sources, permitted changes, prohibited effects, workspace, limits, expected outputs, review criteria, recovery), pinned to the exact work-order revision.";

    /// <inheritdoc />
    public string? SubjectType => "work-order";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        SavedPrompts.UseField,
        new("execution", "object", false, "Explicit execution choices and cumulative restrictions; launcher, roster, goal, assignment, then run precedence. Role restrictions remain binding.", 2),
        new("contributor", "ulid", true, "The principal that does the work: a human, a deterministic tool, a native agent or a qualified external agent. Admitted before the work is assigned."),
        new("brief", "object", true, "outcome, sources (references {type, id, hash?}), permittedChanges, prohibitedEffects, workspace (allowed trees, named without machine paths), limits, expectedOutputs, reviewCriteria, recovery; from format 3, optional reviewBrief retains the complete release review text and optional progressRequest (types, statusText, cadence) replaces broader progress requests; from 0.9.4, optional repository (the identity of the estate or of a repository resource the work changes), excludedChanges (patterns in permittedChanges' grammar the work leaves out) and after (at most 20 assignment ids whose work it waits for, each followed to its current revision, never itself and never a cycle); from 0.9.4 (RUN-04), optional retrieval: public, which permits the work's runs to retrieve public sources with their harness's web tools, never with credentials, each source retained as an untrusted original; no other field or text permits it. A reader lacking these refuses the brief, which admits no unknown field.") { Members = [.. AssignmentBrief.Members.Select(m => m.Describe())] },
        new("note", "string", false, "Free text."),
        new("step", "object", false, "For an assignment of a run's work order, the flow step it carries: key, instance for a step that runs over a list under each, and optional inputs, each input of the step its brief pins by name and the exact record {type, id, hash} that carries it. Every assignment of a run's work order names its step, and only those do. A review before hand-off adds review: the items it reviews (none for a step that declares no items) and the exact subject {type, id, hash} that carries them; its contributor is never an author of the step's work unless every item takes a self review.", 3),
        new("occurrence", "object", false, "From 0.9.4, for an assignment of a standing agent's work order, the occurrence it carries: key (the goal's identity, trigger, revision and item, or for a duty trigger duty/ and the duty occurrence's identity), trigger, revision (the goal revision the trigger's declaration originates from) and item (schedule {interval, dueAt}, event {record {type, id, hash}}, condition {failing, records}, request {at, by} or duty {occurrence, policy, rule, dueAt, deadline, schedule}). Every assignment of a standing agent's work order names one and only those do; a key holds one assignment, whoever records it, unless a person takes its step over. A reader lacking it refuses the assignment, which admits no unknown field.", 3),
        new("routedWith", string.Join('|', RoutedWith), false, "Set by routing on an assignment it stores together with the record that carries it: run, the intent of the agent run on it (an agent-run operation whose subject is this assignment), or ask, the ask that names it. Until that record exists the assignment holds nothing of its step, so an interrupted store never holds a step. From 0.9.4, additive in format 3: session, an assignment routing gave an agent's live session, stored alone and holding its step at once; a reader before 0.9.4 refuses it.", 2),
        new("basis", "ulid", false, "From 0.9.4, additive in format 3: for a hand-over, the lapse ask it answers, an ask about the assignment it takes the step over from. Recorded by a person, never an agent; a reader before 0.9.4 refuses it as an unknown field.", 3),
    ];

    /// <summary>The most assignments a brief's after names.</summary>
    public const int MaxAfter = 20;

    /// <summary>
    /// From 0.9.4 (CODE-73; the estate's design/measured-work.md § 5), the brief fields the overlap check reads, additive in format 3:
    /// repository, the identity of the estate or of a repository resource; excludedChanges, interpretable patterns of the worker's
    /// grammar, since prose excludes nothing; and after, at most 20 assignments, never this one and never a cycle when each is followed
    /// to its current revision.
    /// </summary>
    private static void Overlaps(ValidationContext context, BodyReader brief)
    {
        if (brief.Since(3, AssignmentBrief.Repository.Name) && brief.Has(AssignmentBrief.Repository.Name))
        {
            string? repository = AssignmentBrief.Repository.Id(brief);
            if (repository is not null && !AssignmentOverlaps.IsRepository(context.Model, repository))
            {
                brief.Error("assignment.repository", "repository", "The repository is the identity of the estate or of a repository resource: the first record of its lineage.");
            }
        }

        IReadOnlyList<string>? excluded = brief.Since(3, AssignmentBrief.ExcludedChanges.Name) ? AssignmentBrief.ExcludedChanges.Strings(brief) : null;
        foreach (string entry in excluded ?? [])
        {
            if (PathPatterns.Pattern(entry) is null)
            {
                brief.Error("assignment.excluded", "excludedChanges", $"'{entry}' is not a repository-relative pattern; an exclusion is a pattern, such as path:docs/operator-guide.md, since prose excludes nothing.");
            }
        }

        IReadOnlyList<string>? after = brief.Since(3, AssignmentBrief.After.Name) ? AssignmentBrief.After.Strings(brief) : null;
        if (after is null)
        {
            return;
        }

        if (after.Count > MaxAfter)
        {
            brief.Error("assignment.after", "after", $"A brief waits for at most {MaxAfter} assignments; it names {after.Count}.");
        }

        if (after.Distinct(StringComparer.Ordinal).Count() != after.Count)
        {
            brief.Error("assignment.after", "after", "Each assignment is named once.");
        }

        HashSet<string> own = [.. context.Model.ById.ContainsKey(context.Record.Id) ? context.Model.Chain(context.Model.Latest(context.Record)) : [context.Record.Id]];
        if (context.Record.Supersedes is string previous && context.Model.ById.ContainsKey(previous))
        {
            own.UnionWith(context.Model.Chain(context.Model.Latest(context.Model.ById[previous])));
        }

        foreach (string id in after)
        {
            if (!Records.Identity.Ulid.IsValid(id))
            {
                brief.Error("assignment.after", "after", $"'{id}' is not an assignment id.");
                continue;
            }

            if (own.Contains(id))
            {
                brief.Error("assignment.after", "after", "An assignment never waits for itself.");
                continue;
            }

            context.RequireReference("brief.after", id, "assignment");
        }

        if (AssignmentOverlaps.Cycle(context.Model, own, after) is string through)
        {
            brief.Error("assignment.after", "after", $"Waiting for these assignments makes a cycle through assignment {through}: each would wait for the other.");
        }
    }

    /// <summary>The one retrieval a brief may permit (RUN-04): public sources, with the harness's web tools and no credential.</summary>
    internal const string PublicRetrieval = "public";

    /// <summary>Whether an assignment's brief permits its runs to retrieve public sources (RUN-04); nothing else permits it.</summary>
    internal static bool PermitsRetrieval(RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return assignment.Format >= 3 && assignment.Body.TryGetProperty("brief", out JsonElement brief) && brief.ValueKind == JsonValueKind.Object
            && brief.TryGetProperty("retrieval", out JsonElement retrieval) && retrieval.ValueKind == JsonValueKind.String && retrieval.GetString() == PublicRetrieval;
    }

    /// <summary>The records routing stores an assignment together with; from 0.9.4 (format 3) also session, which it stores alone.</summary>
    public static readonly ImmutableArray<string> RoutedWith = ["run", "ask", "session"];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? contributor = body.Ulid("contributor", true);
        BodyReader? brief = body.Nested("brief", true);
        if (brief is not null)
        {
            // From 0.9.4 (DOCS-46) each member is read by the rule the record reference describes it from (AssignmentBrief).
            AssignmentBrief.Outcome.Text(brief);
            foreach (BodyReader source in AssignmentBrief.Sources.Objects(brief) ?? [])
            {
                RecordReference? reference = RecordParser.ParseReference(source.Element, source.Location, context.Findings);
                if (reference is null)
                {
                    continue;
                }

                RecordEnvelope? target = context.RequireReference("brief.sources", reference.Id, reference.Type);
                if (target is not null && reference.Hash is not null && reference.Hash != target.ComputedHash)
                {
                    context.Findings.Error("assignment.source-hash", source.Location, $"Source {reference.Id} does not have the pinned content hash; the brief relied on a different revision.");
                }
            }

            AssignmentBrief.PermittedChanges.Strings(brief);
            AssignmentBrief.ProhibitedEffects.Strings(brief);
            AssignmentBrief.Workspace.Strings(brief);
            AssignmentBrief.Limits.Strings(brief);
            AssignmentBrief.ExpectedOutputs.Strings(brief);
            AssignmentBrief.ReviewCriteria.Strings(brief);
            AssignmentBrief.Recovery.Text(brief);
            if (context.Format >= AssignmentBrief.ReviewBrief.SinceFormat)
                AssignmentBrief.ReviewBrief.Text(brief);
            Overlaps(context, brief);
            if (brief.Since(3, AssignmentBrief.Retrieval.Name)) AssignmentBrief.Retrieval.OneOf(brief);

            ProgressReporting.Request(brief);
            brief.NoOtherKeys();
        }

        body.Text("note", false, 4000);
        BodyReader? step = body.Since(3, "step") ? body.Nested("step", false) : null;
        if (step is not null)
        {
            step.Text("key", true, 100, Patterns.Slug, "a step key");
            step.Text("instance", false, 100, Patterns.Slug, "an instance name");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader input in step.NestedArray("inputs", false, allowEmpty: false) ?? [])
            {
                string? name = input.Text("name", true, 60, Patterns.Slug, "an input name");
                string? type = input.Text("type", true, 100, Patterns.Slug, "a record type");
                string? id = input.Ulid("id", true);
                string? hash = input.Hash("hash", true);
                input.NoOtherKeys();
                if (name is not null && !names.Add(name))
                {
                    input.Error("assignment.step", "name", "Each input is pinned once.");
                }

                if (type is not null && context.RequireReference("step.inputs", id, type) is RecordEnvelope pinned && pinned.ComputedHash != hash)
                {
                    input.Error("assignment.step", "hash", $"Input {name} does not have the pinned content hash; the brief relied on a different revision.");
                }
            }

            // A review before hand-off (design/flow.md § 6): the items it reviews and the exact subject that carries them.
            BodyReader? review = step.Nested("review", false);
            if (review is not null)
            {
                IReadOnlyList<string>? items = review.StringArray("items", false, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "an item name");
                if (items is not null && items.Distinct(StringComparer.Ordinal).Count() != items.Count)
                {
                    review.Error("assignment.step", "items", "Each item is reviewed once.");
                }

                BodyReader? subject = review.Nested("subject", true);
                if (subject is not null)
                {
                    string? type = subject.Text("type", true, 100, Patterns.Slug, "a record type");
                    string? id = subject.Ulid("id", true);
                    string? hash = subject.Hash("hash", true);
                    subject.NoOtherKeys();
                    if (type is not null && context.RequireReference("step.review.subject", id, type) is RecordEnvelope reviewed && reviewed.ComputedHash != hash)
                    {
                        subject.Error("assignment.step", "hash", "The reviewed subject does not have the pinned content hash; a review names the exact revision it reviews.");
                    }
                }

                review.NoOtherKeys();
            }

            step.NoOtherKeys();
        }

        BodyReader? occurrence = body.Since(3, "occurrence") ? body.Nested("occurrence", false) : null;
        if (body.Since(2, "routedWith") && body.Enum("routedWith", false, RoutedWith) == "session" && context.Format < 3)
        {
            body.Error("body.format", "routedWith", "routedWith session belongs to record format 3, and this record states an earlier format; an estate writes it once it is upgraded to a release that writes format 3.");
        }

        // From 0.9.4 (GOAL-11): a hand-over's basis, the lapse ask it answers.
        string? basis = body.Since(3, "basis") ? body.Ulid("basis", false) : null;

        SavedPrompts.ValidateUse(context);
        ExecutionPolicy.Read(body);
        body.NoOtherKeys();
        string by = context.Record.By;
        RecordEnvelope? order = context.Find(context.Record.Subject?.Id) is { Type: "work-order" } named ? named : null;
        RecordEnvelope? handedOver = context.Format >= 2 && order is not null ? Routing.TakesOverFrom(context.Model, context.Record) : null;
        if (context.Principal(by) is null)
        {
            context.Error("assignment.author", $"Assigning principal {by} has no principal record.");
        }
        else if (handedOver is not null)
        {
            // A hand-over, from record format 2: a person takes over a step an agent holds (design/goals.md § 6, step 4). It is recorded
            // by a person, the one taking the step over, who decides for its goal or an operator, and never by an agent.
            if (!Routing.MayHandOver(context.Model, by, contributor, order!, context.Record))
            {
                context.Error("assignment.handover", $"This assignment takes work order {EstateModel.BodyString(order!, "key") ?? order!.Id} over from the agent's assignment {handedOver.Id}: a hand-over is recorded by a person, the one taking the step over, the sponsor of its goal or an operator, and never by an agent.");
            }
        }
        else if (context.Format >= 3 && DispatchAuthority.DispatcherAt(context.Model, by, context.Record))
        {
            // From 0.9.4 (ID-07): a dispatcher assigns only within its dispatch grant, and nothing else gives it authority to assign.
            if (DispatchAuthority.Refusal(context.Model, context.Record, order) is { } outside)
                context.Error("assignment.dispatch", $"This assignment is by dispatcher {by} and outside its dispatch grant: {outside}.");
        }
        else if (!context.HasRole(by, Roles.Operator) && !context.HasScope(by, Scopes.LocalEngineering)
            && !(context.Format >= 2 && order is not null && Routing.DecidesForStep(context.Model, by, order, context.Record)))
        {
            context.Error("assignment.author", "Work is assigned by the operator, by a principal granted the 'local-engineering' scope, for a step of a goal by the goal's sponsor when the sponsor is a person, or from 0.9.4 by a dispatcher within its dispatch grant.");
        }

        if (context.Format >= 3 && order is not null)
        {
            FlowRuns.ValidateStep(context, order, step);
            StandingAgents.ValidateOccurrence(context, order, occurrence);
        }

        if (contributor is not null && context.Principal(contributor) is null)
        {
            context.Error("assignment.contributor", $"Contributor {contributor} has no principal record; contributors are admitted before work is assigned to them.");
        }

        // From 0.9.4 (GOAL-11): a hand-over citing a lapse ask is a person's act, never an agent's, and its basis is the standing lapse ask
        // the presence sweep recorded about the very assignment it takes the step over from: open or notified when it is recorded, neither
        // withdrawn nor answered, and signed by that assignment's author, as the sweep signs it. An ask any other principal wrote is none.
        if (basis is not null && context.RequireReference("basis", basis, "ask") is RecordEnvelope ask)
        {
            if (!GoalRules.IsPerson(context.Model, by))
            {
                context.Error("assignment.basis", "A hand-over citing a lapse ask is recorded by a person, the one taking the step over, who decides for its goal or an operator, and never by an agent.");
            }
            else if (order is not null && !Routing.MayHandOver(context.Model, by, contributor, order, context.Record))
            {
                context.Error("assignment.basis", "A hand-over citing a lapse ask is recorded by the person taking the step over, who decides for its goal or an operator.");
            }

            // The lapsed assignment is the one the ask is about, found by its id whoever recorded it and whatever it holds now; it carries
            // the same step of the same work order.
            RecordEnvelope? lapsed = ask.Subject is { Type: "assignment" } about && context.Find(about.Id) is { Type: "assignment" } cited ? context.Model.Latest(cited) : null;
            bool sameStep = lapsed?.Subject is not null && order is not null && context.Model.Chain(context.Model.Latest(order)).Contains(lapsed.Subject.Id)
                && Routing.HoldingKey(lapsed.Body) == Routing.HoldingKey(context.Record.Body);
            if (lapsed is null || !sameStep || !Routing.LapseAsk(context.Model, ask, lapsed, context.Record))
            {
                context.Error("assignment.basis", $"Ask {basis} is no standing lapse ask about an assignment of this step{(lapsed is null ? string.Empty : $" ({lapsed.Id})")}: a hand-over's basis is the ask the presence sweep recorded when the session holding that assignment lapsed, open when the hand-over is recorded, neither withdrawn nor answered.");
            }
        }
    }
}
