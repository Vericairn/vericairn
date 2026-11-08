// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Templates;

/// <summary>
/// A role of a flow: who may hold it and its role brief, which a step's brief carries; the estate's own copy of the brief, kept in its
/// repository as a living document, when the role names one (design/process-profiles.md § 11).
/// </summary>
/// <param name="Id">The role's name, by which the roster routes its steps.</param>
/// <param name="Holder">person, agent or either: who may hold it.</param>
/// <param name="Brief">The role brief the flow ships.</param>
/// <param name="Document">The repository-relative path of the estate's copy of the role brief, a living document; null when the flow's brief stands alone.</param>
public sealed record FlowRole(string Id, string Holder, string Brief, string? Document = null);

/// <summary>
/// What a failed work step does (design/flow.md § 7): enter a branch, return the run to an earlier step, or stop for the sponsor.
/// </summary>
/// <param name="Route">branch, step or stop.</param>
/// <param name="Branch">For route branch, the branch it enters.</param>
/// <param name="Step">For route step, the earlier step the run returns to.</param>
public sealed record FlowFailure(string Route, string? Branch, string? Step)
{
    /// <summary>The route of a step that declares none: a stop, for the sponsor.</summary>
    public static FlowFailure Stop { get; } = new("stop", null, null);

    /// <summary>The route in words.</summary>
    public override string ToString() => Route switch
    {
        "branch" => $"it enters branch {Branch}",
        "step" => $"the run returns to {Step}",
        _ => "it stops, and the sponsor is asked",
    };
}

/// <summary>An input of a step: a field of the run's subject, or a named output of an earlier step, which the step's brief pins by hash.</summary>
/// <param name="Name">The input's name within the step.</param>
/// <param name="From">subject or step.</param>
/// <param name="Field">For a subject input, the subject field.</param>
/// <param name="Step">For a step input, the producing step's key.</param>
/// <param name="Output">For a step input, the producing step's output name.</param>
public sealed record FlowInput(string Name, string From, string? Field, string? Step, string? Output);

/// <summary>A review an item takes before its step closes: its independence and, when named, the role whose principal reviews it.</summary>
/// <param name="Independence">none, self, independent, other-harness or person.</param>
/// <param name="Role">The flow role the reviewer is named in, or null for any principal the independence admits.</param>
public sealed record FlowReview(string Independence, string? Role);

/// <summary>
/// Who or what must produce an item: a holder kind, a role or a principal; the step inputs it comes from, by hash; and the provider,
/// tool or settings its producing result or operation records.
/// </summary>
/// <param name="Holder">person, agent or either; null for any.</param>
/// <param name="Role">The flow role the producer is named in; null for any.</param>
/// <param name="Principal">The one principal that must produce it; null for any.</param>
/// <param name="Inputs">The step's inputs it comes from, each pinned by hash.</param>
/// <param name="Provider">The provider its producer used; null for any.</param>
/// <param name="Tool">The tool, such as a launcher, its producer used; null for any.</param>
/// <param name="Settings">The settings its producer used, by name; undefined for any.</param>
public sealed record FlowProvenance(string? Holder, string? Role, string? Principal, IReadOnlyList<string> Inputs, string? Provider, string? Tool, JsonElement Settings);

/// <summary>A named verification an item needs, passing at the item's exact hash.</summary>
/// <param name="Name">The evidence's name within the item.</param>
/// <param name="Method">The verification's method, such as a command or a named tool.</param>
/// <param name="Command">The command it ran, when named.</param>
/// <param name="Role">The verification's role (self, independent-agent, operator or automation), when named.</param>
public sealed record FlowEvidence(string Name, string Method, string? Command, string? Role);

/// <summary>
/// One item a step produces or collects before it closes, declared in its outputs (design/flow.md § 5): its name and kind, required or
/// optional with a default, and its completion requirements: its provenance, its evidence and its review.
/// </summary>
/// <param name="Name">The item's name, by which a later step's inputs name it.</param>
/// <param name="Kind">file, record, observation, verification, decision or attestation.</param>
/// <param name="MediaType">For a file, its declared media type.</param>
/// <param name="RecordType">For a record or an attestation, its record type.</param>
/// <param name="RecordKind">For a record, an observation or an attestation, the value of the record's kind field it must have.</param>
/// <param name="Required">Whether it always counts; an optional item counts by its configured value.</param>
/// <param name="DefaultOn">For an optional item, whether it counts when nothing configures it.</param>
/// <param name="Provenance">Who or what must produce it; null for any.</param>
/// <param name="Evidence">The verifications it needs.</param>
/// <param name="Review">Its own review; null when the step's review applies.</param>
/// <param name="Element">The item's declared content.</param>
public sealed record FlowOutput(string Name, string Kind, string? MediaType, string? RecordType, string? RecordKind, bool Required, bool DefaultOn,
    FlowProvenance? Provenance, IReadOnlyList<FlowEvidence> Evidence, FlowReview? Review, JsonElement Element)
{
    /// <summary>The record types the step's done predicates count it from, separated by |; null for a file.</summary>
    public string? Counts => FlowContract.Counted(Kind, RecordType);
}

/// <summary>
/// A check a step runs before its action, named and explained: record predicates the position reads, which hold when the trap is clear,
/// or a named check the step's action runs on its machine before it acts, such as an objection of release submit, or both.
/// </summary>
/// <param name="Name">The trap's name.</param>
/// <param name="Summary">What it guards against.</param>
/// <param name="Holds">The predicates that hold when the trap is clear; empty for a trap only its action checks.</param>
/// <param name="Check">The check or objection the step's action runs and refuses on, when it names one; never read from records.</param>
public sealed record FlowTrap(string Name, string Summary, IReadOnlyList<JsonElement> Holds, string? Check = null);

/// <summary>A branch: steps entered only when its predicates hold, such as a withdrawal, and the step the run returns to.</summary>
/// <param name="Name">The branch's name.</param>
/// <param name="When">The predicates that enter it.</param>
/// <param name="Steps">Its steps, which are off until it is entered.</param>
/// <param name="ReturnTo">The step the run returns to after it, when named.</param>
public sealed record FlowBranch(string Name, IReadOnlyList<JsonElement> When, IReadOnlyList<string> Steps, string? ReturnTo);

/// <summary>One step of a resolved flow, in the flow's order (design/flow.md § 5; design/release-procedure.md § 4).</summary>
/// <param name="Key">Stable within the flow.</param>
/// <param name="Title">In words.</param>
/// <param name="Kind">work, review, operation, effect, decision, exchange or flow.</param>
/// <param name="Role">The flow role that owns it, by which it is routed.</param>
/// <param name="Required">Whether it always runs; an optional step runs by its configured value.</param>
/// <param name="DefaultOn">For an optional step, whether it runs when nothing configures it.</param>
/// <param name="Reserved">The reserved act it is, or null.</param>
/// <param name="After">The steps it waits on: those it names, or the step before it.</param>
/// <param name="Requires">Its preconditions, record predicates over the run.</param>
/// <param name="Done">Its done predicates.</param>
/// <param name="Action">What carries it, in words.</param>
/// <param name="Recovery">What to do when it fails, its outcome is uncertain, or its inputs changed after use.</param>
/// <param name="Traps">The checks it runs before its action.</param>
/// <param name="Inputs">Its inputs, which its brief pins.</param>
/// <param name="Outputs">Its outputs, the hand-off contract.</param>
/// <param name="Review">The review its items take before it closes, as declared, the default for items that declare none; null when the flow's review option decides for a work step.</param>
/// <param name="Each">The subject list field it runs over, one instance per name; null for one instance.</param>
/// <param name="When">Its timing, a due time or a watch; undefined without one.</param>
/// <param name="Element">The step's declared content.</param>
/// <param name="DeclaredIn">The flow version that declares it.</param>
/// <param name="Failure">A work step's declared failure route; null when it declares none, and then a failure stops.</param>
public sealed record FlowStep(string Key, string Title, string Kind, string Role, bool Required, bool DefaultOn, string? Reserved, IReadOnlyList<string> After,
    IReadOnlyList<JsonElement> Requires, IReadOnlyList<JsonElement> Done, string? Action, string Recovery, IReadOnlyList<FlowTrap> Traps,
    IReadOnlyList<FlowInput> Inputs, IReadOnlyList<FlowOutput> Outputs, FlowReview? Review, string? Each, JsonElement When, JsonElement Element, TemplatePin DeclaredIn,
    FlowFailure? Failure = null)
{
    /// <summary>The failure route it takes: the one it declares, or a stop.</summary>
    public FlowFailure FailureRoute => Failure ?? FlowFailure.Stop;

    /// <summary>True for the kinds the conductor routes as an assignment of the run's work order: work and review.</summary>
    public bool IsRouted => FlowContract.Routed.IndexOf(Kind) >= 0;

    /// <summary>The option that configures an optional step: step.&lt;key&gt;.</summary>
    public string Option => FlowContract.StepOption + Key;

    /// <summary>The option that configures one of its optional items: item.&lt;key&gt;.&lt;name&gt;.</summary>
    public string ItemOption(FlowOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return FlowContract.ItemOption + Key + "." + output.Name;
    }
}

/// <summary>
/// A flow resolved for a run: its own content merged onto the bases it extends, in order (design/flow.md § 5). An extension adds roles,
/// options and steps, each placed after a named step or at the end, and may turn off a step its base declares optional; it never
/// redeclares a base step or role, so it never turns off a required step or a reserved marking. Resolving reads exact pinned content
/// only, grants nothing and is the same on every machine that holds that content.
/// </summary>
public sealed class FlowDefinition
{
    /// <summary>The deepest chain of extensions a flow resolves.</summary>
    public const int MaximumDepth = 8;

    private readonly Dictionary<string, FlowStep> byKey;

    private FlowDefinition(TemplateDefinition definition, IReadOnlyList<TemplatePin> chain, JsonElement subject, IReadOnlyList<FlowRole> roles, IReadOnlyList<FlowStep> steps,
        IReadOnlyList<FlowBranch> branches, IReadOnlyList<string> never, IReadOnlyList<JsonElement> triggers, IReadOnlyDictionary<string, TemplatePin> turnedOff,
        IReadOnlyDictionary<string, TemplatePin> turnedOffItems, IReadOnlyDictionary<string, JsonElement> options)
    {
        Definition = definition;
        Chain = chain;
        Subject = subject;
        Roles = roles;
        Steps = steps;
        Branches = branches;
        Never = never;
        Triggers = triggers;
        TurnedOff = turnedOff;
        TurnedOffItems = turnedOffItems;
        Options = options;
        byKey = steps.ToDictionary(s => s.Key, StringComparer.Ordinal);
        SubjectFields = TJson.Array(subject, "fields").ToDictionary(f => TJson.Text(f, "name")!, f => TJson.Text(f, "type")!, StringComparer.Ordinal);
    }

    /// <summary>The pinned flow version a run names.</summary>
    public TemplateDefinition Definition { get; }

    /// <summary>The exact pin of the flow version a run names.</summary>
    public TemplatePin Pin => new(Definition.Id, Definition.Version, Definition.Hash);

    /// <summary>The flow and the bases it extends, the deepest base first and this flow last.</summary>
    public IReadOnlyList<TemplatePin> Chain { get; }

    /// <summary>What a run is about: its summary and fields.</summary>
    public JsonElement Subject { get; }

    /// <summary>The subject's fields by name, with their kinds: record, text or list.</summary>
    public IReadOnlyDictionary<string, string> SubjectFields { get; }

    /// <summary>The flow's roles.</summary>
    public IReadOnlyList<FlowRole> Roles { get; }

    /// <summary>The steps, in the flow's order.</summary>
    public IReadOnlyList<FlowStep> Steps { get; }

    /// <summary>The branches.</summary>
    public IReadOnlyList<FlowBranch> Branches { get; }

    /// <summary>The lines carried verbatim into every brief.</summary>
    public IReadOnlyList<string> Never { get; }

    /// <summary>The triggers, each with its kind and summary.</summary>
    public IReadOnlyList<JsonElement> Triggers { get; }

    /// <summary>The optional base steps an extension turns off, each with the extension that does.</summary>
    public IReadOnlyDictionary<string, TemplatePin> TurnedOff { get; }

    /// <summary>The optional items an extension turns off, by step.item, each with the extension that does.</summary>
    public IReadOnlyDictionary<string, TemplatePin> TurnedOffItems { get; }

    /// <summary>
    /// The options a configuration sets, by name, each with its allowed values and default: the flows' own options, and step.&lt;key&gt;
    /// (on or off) for each optional step an extension has not turned off.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Options { get; }

    /// <summary>The step with a key, or null.</summary>
    public FlowStep? Step(string? key) => key is not null && byKey.TryGetValue(key, out FlowStep? step) ? step : null;

    /// <summary>The role with a name, or null.</summary>
    public FlowRole? Role(string? id) => Roles.FirstOrDefault(r => r.Id == id);

    /// <summary>The branch a step belongs to, or null for a step of the main sequence.</summary>
    public FlowBranch? BranchOf(string key) => Branches.FirstOrDefault(b => b.Steps.Contains(key, StringComparer.Ordinal));

    /// <summary>The position of a step in the flow's order.</summary>
    public int IndexOf(string key) => Steps.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Key == key, (null!, -1)).Item2;

    /// <summary>Resolves an exact pin from the estate's catalogue, with every base it extends; refuses content that is unavailable, changed or invalid.</summary>
    public static FlowDefinition Resolve(EstateModel model, TemplatePin pin)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(pin);
        return Resolve(TemplateCatalog.Resolve(model, pin), p => TemplateCatalog.Resolve(model, p));
    }

    /// <summary>Resolves a flow's content with every base it extends, read through a resolver of exact pins.</summary>
    public static FlowDefinition Resolve(TemplateDefinition definition, Func<TemplatePin, TemplateDefinition> bases)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(bases);
        var chain = new List<TemplateDefinition> { definition };
        while (TJson.Object(chain[^1].Content, "extends") is { ValueKind: JsonValueKind.Object } extends)
        {
            if (chain.Count > MaximumDepth)
            {
                throw new InvalidOperationException($"A flow extends at most {MaximumDepth} bases in a chain.");
            }

            chain.Add(bases(new TemplatePin(TJson.Text(extends, "id")!, TJson.Text(extends, "version")!, TJson.Text(extends, "hash")!)));
        }

        chain.Reverse();
        if (chain.Any(d => !d.IsFlow))
        {
            throw new InvalidOperationException("A flow extends only another flow.");
        }

        var errors = new List<string>();
        JsonElement subject = TJson.Object(chain[0].Content, "subject");
        Dictionary<string, string> fields = TJson.Array(subject, "fields").ToDictionary(f => TJson.Text(f, "name")!, f => TJson.Text(f, "type")!, StringComparer.Ordinal);
        var roles = new List<FlowRole>();
        var steps = new List<FlowStep>();
        var branches = new List<FlowBranch>();
        var never = new List<string>();
        var triggers = new List<JsonElement>();
        var options = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var turnedOff = new Dictionary<string, TemplatePin>(StringComparer.Ordinal);
        var turnedOffItems = new Dictionary<string, TemplatePin>(StringComparer.Ordinal);
        foreach (TemplateDefinition flow in chain)
        {
            var pin = new TemplatePin(flow.Id, flow.Version, flow.Hash);
            bool extension = flow != chain[0];
            string where = $"{flow.Id} {flow.Version}";
            foreach (JsonElement role in TJson.Array(flow.Content, "roles"))
            {
                string id = TJson.Text(role, "id")!;
                if (roles.Any(r => r.Id == id))
                {
                    errors.Add($"{where} declares role '{id}' again: an extension adds roles and never changes who may hold its base's.");
                    continue;
                }

                roles.Add(new FlowRole(id, TJson.Text(role, "holder")!, TJson.Text(role, "brief")!, TJson.Text(role, "document")));
            }

            foreach (JsonElement option in TJson.Array(flow.Content, "options"))
            {
                if (!options.TryAdd(TJson.Text(option, "id")!, option))
                {
                    errors.Add($"{where} declares option '{TJson.Text(option, "id")}' again.");
                }
            }

            never.AddRange(TJson.Strings(flow.Content, "never"));
            foreach (JsonElement trigger in TJson.Array(flow.Content, "triggers"))
            {
                if (triggers.Any(t => TJson.Text(t, "kind") == TJson.Text(trigger, "kind")))
                {
                    errors.Add($"{where} declares trigger '{TJson.Text(trigger, "kind")}' again.");
                    continue;
                }

                triggers.Add(trigger);
            }

            foreach (string key in TJson.Strings(flow.Content, "off").Where(k => k.Contains('.', StringComparison.Ordinal)))
            {
                string[] parts = key.Split('.');
                FlowOutput? output = steps.FirstOrDefault(s => s.Key == parts[0])?.Outputs.FirstOrDefault(o => o.Name == parts[1]);
                if (output is null)
                {
                    errors.Add($"{where} turns off item '{key}', which no base declares.");
                }
                else if (output.Required)
                {
                    errors.Add($"{where} turns off item '{key}', a required item: an extension turns off only an item its base declares optional.");
                }
                else if (!turnedOffItems.TryAdd(key, pin))
                {
                    errors.Add($"{where} turns off item '{key}', which a base already turned off.");
                }
            }

            foreach (string key in TJson.Strings(flow.Content, "off").Where(k => !k.Contains('.', StringComparison.Ordinal)))
            {
                FlowStep? step = steps.FirstOrDefault(s => s.Key == key);
                if (step is null)
                {
                    errors.Add($"{where} turns off '{key}', which no base declares.");
                }
                else if (step.Required)
                {
                    errors.Add($"{where} turns off '{key}', a required step: an extension turns off only a step its base declares optional.");
                }
                else if (!turnedOff.TryAdd(key, pin))
                {
                    errors.Add($"{where} turns off '{key}', which a base already turned off.");
                }
            }

            // The previous step a new step waits on by default is its predecessor in the merged order; an extension's steps are placed
            // after a named step, or at the end, and keep their declared order.
            var placedAfter = new Dictionary<string, string>(StringComparer.Ordinal);
            var own = new List<string>();
            foreach (JsonElement content in TJson.Array(flow.Content, "steps"))
            {
                string key = TJson.Text(content, "key")!;
                if (steps.Any(s => s.Key == key))
                {
                    errors.Add($"{where} declares step '{key}' again: an extension adds steps, and never redeclares a base step, its marking required or optional, or its reserved marking.");
                    continue;
                }

                int index = steps.Count;
                if (TJson.Text(content, "following") is string anchor)
                {
                    int at = steps.FindIndex(s => s.Key == (placedAfter.TryGetValue(anchor, out string? last) ? last : anchor));
                    if (at < 0)
                    {
                        errors.Add($"{where} places '{key}' after '{anchor}', which is not a step declared before it.");
                    }
                    else
                    {
                        index = at + 1;
                        placedAfter[anchor] = key;
                    }
                }

                List<string> earlier = steps.Take(index).Select(s => s.Key).ToList();
                FlowStep step = Read(content, pin, index == 0 ? null : steps[index - 1].Key);
                if (extension)
                {
                    errors.AddRange(Extension(where, step, earlier, steps, fields));
                }

                foreach (string role in new[] { step.Role, step.Review?.Role }.Concat(step.Outputs.SelectMany(o => new[] { o.Provenance?.Role, o.Review?.Role })).OfType<string>().Distinct(StringComparer.Ordinal))
                {
                    if (roles.All(r => r.Id != role))
                    {
                        errors.Add($"{where}: step '{key}' names role '{role}', which the flow does not declare.");
                    }
                }

                steps.Insert(index, step);
                own.Add(key);
            }

            foreach (JsonElement branch in TJson.Array(flow.Content, "branches"))
            {
                string name = TJson.Text(branch, "name")!;
                List<string> members = TJson.Strings(branch, "steps").ToList();
                string? returnTo = TJson.Text(branch, "returnTo");
                if (branches.Any(b => b.Name == name))
                {
                    errors.Add($"{where} declares branch '{name}' again.");
                }

                foreach (string member in members)
                {
                    if (!own.Contains(member, StringComparer.Ordinal))
                    {
                        errors.Add($"{where}: branch '{name}' names '{member}', which is not one of the steps it declares; a branch never takes in a base's step.");
                    }
                    else if (branches.Any(b => b.Steps.Contains(member, StringComparer.Ordinal)) || members.Count(m => m == member) > 1)
                    {
                        errors.Add($"{where}: step '{member}' belongs to more than one branch.");
                    }
                }

                if (returnTo is not null && (steps.All(s => s.Key != returnTo) || members.Contains(returnTo, StringComparer.Ordinal)))
                {
                    errors.Add($"{where}: branch '{name}' returns to '{returnTo}', which is not a step outside the branch.");
                }

                branches.Add(new FlowBranch(name, TJson.Array(branch, "when").ToList(), members, returnTo));
            }
        }

        // A failure route returns to a step before the failed one, or enters a branch the flow declares, whichever content declares it.
        foreach (FlowStep step in steps.Where(s => s.Failure is not null))
        {
            FlowFailure failure = step.Failure!;
            int at = failure.Step is null ? -1 : steps.FindIndex(s => s.Key == failure.Step);
            if (failure.Step is not null && (at < 0 || at >= steps.FindIndex(s => s.Key == step.Key)))
            {
                errors.Add($"Step '{step.Key}' returns the run to '{failure.Step}' when it fails, which is not a step before it.");
            }
            else if (failure.Step is not null && branches.FirstOrDefault(b => b.Steps.Contains(failure.Step, StringComparer.Ordinal)) is FlowBranch other
                && !other.Steps.Contains(step.Key, StringComparer.Ordinal))
            {
                errors.Add($"Step '{step.Key}' returns the run to '{failure.Step}' when it fails, a step of branch '{other.Name}', which runs only when it is entered.");
            }

            if (failure.Step is not null && at >= 0 && steps[at].Kind is "effect" or "exchange")
            {
                errors.Add($"Step '{step.Key}' returns the run to '{failure.Step}' when it fails, an {steps[at].Kind} step: a failure route never repeats an outside effect.");
            }

            if (failure.Branch is string entered && branches.All(b => b.Name != entered))
            {
                errors.Add($"Step '{step.Key}' enters branch '{entered}' when it fails, which the flow does not declare.");
            }
            else if (failure.Branch is string own && branches.First(b => b.Name == own).Steps.Contains(step.Key, StringComparer.Ordinal))
            {
                errors.Add($"Step '{step.Key}' enters its own branch '{own}' when it fails; a failure route enters another branch.");
            }
            else if (failure.Branch is string back && branches.First(b => b.Name == back).ReturnTo is string target && target != step.Key
                && steps.FindIndex(s => s.Key == target) > steps.FindIndex(s => s.Key == step.Key))
            {
                errors.Add($"Step '{step.Key}' enters branch '{back}' when it fails, which returns the run to '{target}', a step after it: a failure route returns to the failed step or an earlier one.");
            }
        }

        // An input from a step that runs over a list comes item by item, to a step that runs over the same list.
        foreach (FlowStep step in steps)
        {
            foreach (FlowInput input in step.Inputs.Where(i => i.Step is not null))
            {
                if (steps.FirstOrDefault(s => s.Key == input.Step) is { Each: string each } producer && step.Each != each)
                {
                    errors.Add($"Step '{step.Key}' takes input '{input.Name}' from '{producer.Key}', which runs once for each of the subject's {each}; a step taking it runs over the same list, item by item.");
                }
            }
        }

        // A required step never loses an input to a step or an item that is turned off.
        foreach (FlowStep step in steps.Where(s => s.Required))
        {
            foreach (FlowInput input in step.Inputs.Where(i => i.Step is not null && (turnedOff.ContainsKey(i.Step) || turnedOffItems.ContainsKey(i.Step + "." + i.Output))))
            {
                errors.Add($"Turning off '{(turnedOff.ContainsKey(input.Step!) ? input.Step : input.Step + "." + input.Output)}' leaves required step '{step.Key}' without its input '{input.Name}'.");
            }
        }

        // A reserved step done by the operator's close of its request never waits on a step done at no time the records give, in the
        // merged order an extension's steps take among its base's.
        HashSet<string> branched = branches.SelectMany(b => b.Steps).ToHashSet(StringComparer.Ordinal);
        List<FlowContract.ClosingShape> closing = steps.Select(s => new FlowContract.ClosingShape(s.Key, s.After,
            s.Inputs.Select(i => i.Step).Append(TJson.Text(TJson.Object(s.When, "due"), "from")).OfType<string>().ToList(),
            !s.Required || branched.Contains(s.Key), FlowContract.Untimed(s.Kind, s.Outputs.Count, s.Done), FlowContract.ClosesByRequest(s.Reserved, s.Done))).ToList();
        foreach ((string reserved, string waited) in FlowContract.Unclosable(closing))
        {
            errors.Add($"{FlowContract.UnclosableCode}: {FlowContract.UnclosableMessage(reserved, waited)}");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("The flow does not resolve: " + string.Join(" ", errors));
        }

        // An optional step a base declares and no extension turned off is configured by its own option, and so is each optional item.
        foreach (FlowStep step in steps.Where(s => !s.Required && !turnedOff.ContainsKey(s.Key)))
        {
            options[step.Option] = OnOff(step.Option, step.DefaultOn);
        }

        foreach (FlowStep step in steps.Where(s => !turnedOff.ContainsKey(s.Key)))
        {
            foreach (FlowOutput output in step.Outputs.Where(o => !o.Required && !turnedOffItems.ContainsKey(step.Key + "." + o.Name)))
            {
                options[step.ItemOption(output)] = OnOff(step.ItemOption(output), output.DefaultOn);
            }
        }

        return new FlowDefinition(definition, chain.Select(d => new TemplatePin(d.Id, d.Version, d.Hash)).ToList(), subject, roles, steps, branches, never, triggers, turnedOff, turnedOffItems, options);
    }

    private static JsonElement OnOff(string id, bool on)
    {
        using JsonDocument option = JsonDocument.Parse($$"""{"id":"{{id}}","values":["on","off"],"default":"{{(on ? "on" : "off")}}"}""");
        return option.RootElement.Clone();
    }

    /// <summary>What an extension's step may not do: reach a step it is not placed after, or bind a subject field its base does not have.</summary>
    private static IEnumerable<string> Extension(string where, FlowStep step, IReadOnlyList<string> earlier, IReadOnlyList<FlowStep> steps, Dictionary<string, string> fields)
    {
        foreach (string waited in step.After.Where(a => !earlier.Contains(a, StringComparer.Ordinal)))
        {
            yield return $"{where}: step '{step.Key}' waits on '{waited}', which is not a step before it.";
        }

        foreach (FlowInput input in step.Inputs)
        {
            if (input.Field is not null && !fields.ContainsKey(input.Field))
            {
                yield return $"{where}: step '{step.Key}' takes subject field '{input.Field}', which the flow's subject does not declare.";
            }

            FlowStep? producer = input.Step is null ? null : steps.FirstOrDefault(s => s.Key == input.Step);
            if (input.Step is not null && (producer is null || !earlier.Contains(input.Step, StringComparer.Ordinal)))
            {
                yield return $"{where}: step '{step.Key}' takes an input from '{input.Step}', which is not a step before it.";
            }
            else if (producer is not null && producer.Outputs.All(o => o.Name != input.Output))
            {
                yield return $"{where}: step '{input.Step}' declares no output '{input.Output}'.";
            }
            else if (producer is not null && step.Kind == "review" && producer.Kind != "work")
            {
                yield return $"{where}: review step '{step.Key}' reviews '{producer.Key}', which is not a work step.";
            }
        }

        if (step.Each is not null && fields.GetValueOrDefault(step.Each) != "list")
        {
            yield return $"{where}: step '{step.Key}' runs over '{step.Each}', which is not a list field of the flow's subject.";
        }

        if (TJson.Text(TJson.Object(step.When, "due"), "from") is string from && !earlier.Contains(from, StringComparer.Ordinal))
        {
            yield return $"{where}: step '{step.Key}' is due after '{from}', which is not a step before it.";
        }

        foreach (JsonElement predicate in step.Requires.Concat(step.Done).Concat(step.Traps.SelectMany(t => t.Holds)))
        {
            if (Bindings(predicate).Contains("previous", StringComparer.Ordinal) && !FlowContract.BindsPrevious(fields))
            {
                yield return $"{where}: step '{step.Key}' binds {{previous}}, and the flow's subject names no product and version.";
            }

            foreach (string binding in Bindings(predicate).Where(b => b.StartsWith("subject.", StringComparison.Ordinal)))
            {
                if (!fields.TryGetValue(binding["subject.".Length..], out string? type) || type == "list")
                {
                    yield return $"{where}: step '{step.Key}' binds {{{binding}}}, which is not a record or text field of the flow's subject.";
                }
            }
        }

        // A reserved step's command binds its base's subject as its predicates do.
        foreach (string binding in (step.Reserved is null ? [] : FlowContract.Placeholders(step.Action)).Where(b => b.StartsWith("subject.", StringComparison.Ordinal)))
        {
            if (!fields.TryGetValue(binding["subject.".Length..], out string? type) || type == "list")
            {
                yield return $"{where}: step '{step.Key}' binds {{{binding}}} in its command, which is not a record or text field of the flow's subject.";
            }
        }
    }

    /// <summary>The bindings a predicate's values name, without their braces.</summary>
    internal static IEnumerable<string> Bindings(JsonElement predicate)
    {
        foreach (string? value in new[] { TJson.Text(predicate, "subject"), TJson.Text(predicate, "product") }
            .Concat(TJson.Object(predicate, "fields") is { ValueKind: JsonValueKind.Object } fields ? fields.EnumerateObject().Select(f => f.Value.ValueKind == JsonValueKind.String ? f.Value.GetString() : null) : []))
        {
            if (value is not null && FlowContract.Binding().Match(value) is { Success: true } match)
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    private static FlowStep Read(JsonElement content, TemplatePin pin, string? previous)
    {
        List<string> after = TJson.Object(content, "after").ValueKind == JsonValueKind.Array ? TJson.Strings(content, "after").ToList() : previous is null ? [] : [previous];
        List<FlowTrap> traps = TJson.Array(content, "traps").Select(t => new FlowTrap(TJson.Text(t, "name")!, TJson.Text(t, "summary")!, TJson.Array(t, "holds").ToList(), TJson.Text(t, "check"))).ToList();
        List<FlowInput> inputs = TJson.Array(content, "inputs").Select(i => new FlowInput(TJson.Text(i, "name")!, TJson.Text(i, "from")!, TJson.Text(i, "field"), TJson.Text(i, "step"), TJson.Text(i, "output"))).ToList();
        List<FlowOutput> outputs = TJson.Array(content, "outputs").Select(o => new FlowOutput(TJson.Text(o, "name")!, TJson.Text(o, "kind")!, TJson.Text(o, "mediaType"), TJson.Text(o, "recordType"), TJson.Text(o, "recordKind"),
            !TJson.Flag(o, "optional"), TJson.Text(o, "default") != "off",
            TJson.Object(o, "provenance") is { ValueKind: JsonValueKind.Object } p
                ? new FlowProvenance(TJson.Text(p, "holder"), TJson.Text(p, "role"), TJson.Text(p, "principal"), TJson.Strings(p, "inputs").ToList(), TJson.Text(p, "provider"), TJson.Text(p, "tool"), TJson.Object(p, "settings") is { ValueKind: JsonValueKind.Object } settings ? settings.Clone() : default)
                : null,
            TJson.Array(o, "evidence").Select(e => new FlowEvidence(TJson.Text(e, "name")!, TJson.Text(e, "method")!, TJson.Text(e, "command"), TJson.Text(e, "role"))).ToList(),
            ReviewOf(TJson.Object(o, "review")), o.Clone())).ToList();
        return new FlowStep(TJson.Text(content, "key")!, TJson.Text(content, "title")!, TJson.Text(content, "kind")!, TJson.Text(content, "role")!,
            !TJson.Flag(content, "optional"), TJson.Text(content, "default") != "off", TJson.Text(content, "reserved"), after,
            TJson.Array(content, "requires").ToList(), TJson.Array(content, "done").ToList(), TJson.Text(content, "action"), TJson.Text(content, "recovery")!,
            traps, inputs, outputs, ReviewOf(TJson.Object(content, "review")), TJson.Text(content, "each"), TJson.Object(content, "when"), content.Clone(), pin,
            TJson.Object(content, "failure") is { ValueKind: JsonValueKind.Object } failure ? new FlowFailure(TJson.Text(failure, "route")!, TJson.Text(failure, "branch"), TJson.Text(failure, "step")) : null);
    }

    private static FlowReview? ReviewOf(JsonElement review) => review.ValueKind switch
    {
        JsonValueKind.String => new FlowReview(review.GetString()!, null),
        JsonValueKind.Object => new FlowReview(TJson.Text(review, "independence")!, TJson.Text(review, "role")),
        _ => null,
    };
}
