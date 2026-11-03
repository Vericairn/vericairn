// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Templates;

/// <summary>A role of a flow: who may hold it and its role brief, a living document a step's brief carries.</summary>
/// <param name="Id">The role's name, by which the roster routes its steps.</param>
/// <param name="Holder">person, agent or either: who may hold it.</param>
/// <param name="Brief">The role brief.</param>
public sealed record FlowRole(string Id, string Holder, string Brief);

/// <summary>An input of a step: a field of the run's subject, or a named output of an earlier step, which the step's brief pins by hash.</summary>
/// <param name="Name">The input's name within the step.</param>
/// <param name="From">subject or step.</param>
/// <param name="Field">For a subject input, the subject field.</param>
/// <param name="Step">For a step input, the producing step's key.</param>
/// <param name="Output">For a step input, the producing step's output name.</param>
public sealed record FlowInput(string Name, string From, string? Field, string? Step, string? Output);

/// <summary>A named, typed output of a step: its part of the hand-off contract (design/flow.md § 5).</summary>
/// <param name="Name">The output's name.</param>
/// <param name="Type">file, record, decision or review.</param>
/// <param name="MediaType">For a file, its declared media type.</param>
/// <param name="RecordType">For a record output, its record type.</param>
/// <param name="Kind">For a record output, the value of the record's kind field it must have.</param>
public sealed record FlowOutput(string Name, string Type, string? MediaType, string? RecordType, string? Kind)
{
    /// <summary>The record type a record or decision output is counted from by the step's done predicates; null for a file or a review.</summary>
    public string? Counts => Type == "decision" ? "decision" : Type == "record" ? RecordType : null;
}

/// <summary>A check a step runs before its action, named and explained: here, record predicates that must hold.</summary>
/// <param name="Name">The trap's name.</param>
/// <param name="Summary">What it guards against.</param>
/// <param name="Holds">The predicates that hold when the trap is clear.</param>
public sealed record FlowTrap(string Name, string Summary, IReadOnlyList<JsonElement> Holds);

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
/// <param name="Review">The review its output takes before hand-off, as declared; null when the flow's review option decides.</param>
/// <param name="Each">The subject list field it runs over, one instance per item; null for one instance.</param>
/// <param name="When">Its timing, a due time or a watch; undefined without one.</param>
/// <param name="Element">The step's declared content.</param>
/// <param name="DeclaredIn">The flow version that declares it.</param>
public sealed record FlowStep(string Key, string Title, string Kind, string Role, bool Required, bool DefaultOn, string? Reserved, IReadOnlyList<string> After,
    IReadOnlyList<JsonElement> Requires, IReadOnlyList<JsonElement> Done, string? Action, string Recovery, IReadOnlyList<FlowTrap> Traps,
    IReadOnlyList<FlowInput> Inputs, IReadOnlyList<FlowOutput> Outputs, string? Review, string? Each, JsonElement When, JsonElement Element, TemplatePin DeclaredIn)
{
    /// <summary>True for the kinds the conductor routes as an assignment of the run's work order: work and review.</summary>
    public bool IsRouted => FlowContract.Routed.IndexOf(Kind) >= 0;

    /// <summary>The option that configures an optional step: step.&lt;key&gt;.</summary>
    public string Option => FlowContract.StepOption + Key;
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
        IReadOnlyList<FlowBranch> branches, IReadOnlyList<string> never, IReadOnlyList<JsonElement> triggers, IReadOnlyDictionary<string, TemplatePin> turnedOff, IReadOnlyDictionary<string, JsonElement> options)
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

                roles.Add(new FlowRole(id, TJson.Text(role, "holder")!, TJson.Text(role, "brief")!));
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

            foreach (string key in TJson.Strings(flow.Content, "off"))
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
                    errors.Add($"{where} declares step '{key}' again: an extension adds steps, and never redeclares a base step, its presence or its reserved marking.");
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

                if (roles.All(r => r.Id != step.Role))
                {
                    errors.Add($"{where}: step '{key}' names role '{step.Role}', which the flow does not declare.");
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

        // A required step never loses an input to a step that is turned off.
        foreach (FlowStep step in steps.Where(s => s.Required))
        {
            foreach (FlowInput input in step.Inputs.Where(i => i.Step is not null && turnedOff.ContainsKey(i.Step)))
            {
                errors.Add($"Turning off '{input.Step}' leaves required step '{step.Key}' without its input '{input.Name}'.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("The flow does not resolve: " + string.Join(" ", errors));
        }

        // An optional step a base declares and no extension turned off is configured by its own option.
        foreach (FlowStep step in steps.Where(s => !s.Required && !turnedOff.ContainsKey(s.Key)))
        {
            using JsonDocument option = JsonDocument.Parse($$"""{"id":"{{step.Option}}","values":["on","off"],"default":"{{(step.DefaultOn ? "on" : "off")}}"}""");
            options[step.Option] = option.RootElement.Clone();
        }

        return new FlowDefinition(definition, chain.Select(d => new TemplatePin(d.Id, d.Version, d.Hash)).ToList(), subject, roles, steps, branches, never, triggers, turnedOff, options);
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
            foreach (string binding in Bindings(predicate).Where(b => b.StartsWith("subject.", StringComparison.Ordinal)))
            {
                if (!fields.TryGetValue(binding["subject.".Length..], out string? type) || type == "list")
                {
                    yield return $"{where}: step '{step.Key}' binds {{{binding}}}, which is not a record or text field of the flow's subject.";
                }
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
        List<FlowTrap> traps = TJson.Array(content, "traps").Select(t => new FlowTrap(TJson.Text(t, "name")!, TJson.Text(t, "summary")!, TJson.Array(t, "holds").ToList())).ToList();
        List<FlowInput> inputs = TJson.Array(content, "inputs").Select(i => new FlowInput(TJson.Text(i, "name")!, TJson.Text(i, "from")!, TJson.Text(i, "field"), TJson.Text(i, "step"), TJson.Text(i, "output"))).ToList();
        List<FlowOutput> outputs = TJson.Array(content, "outputs").Select(o => new FlowOutput(TJson.Text(o, "name")!, TJson.Text(o, "type")!, TJson.Text(o, "mediaType"), TJson.Text(o, "recordType"), TJson.Text(o, "kind"))).ToList();
        return new FlowStep(TJson.Text(content, "key")!, TJson.Text(content, "title")!, TJson.Text(content, "kind")!, TJson.Text(content, "role")!,
            TJson.Text(content, "presence") == "required", TJson.Text(content, "default") != "off", TJson.Text(content, "reserved"), after,
            TJson.Array(content, "requires").ToList(), TJson.Array(content, "done").ToList(), TJson.Text(content, "action"), TJson.Text(content, "recovery")!,
            traps, inputs, outputs, TJson.Text(content, "review"), TJson.Text(content, "each"), TJson.Object(content, "when"), content.Clone(), pin);
    }
}
