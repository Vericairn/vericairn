// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Templates;

/// <summary>
/// The content contract of a template of kind <c>flow</c> (the estate's design/flow.md § 5): a declared role process, its subject,
/// roles, ordered steps with their hand-off contract, options, triggers, the flow it extends and the lines carried into every brief.
/// Reading one checks its own content; how an extension relates to its base is checked when the flow is resolved
/// (<see cref="FlowDefinition"/>). A flow is content and grants nothing.
/// </summary>
public static partial class FlowContract
{
    /// <summary>The template kind of a flow.</summary>
    public const string Kind = "flow";

    /// <summary>The step kinds: an assignment, a review, a product command, an outside effect, a phase gate, an outside round trip, another flow's run.</summary>
    public static readonly ImmutableArray<string> StepKinds = ["work", "review", "operation", "effect", "decision", "exchange", "flow"];

    /// <summary>Who may hold a role.</summary>
    public static readonly ImmutableArray<string> Holders = ["person", "agent", "either"];

    /// <summary>The reserved acts a step may be (design/release-procedure.md § 4).</summary>
    public static readonly ImmutableArray<string> ReservedActs = ["accept-release", "publish", "spend", "use-credential", "security-review"];

    /// <summary>
    /// The independence of an item's review before its step closes (design/flow.md §§ 5 and 6): none, self (the author's own check,
    /// labeled as such), independent (a principal other than its producer), other-harness (independent, by an agent on another harness
    /// than the producer's) or person (a person other than its producer).
    /// </summary>
    public static readonly ImmutableArray<string> Reviews = ["none", "self", "independent", "other-harness", "person"];

    /// <summary>
    /// The kind of an item a step produces or collects (design/flow.md § 5): a file by hash and media type, a record by record type, an
    /// observation (an operation's observed outcome), a verification (a check's result), or a person's decision or attestation.
    /// </summary>
    public static readonly ImmutableArray<string> ItemKinds = ["file", "record", "observation", "verification", "decision", "attestation"];

    /// <summary>The prefix of the option an optional item is configured by: item.&lt;step&gt;.&lt;name&gt;, on or off.</summary>
    public const string ItemOption = "item.";

    /// <summary>The routes an outside effect or an exchange travels by (design/flow.md § 9).</summary>
    public static readonly ImmutableArray<string> Routes = ["api", "person", "browser"];

    /// <summary>The kinds of a subject field: a record reference, a text value, or a list of instance names an <c>each</c> step runs over.</summary>
    public static readonly ImmutableArray<string> SubjectTypes = ["record", "text", "list"];

    /// <summary>The kinds whose steps the conductor routes to their role's principal as an assignment of the run's work order.</summary>
    public static readonly ImmutableArray<string> Routed = ["work", "review"];

    /// <summary>
    /// The failure routes a work step may declare (design/flow.md § 7): a branch it enters, an earlier step the run returns to, or a stop
    /// for the sponsor; a step that declares none stops.
    /// </summary>
    public static readonly ImmutableArray<string> FailureRoutes = ["branch", "step", "stop"];

    /// <summary>The prefix of the option a flow's optional step is configured by: step.&lt;key&gt;, on or off.</summary>
    public const string StepOption = "step.";

    /// <summary>The values of an optional step's option.</summary>
    public static readonly ImmutableArray<string> OnOff = ["on", "off"];

    /// <summary>True when template content declares the flow kind.</summary>
    public static bool IsFlow(JsonElement content) => TJson.Text(content, "kind") == Kind;

    /// <summary>
    /// Checks one flow's content: every field of design/flow.md § 5, each step's kind, its marking required or optional with a default, its role among the
    /// declared roles, its preconditions as record predicates over the run's subject, its hand-off contract and the steps it waits on,
    /// all of which name earlier steps only, so a flow has no cycle. An extension's relation to its base is checked on resolution.
    /// </summary>
    internal static void Validate(BodyReader body)
    {
        body.Enum("format", true, "vericairn.template/1");
        body.Enum("kind", true, Kind);
        body.Text("id", true, 200, Patterns.Contribution, "namespace/name");
        body.Text("version", true, 100, Patterns.SemanticVersion, "a semantic version");
        body.Text("purpose", true, 4000);
        body.Text("domain", false, 100);
        BodyReader? extended = body.Nested("extends", false);
        bool extension = extended is not null;
        if (extended is not null)
        {
            TemplateRecords.ReadPin(extended);
            extended.NoOtherKeys();
        }

        Dictionary<string, string> subject = extension ? [] : Subject(body.Nested("subject", true));
        if (extension && body.Has("subject"))
        {
            body.Error("flow.extends", "subject", "An extension runs over its base's subject and declares none of its own.");
        }

        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BodyReader role in body.NestedArray("roles", !extension, allowEmpty: extension) ?? [])
        {
            string? id = role.Text("id", true, 60, Patterns.Slug, "a lower-case role name");
            string? holder = role.Enum("holder", true, Holders);
            role.Text("brief", true, 8000);
            string? document = role.Text("document", false, 500, Patterns.RelativePath, "a normalized repository-relative path");
            if (document is not null && (!DocumentPatterns.IsSafePath(document) || document.Split('/')[0].ToLowerInvariant() is "records" or "objects"))
            {
                role.Error("flow.role", "document", "A role brief's document is a portable path to a file the estate keeps in its repository, outside hidden directories, its records and its objects.");
            }

            role.NoOtherKeys();
            if (id is not null && !roles.TryAdd(id, holder ?? "either"))
            {
                role.Error("flow.duplicate", "id", "Each role is declared once.");
            }
        }

        ReadOptions(body);
        var triggers = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader trigger in body.NestedArray("triggers", false, allowEmpty: false) ?? [])
        {
            string? kind = trigger.Text("kind", true, 100, Patterns.Slug, "a trigger kind");
            trigger.Text("summary", true, 2000);
            trigger.NoOtherKeys();
            if (kind is not null && !triggers.Add(kind))
            {
                trigger.Error("flow.duplicate", "kind", "Each trigger kind is declared once.");
            }
        }

        body.StringArray("never", false, allowEmpty: false, maxItemLength: 2000);
        IReadOnlyList<string>? off = body.StringArray("off", false, allowEmpty: false, maxItemLength: 200, itemPattern: OffPattern(), patternHint: "a step key, or step.item");
        if (off is not null && !extension)
        {
            body.Error("flow.off", "off", "Only an extension turns off a step or an item, one its base declares optional.");
        }

        if (off is not null && off.Distinct(StringComparer.Ordinal).Count() != off.Count)
        {
            body.Error("flow.duplicate", "off", "A step is turned off once.");
        }

        IReadOnlyList<BodyReader> steps = body.NestedArray("steps", !extension, allowEmpty: extension) ?? [];
        var declared = new List<StepShape>();
        foreach (BodyReader step in steps)
        {
            declared.Add(Step(step, subject, extension, declared, roles));
        }

        // Within one content the references are checked against the steps declared so far; an extension's references to its base's
        // steps and roles are checked when the flow is resolved.
        if (!extension)
        {
            foreach (StepShape step in declared)
            {
                if (step.Role is not null && !roles.ContainsKey(step.Role))
                {
                    step.Body.Error("flow.role", "role", $"The step's role '{step.Role}' is not one of the flow's roles.");
                }
            }
        }

        var branches = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader branch in body.NestedArray("branches", false, allowEmpty: false) ?? [])
        {
            if (branch.Text("name", true, 100, Patterns.Slug, "a branch name") is string name)
            {
                branches.Add(name);
            }

            foreach (BodyReader predicate in branch.NestedArray("when", true, allowEmpty: false) ?? [])
            {
                Predicate(predicate, subject, extension, instance: false);
            }

            branch.StringArray("steps", true, allowEmpty: false, maxItemLength: 100, itemPattern: Patterns.Slug, patternHint: "a step key");
            branch.Text("returnTo", false, 100, Patterns.Slug, "a step key");
            branch.NoOtherKeys();
        }

        // A failure route that enters a branch names one this content declares; an extension's route to its base's branch is checked
        // when the flow is resolved.
        if (!extension)
        {
            foreach (StepShape step in declared.Where(s => s.FailureBranch is not null && !branches.Contains(s.FailureBranch)))
            {
                step.Body.Error("flow.failure", "failure", $"The step's failure route enters branch '{step.FailureBranch}', which the flow does not declare.");
            }
        }

        body.NoOtherKeys();
    }

    /// <summary>A flow's subject: what a run is about, and the fields a run's plan binds, by name and type.</summary>
    private static Dictionary<string, string> Subject(BodyReader? subject)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (subject is null)
        {
            return fields;
        }

        subject.Text("summary", true, 2000);
        foreach (BodyReader field in subject.NestedArray("fields", true, allowEmpty: true) ?? [])
        {
            string? name = field.Text("name", true, 60, Patterns.Slug, "a lower-case field name");
            string? type = field.Enum("type", true, SubjectTypes);
            string? recordType = field.Text("recordType", type == "record", 100, Patterns.Slug, "a record type");
            if (recordType is not null && RecordTypeRegistry.Find(recordType) is null)
            {
                field.Error("flow.subject", "recordType", $"'{recordType}' is not a record type this build reads.");
            }

            if (type != "record" && field.Has("recordType"))
            {
                field.Error("flow.subject", "recordType", "Only a record field names a record type.");
            }

            field.NoOtherKeys();
            if (name is not null && type is not null && !fields.TryAdd(name, type))
            {
                field.Error("flow.duplicate", "name", "Each subject field is declared once.");
            }
        }

        subject.NoOtherKeys();
        return fields;
    }

    private sealed record StepShape(BodyReader Body, string? Key, string? Kind, string? Role, IReadOnlyDictionary<string, string> Outputs, string? Each, string? FailureBranch = null);

    private static StepShape Step(BodyReader step, Dictionary<string, string> subject, bool extension, List<StepShape> earlier, Dictionary<string, string> roles)
    {
        string? key = step.Text("key", true, 100, Patterns.Slug, "a step key");
        if (key is not null && earlier.Any(e => e.Key == key))
        {
            step.Error("flow.duplicate", "key", "Each step key is declared once.");
        }

        step.Text("title", true, 300);
        string? kind = step.Enum("kind", true, StepKinds);
        string? role = step.Text("role", true, 60, Patterns.Slug, "a role of the flow");
        Marking(step, "step");

        string? reserved = step.Enum("reserved", false, ReservedActs);
        string? following = step.Text("following", false, 100, Patterns.Slug, "a step key");
        if (following is not null && !extension)
        {
            step.Error("flow.following", "following", "Only an extension places a step after a named step of its base.");
        }

        string? each = step.Text("each", false, 60, Patterns.Slug, "a list field of the subject");
        if (each is not null && !extension && subject.GetValueOrDefault(each) != "list")
        {
            step.Error("flow.each", "each", $"'{each}' is not a list field of the flow's subject, which an each step runs over, one instance per name.");
        }

        IReadOnlyList<string>? after = step.StringArray("after", false, allowEmpty: true, maxItemLength: 100, itemPattern: Patterns.Slug, patternHint: "a step key");
        foreach (string waited in after ?? [])
        {
            if (!extension && !earlier.Any(e => e.Key == waited))
            {
                step.Error("flow.after", "after", $"'{waited}' is not a step declared before this one; a step waits only on earlier steps.");
            }
        }

        // A predicate may name the record that carries one of the step's own inputs, {input.<name>.id}; a reserved step's done predicates
        // may also name its operator request, {request}, whose close by the operator is the record of an act that leaves no other.
        HashSet<string> declaredInputs = TJson.Array(step.Element, "inputs").Select(i => TJson.Text(i, "name")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (BodyReader predicate in step.NestedArray("requires", false, allowEmpty: false) ?? [])
        {
            Predicate(predicate, subject, extension, each is not null, declaredInputs);
        }

        IReadOnlyList<BodyReader> done = step.NestedArray("done", false, allowEmpty: false) ?? [];
        foreach (BodyReader predicate in done)
        {
            Predicate(predicate, subject, extension, each is not null, declaredInputs, request: reserved is not null);
        }

        if (kind is not null && Routed.IndexOf(kind) < 0 && done.Count == 0)
        {
            step.Error("flow.done", "done", $"A {kind} step is done when its done predicates hold; it declares at least one.");
        }

        string? action = step.Text("action", false, 4000);
        step.Text("recovery", true, 4000);
        var traps = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader trap in step.NestedArray("traps", false, allowEmpty: false) ?? [])
        {
            string? name = trap.Text("name", true, 100, Patterns.Slug, "a trap name");
            trap.Text("summary", true, 2000);
            // A trap is record predicates the position reads, or a named check the step's action runs on its machine before it acts and
            // refuses on, such as release submit's release.client-build objection, or both; a check alone is never evaluated from records.
            string? check = trap.Text("check", false, 200, CheckPattern(), "a check or objection name, such as release.client-build");
            foreach (BodyReader predicate in trap.NestedArray("holds", check is null, allowEmpty: false) ?? [])
            {
                Predicate(predicate, subject, extension, each is not null, declaredInputs);
            }

            trap.NoOtherKeys();
            if (name is not null && !traps.Add(name))
            {
                trap.Error("flow.duplicate", "name", "Each trap of a step is named once.");
            }
        }

        var inputs = new HashSet<string>(StringComparer.Ordinal);
        int fromSteps = 0;
        foreach (BodyReader input in step.NestedArray("inputs", false, allowEmpty: false) ?? [])
        {
            string? name = input.Text("name", true, 60, Patterns.Slug, "an input name");
            string? from = input.Enum("from", true, "subject", "step");
            if (from == "subject")
            {
                string? field = input.Text("field", true, 60, Patterns.Slug, "a subject field");
                if (field is not null && !extension && !subject.ContainsKey(field))
                {
                    input.Error("flow.input", "field", $"'{field}' is not a field of the flow's subject.");
                }
            }
            else if (from == "step")
            {
                fromSteps++;
                string? source = input.Text("step", true, 100, Patterns.Slug, "a step key");
                string? output = input.Text("output", true, 60, Patterns.Slug, "an output name");
                StepShape? producer = earlier.FirstOrDefault(e => e.Key == source);
                if (source is not null && producer is null && !extension)
                {
                    input.Error("flow.input", "step", $"'{source}' is not a step declared before this one; an input comes from an earlier step's output.");
                }
                else if (producer is not null && output is not null && !producer.Outputs.ContainsKey(output))
                {
                    input.Error("flow.input", "output", $"Step '{source}' declares no output '{output}'.");
                }
                else if (producer is not null && kind == "review" && producer.Kind != "work")
                {
                    input.Error("flow.review", "step", "A review step reviews the result of a work step.");
                }
            }

            input.NoOtherKeys();
            if (name is not null && !inputs.Add(name))
            {
                input.Error("flow.duplicate", "name", "Each input of a step is named once.");
            }
        }

        if (kind == "review" && fromSteps != 1)
        {
            step.Error("flow.review", "inputs", "A review step names exactly one input from a work step's output: the result it reviews.");
        }

        // A reserved act waits on the operator with its exact command, computed from the records only when the step is next, and is
        // done when the records of the operator's act hold (design/flow.md § 8; design/release-procedure.md § 6).
        if (reserved is not null)
        {
            if (action is null)
            {
                step.Error("flow.reserved", "action", "A reserved step declares its action: the exact command the operator runs, which its operator request shows only when the step is next.");
            }

            if (done.Count == 0)
            {
                step.Error("flow.reserved", "done", "A reserved step declares its done predicates: the records of the operator's act, from which its completion is read.");
            }

            // A derived state rests on records other than those counted, a work order's status or acceptance, an ask's answers or a
            // document's clearance, so it cannot show that the operator's own act made it true.
            if (done.Any(d => d.Has("state")))
            {
                step.Error("flow.reserved", "done", "A reserved step's done predicates count the records of the operator's act itself and name no state, which other records decide.");
            }

            foreach (string placeholder in Placeholders(action))
            {
                if (!CommandBinds(placeholder, subject, extension, each is not null, inputs))
                {
                    step.Error("flow.binding", "action", $"{{{placeholder}}} is not a binding of the run: a reserved step's command binds {{subject.<record or text field>}}, {{instance}} in an each step, {{goal}}, {{work}}, or {{input.<its input>.id}} and {{input.<its input>.hash}}.");
                }
            }
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BodyReader output in step.NestedArray("outputs", false, allowEmpty: false) ?? [])
        {
            string? itemKind = Item(output, kind, done, inputs, extension, roles);
            string? name = TJson.Text(output.Element, "name");
            if (name is not null && itemKind is not null && !outputs.TryAdd(name, itemKind))
            {
                output.Error("flow.duplicate", "name", "Each item of a step is named once.");
            }
        }

        if (step.Has("review"))
        {
            Review(step, "review", extension, roles);
            if (kind == "review")
            {
                step.Error("flow.review", "review", "A review step is the review; its own review is not reviewed again.");
            }
        }

        BodyReader? when = step.Nested("when", false);
        if (when is not null)
        {
            When(when, earlier, extension, done.Count > 0);
            if (when.Has("watch") && kind is not null && Routed.IndexOf(kind) >= 0)
            {
                when.Error("flow.when", "watch", "A watch reads the records until its predicates hold; a work or review step is carried by an assignment instead.");
            }
        }

        BodyReader? flow = step.Nested("flow", kind == "flow");
        if (flow is not null)
        {
            TemplateRecords.ReadPin(flow);
            flow.NoOtherKeys();
            if (kind != "flow")
            {
                step.Error("flow.kind", "flow", "Only a flow step names another flow to run.");
            }
        }

        BodyReader? effect = step.Nested("effect", kind == "effect");
        if (effect is not null)
        {
            effect.Enum("route", true, Routes);
            effect.Text("provider", false, 200, Patterns.Contribution, "namespace/name");
            effect.Text("operation", false, 200);
            effect.NoOtherKeys();
            if (kind != "effect")
            {
                step.Error("flow.kind", "effect", "Only an effect step names a route for an outside effect.");
            }
        }

        BodyReader? exchange = step.Nested("exchange", kind == "exchange");
        if (exchange is not null)
        {
            Exchange(exchange, inputs);
            if (kind != "exchange")
            {
                step.Error("flow.kind", "exchange", "Only an exchange step pairs an export with an intake.");
            }
        }

        string? failureBranch = null;
        BodyReader? failure = step.Nested("failure", false);
        if (failure is not null)
        {
            failureBranch = Failure(failure, step, kind, earlier, extension);
        }

        step.NoOtherKeys();
        return new StepShape(step, key, kind, role, outputs, each, failureBranch);
    }

    /// <summary>
    /// A work step's failure route (design/flow.md § 7): route branch with the branch it enters, route step with the earlier step the run
    /// returns to, or route stop. Returns the branch it enters, checked once the flow's branches are read.
    /// </summary>
    private static string? Failure(BodyReader failure, BodyReader step, string? kind, List<StepShape> earlier, bool extension)
    {
        string? route = failure.Enum("route", true, FailureRoutes);
        string? branch = failure.Text("branch", route == "branch", 100, Patterns.Slug, "a branch name");
        string? target = failure.Text("step", route == "step", 100, Patterns.Slug, "a step key");
        failure.NoOtherKeys();
        if (kind is not null && kind != "work")
        {
            step.Error("flow.failure", "failure", "A work step declares its failure route; a review's failure is the failure of the work it reviews, and the other kinds are carried by a person in this build.");
        }

        if (route != "branch" && failure.Has("branch"))
        {
            failure.Error("flow.failure", "branch", "Only a failure route of route branch names a branch.");
        }

        if (route != "step" && failure.Has("step"))
        {
            failure.Error("flow.failure", "step", "Only a failure route of route step names the step the run returns to.");
        }

        if (target is not null && !extension && earlier.All(e => e.Key != target))
        {
            failure.Error("flow.failure", "step", $"'{target}' is not a step declared before this one; a failed step returns the run to an earlier step.");
        }
        else if (earlier.FirstOrDefault(e => e.Key == target) is { Kind: "effect" or "exchange" } outside)
        {
            failure.Error("flow.failure", "step", $"'{target}' is an {outside.Kind} step: a failure route never repeats an outside effect.");
        }

        return route == "branch" ? branch : null;
    }

    /// <summary>
    /// One item a step produces or collects before it closes (design/flow.md § 5): its name and kind, required or optional with a
    /// default, and its completion requirements: its provenance (who must produce it, by role, principal or holder; from which of the
    /// step's inputs, by hash; with which provider, tool or settings), its evidence (named verifications passing at its exact hash) and
    /// its review, the step's when it declares none. A review step's one item is the review it records: a record of type review.
    /// Returns the item's kind.
    /// </summary>
    private static string? Item(BodyReader output, string? kind, IReadOnlyList<BodyReader> done, HashSet<string> inputs, bool extension, Dictionary<string, string> roles)
    {
        output.Text("name", true, 60, Patterns.Slug, "an item name");
        string? itemKind = output.Enum("kind", true, ItemKinds);
        output.Text("mediaType", false, 100);
        string? recordType = output.Text("recordType", itemKind is "record" or "attestation", 100, Patterns.Slug, "a record type");
        output.Text("recordKind", false, 100);
        Marking(output, "item");
        BodyReader? provenance = output.Nested("provenance", false);
        if (provenance is not null)
        {
            provenance.Enum("holder", false, Holders);
            string? role = provenance.Text("role", false, 60, Patterns.Slug, "a role of the flow");
            if (role is not null && !extension && !roles.ContainsKey(role))
            {
                provenance.Error("flow.role", "role", $"'{role}' is not one of the flow's roles.");
            }

            provenance.Ulid("principal", false);
            foreach (string named in provenance.StringArray("inputs", false, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "an input name") ?? [])
            {
                if (!inputs.Contains(named))
                {
                    provenance.Error("flow.provenance", "inputs", $"'{named}' is not an input of the step; an item comes from the step's inputs, each pinned by hash.");
                }
            }

            provenance.Text("provider", false, 200);
            provenance.Text("tool", false, 200);
            BodyReader? settings = provenance.Nested("settings", false);
            if (settings is not null)
            {
                foreach (JsonProperty setting in settings.Element.EnumerateObject())
                {
                    settings.Text(setting.Name, true, 200);
                }

                if (!settings.Element.EnumerateObject().Any())
                {
                    settings.Error("flow.provenance", "settings", "A settings requirement names at least one setting.");
                }
            }

            provenance.NoOtherKeys();
            if (itemKind is not ("file" or "record" or "observation") && (provenance.Has("inputs") || provenance.Has("provider") || provenance.Has("tool") || provenance.Has("settings")))
            {
                provenance.Error("flow.provenance", "inputs", "Inputs, a provider, a tool and settings are the provenance of an item a result or an operation produces: a file, a record or an observation.");
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader evidence in output.NestedArray("evidence", false, allowEmpty: false) ?? [])
        {
            string? name = evidence.Text("name", true, 100, Patterns.Slug, "an evidence name");
            evidence.Text("method", true, 200);
            evidence.Text("command", false, 4000);
            evidence.Enum("role", false, VerificationType.Roles);
            evidence.NoOtherKeys();
            if (name is not null && !names.Add(name))
            {
                evidence.Error("flow.duplicate", "name", "Each piece of evidence of an item is named once.");
            }
        }

        bool reviews = kind == "review";
        if (output.Has("review"))
        {
            Review(output, "review", extension, roles);
            if (reviews)
            {
                output.Error("flow.review", "review", "A review step's review is not reviewed again.");
            }
        }

        output.NoOtherKeys();
        if (itemKind != "file" && output.Has("mediaType"))
        {
            output.Error("flow.output", "mediaType", "Only a file names a media type.");
        }

        if (itemKind is not ("record" or "attestation") && output.Has("recordType"))
        {
            output.Error("flow.output", "recordType", "Only a record or an attestation names its record type; the other kinds are their own types.");
        }

        if (itemKind is not ("record" or "observation" or "attestation") && output.Has("recordKind"))
        {
            output.Error("flow.output", "recordKind", "Only a record, an observation or an attestation names the kind its record has.");
        }

        if (recordType is not null && RecordTypeRegistry.Find(recordType) is null)
        {
            output.Error("flow.output", "recordType", $"'{recordType}' is not a record type this build reads.");
        }

        if (itemKind == "file" && kind != "work")
        {
            output.Error("flow.output", "kind", "A file is carried by a work step's result, by name and hash.");
        }

        if (reviews != (itemKind == "record" && recordType == "review"))
        {
            output.Error("flow.output", "kind", reviews ? "A review step's item is the review it records: a record of type review." : "A review is a review step's item.");
        }

        if (!reviews && Counted(itemKind, recordType) is string counted && !done.Any(p => counted.Split('|').Contains(TJson.Text(p.Element, "type"), StringComparer.Ordinal)))
        {
            output.Error("flow.output", "kind", $"A{(itemKind is "observation" or "attestation" ? "n" : string.Empty)} {itemKind} is the newest {counted.Replace("|", " or ", StringComparison.Ordinal)} record the step's done predicates count; declare a done predicate of that type.");
        }

        return itemKind;
    }

    /// <summary>
    /// The record types an item is counted from by its step's done predicates: a record's or an attestation's declared type, an
    /// observation's operation or observation record, a verification, a decision; null for a file.
    /// </summary>
    public static string? Counted(string? kind, string? recordType) => kind switch
    {
        "record" or "attestation" => recordType,
        "observation" => "operation|observation",
        "verification" or "decision" => kind,
        _ => null,
    };

    /// <summary>Whether a step or an item is required or optional: exactly one of required: true or optional: true, an optional one with its default.</summary>
    private static void Marking(BodyReader body, string what)
    {
        bool? required = body.Flag("required", false);
        bool? optional = body.Flag("optional", false);
        if (required == false || optional == false || (required == true) == (optional == true))
        {
            body.Error("flow.marking", "required", $"Each {what} is marked either required: true or optional: true.");
        }

        body.Enum("default", optional == true, OnOff);
        if (optional != true && body.Has("default"))
        {
            body.Error("flow.marking", "default", $"A required {what} always counts; only an optional {what} has a default, on or off.");
        }
    }

    /// <summary>A review requirement: its independence, as a word or as an object with the reviewer's role.</summary>
    private static void Review(BodyReader owner, string name, bool extension, Dictionary<string, string> roles)
    {
        JsonElement value = TJson.Object(owner.Element, name);
        if (value.ValueKind == JsonValueKind.String)
        {
            owner.Enum(name, false, Reviews);
            return;
        }

        BodyReader? review = owner.Nested(name, false);
        if (review is null)
        {
            return;
        }

        review.Enum("independence", true, Reviews);
        string? role = review.Text("role", false, 60, Patterns.Slug, "a role of the flow");
        review.NoOtherKeys();
        if (role is not null && !extension && !roles.ContainsKey(role))
        {
            review.Error("flow.role", "role", $"'{role}' is not one of the flow's roles.");
        }
    }

    /// <summary>An exchange (design/flow.md §§ 5 and 9): the package exported, by input, with its question, format and expiry, and the routes.</summary>
    private static void Exchange(BodyReader exchange, HashSet<string> inputs)
    {
        BodyReader? export = exchange.Nested("export", true);
        if (export is not null)
        {
            IReadOnlyList<string>? material = export.StringArray("material", true, allowEmpty: false, maxItemLength: 60, itemPattern: Patterns.Slug, patternHint: "an input name");
            foreach (string name in material ?? [])
            {
                if (!inputs.Contains(name))
                {
                    export.Error("flow.exchange", "material", $"'{name}' is not an input of the step; an export packages the step's inputs, each by hash.");
                }
            }

            export.Text("question", true, 4000);
            BodyReader? format = export.Nested("format", true);
            if (format is not null)
            {
                format.Text("mediaType", true, 100);
                format.Text("summary", false, 2000);
                format.NoOtherKeys();
            }

            Duration(export, "expires", true);
            export.Enum("route", true, Routes);
            export.NoOtherKeys();
        }

        BodyReader? intake = exchange.Nested("intake", true);
        if (intake is not null)
        {
            intake.Enum("route", true, Routes);
            intake.NoOtherKeys();
        }

        exchange.NoOtherKeys();
    }

    /// <summary>A step's timing (design/flow.md § 7): a due time after an earlier step is done, or a watch repeated until its done predicates hold or a limit passes.</summary>
    private static void When(BodyReader when, List<StepShape> earlier, bool extension, bool hasDone)
    {
        bool due = when.Has("due");
        bool watch = when.Has("watch");
        if (due == watch)
        {
            when.Error("flow.when", "due", "A step's timing is a due time or a watch, one of them.");
        }

        BodyReader? at = when.Nested("due", false);
        if (at is not null)
        {
            Duration(at, "after", true);
            string? from = at.Text("from", true, 100, Patterns.Slug, "a step key");
            if (from is not null && !extension && !earlier.Any(e => e.Key == from))
            {
                at.Error("flow.when", "from", $"'{from}' is not a step declared before this one; a due time counts from an earlier step's completion.");
            }

            at.NoOtherKeys();
        }

        BodyReader? repeated = when.Nested("watch", false);
        if (repeated is not null)
        {
            Duration(repeated, "every", true);
            Duration(repeated, "limit", true);
            repeated.NoOtherKeys();
            if (!hasDone)
            {
                repeated.Error("flow.when", "watch", "A watch repeats until the step's done predicates hold; declare them.");
            }
        }

        when.NoOtherKeys();
    }

    private static void ReadOptions(BodyReader body)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader option in body.NestedArray("options", false, false) ?? [])
        {
            string? id = option.Text("id", true, 100, Patterns.Slug, "an option id");
            if (id is not null && !ids.Add(id)) option.Error("template.duplicate", "id", "Option identifiers are unique within their owner.");
            IReadOnlyList<string>? values = option.StringArray("values", true, false, 300);
            string? value = option.Text("default", true, 300);
            if (value is not null && values is not null && (!values.Contains(value, StringComparer.Ordinal) || values.Distinct(StringComparer.Ordinal).Count() != values.Count))
                option.Error("template.option", "default", "Allowed values are distinct and include the default.");
            option.NoOtherKeys();
        }
    }

    /// <summary>
    /// A record predicate over the run (the goal completion language of design/goals.md § 5), whose string values may name the run's
    /// bindings: {subject.&lt;field&gt;} (a record field's id or a text field's value), {instance} in an each step, {goal}, {work},
    /// {input.&lt;name&gt;.id} for one of the step's own inputs (the id of the record that carries it), and, as a reserved step's done
    /// predicate's subject only, {request}: the step's operator request.
    /// </summary>
    internal static void Predicate(BodyReader predicate, IReadOnlyDictionary<string, string> subject, bool extension, bool instance,
        IReadOnlySet<string>? inputs = null, bool request = false)
    {
        string? type = predicate.Text("type", true, 100, Patterns.Slug, "a record type");
        if (type is not null && RecordTypeRegistry.Find(type) is null)
        {
            predicate.Error("goal.predicate", "type", $"'{type}' is not a record type this build reads.");
        }

        string? named = predicate.Text("subject", false, 100);
        if (named is not null && !Identity.Ulid.IsValid(named) && !Binds(named, subject, extension, instance, record: true, inputs, request))
        {
            predicate.Error("flow.binding", "subject", "A predicate's subject is a record id or a record binding of the run: {subject.<record field>}, {goal}, {work}, {input.<its input>.id}, or {request} in a reserved step's done predicate.");
        }

        string? product = predicate.Text("product", false, 100);
        if (product is not null && !Identity.Ulid.IsValid(product) && !Binds(product, subject, extension, instance, record: true, inputs))
        {
            predicate.Error("flow.binding", "product", "A predicate's product is a product id or a record binding of the run.");
        }

        if (product is not null && type is not null && type != "living-document")
        {
            predicate.Error("goal.predicate", "product", "'product' names the product whose living documents are counted; it goes with type living-document.");
        }

        BodyReader? fields = predicate.Nested("fields", false);
        if (fields is not null)
        {
            foreach (JsonProperty field in fields.Element.EnumerateObject())
            {
                fields.Allow(field.Name);
                if (field.Value.ValueKind != JsonValueKind.String)
                {
                    fields.Error("body.type", field.Name, "A predicate compares a body field with a string value.");
                }
                else if (Binding().IsMatch(field.Value.GetString()!) && !Binds(field.Value.GetString()!, subject, extension, instance, record: false, inputs))
                {
                    fields.Error("flow.binding", field.Name, "A binding names a record or text field of the subject, {instance} in an each step, {goal}, {work} or {input.<its input>.id}.");
                }
            }
        }

        string? state = predicate.Text("state", false, 100);
        if (state is not null && type is not null && RecordTypeRegistry.Find(type) is not null)
        {
            if (!GoalPredicates.States.TryGetValue(type, out IReadOnlyList<string>? states))
            {
                predicate.Error("goal.predicate", "state", $"'state' is the state status derives, for {string.Join(", ", GoalPredicates.States.Keys)} records.");
            }
            else if (!states.Contains(state, StringComparer.Ordinal))
            {
                predicate.Error("goal.predicate", "state", $"The state of a {type} record is one of: {string.Join(", ", states)}.");
            }
        }

        long? min = predicate.Number("min", false, 0, 1_000_000);
        long? max = predicate.Number("max", false, 0, 1_000_000);
        if (min is not null && max is not null && max < min)
        {
            predicate.Error("goal.predicate", "max", "'max' is at least 'min'.");
        }

        predicate.NoOtherKeys();
    }

    private static bool Binds(string value, IReadOnlyDictionary<string, string> subject, bool extension, bool instance, bool record,
        IReadOnlySet<string>? inputs = null, bool request = false)
    {
        Match match = Binding().Match(value);
        if (!match.Success)
        {
            return false;
        }

        string name = match.Groups["name"].Value;
        if (name is "goal" or "work")
        {
            return true;
        }

        if (name == "request")
        {
            return request && record;
        }

        if (name.StartsWith("input.", StringComparison.Ordinal))
        {
            return inputs is not null && inputs.Contains(name["input.".Length..^".id".Length]);
        }

        if (name == "instance")
        {
            return instance && !record;
        }

        string field = name["subject.".Length..];
        // An extension binds its base's subject, which is checked when the flow is resolved.
        return extension || (subject.TryGetValue(field, out string? type) && (type == "record" || (!record && type == "text")));
    }

    /// <summary>
    /// Whether a placeholder of a reserved step's command names a binding of the run: a record or text field of the subject, the
    /// instance in an each step, the goal or the work order, or the id or hash of one of the step's own inputs, as they stand when the
    /// step is next.
    /// </summary>
    private static bool CommandBinds(string name, Dictionary<string, string> subject, bool extension, bool instance, HashSet<string> inputs)
    {
        if (name is "goal" or "work")
        {
            return true;
        }

        if (name == "instance")
        {
            return instance;
        }

        if (name.StartsWith("input.", StringComparison.Ordinal))
        {
            string[] parts = name.Split('.');
            return parts.Length == 3 && inputs.Contains(parts[1]) && parts[2] is "id" or "hash";
        }

        if (name.StartsWith("subject.", StringComparison.Ordinal) && name.IndexOf('.', "subject.".Length) < 0)
        {
            string field = name["subject.".Length..];
            return extension || (subject.TryGetValue(field, out string? type) && type is "record" or "text");
        }

        return false;
    }

    /// <summary>The placeholders a reserved step's command names, without their braces: lower-case names in braces; other braces are literal text.</summary>
    public static IEnumerable<string> Placeholders(string? command) =>
        command is null ? [] : Placeholder().Matches(command).Select(m => m.Groups["name"].Value);

    /// <summary>A placeholder of a reserved step's command: a lower-case name in braces, such as {subject.product} or {input.candidate.hash}.</summary>
    [GeneratedRegex(@"\{(?<name>[a-z][a-z0-9]*(?:[.\-][a-z0-9]+)*)\}")]
    internal static partial Regex Placeholder();

    /// <summary>An ISO 8601 duration of days, hours, minutes and seconds, such as P1D or PT6H; null when the text is not one.</summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (text is null || DurationPattern().Match(text) is not { Success: true } match || match.Groups.Values.Skip(1).All(g => !g.Success))
        {
            return null;
        }

        long Part(string name) => match.Groups[name].Success ? long.Parse(match.Groups[name].Value, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
        long seconds = Part("d") * 86400 + Part("h") * 3600 + Part("m") * 60 + Part("s");
        return seconds is > 0 and <= 3_650L * 86400 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static void Duration(BodyReader body, string name, bool required)
    {
        string? text = body.Text(name, required, 40);
        if (text is not null && ParseDuration(text) is null)
        {
            body.Error("flow.duration", name, "A duration is ISO 8601 days, hours, minutes and seconds, such as P7D or PT6H, from one second to ten years.");
        }
    }

    /// <summary>The binding a predicate value names: {subject.&lt;field&gt;}, {instance}, {goal}, {work}, {input.&lt;name&gt;.id} or {request}.</summary>
    [GeneratedRegex(@"^\{(?<name>subject\.[a-z0-9]+(?:-[a-z0-9]+)*|instance|goal|work|request|input\.[a-z0-9]+(?:-[a-z0-9]+)*\.id)\}\z")]
    internal static partial Regex Binding();

    /// <summary>A check or objection a step's action runs: lower-case words joined by dots, hyphens or a slash, such as release.client-build.</summary>
    [GeneratedRegex(@"^[a-z0-9]+([.\-/][a-z0-9]+)*$")]
    private static partial Regex CheckPattern();

    /// <summary>A step key, or a step key and an item name joined by a dot.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)?$")]
    private static partial Regex OffPattern();

    [GeneratedRegex(@"^P(?:(?<d>\d{1,5})D)?(?:T(?:(?<h>\d{1,6})H)?(?:(?<m>\d{1,8})M)?(?:(?<s>\d{1,10})S)?)?$")]
    private static partial Regex DurationPattern();
}
