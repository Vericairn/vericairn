// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// A run of a flow (the estate's design/flow.md §§ 5, 6 and 12): a goal whose plan names a flow by its exact pin, the subject it binds,
/// the options it chooses and an optional step it ends at. Its configuration of optional steps is the narrowest of the flow's defaults,
/// the built template-defaults at the estate, the flow's domain and the run's product, and the goal's own options. No configuration
/// turns off a required step, a reserved marking or a step an estate policy requires, and configuring grants nothing.
/// </summary>
public static class FlowRuns
{
    /// <summary>The policy rule kind by which an estate policy requires named steps of a flow (design/flow.md § 5).</summary>
    public const string PolicyKind = "vericairn.estate/flow-steps";

    /// <summary>A setting of a run's configuration: its value and where it comes from.</summary>
    /// <param name="Option">The option: a flow option, or step.&lt;key&gt;.</param>
    /// <param name="Value">The effective value.</param>
    /// <param name="Source">Where the value comes from, in words.</param>
    /// <param name="Record">The template-defaults record or goal it comes from; null for the flow's default.</param>
    public sealed record Setting(string Option, string Value, string Source, RecordEnvelope? Record);

    /// <summary>A run's configuration: every option's effective value, and each step that is off with why.</summary>
    /// <param name="Values">Every option, by name.</param>
    /// <param name="Off">The steps that are not part of the run, by key, each with the reason.</param>
    /// <param name="Until">The step the run ends at, when the plan names one.</param>
    public sealed record Configuration(IReadOnlyDictionary<string, Setting> Values, IReadOnlyDictionary<string, string> Off, string? Until)
    {
        /// <summary>The value of a flow option, or null when the flow has none by that name.</summary>
        public string? Value(string option) => Values.TryGetValue(option, out Setting? setting) ? setting.Value : null;
    }

    /// <summary>A goal's plan.flow, or undefined when its plan names no flow.</summary>
    public static JsonElement Plan(RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return TJson.Object(TJson.Object(goal.Body, "plan"), "flow") is { ValueKind: JsonValueKind.Object } flow ? flow : default;
    }

    /// <summary>True when a goal is a run: its plan names a flow.</summary>
    public static bool IsRun(RecordEnvelope goal) => Plan(goal).ValueKind == JsonValueKind.Object;

    /// <summary>The exact flow pin a run names, or null.</summary>
    public static TemplatePin? Pin(RecordEnvelope goal)
    {
        JsonElement pin = TJson.Object(Plan(goal), "pin");
        return TJson.Text(pin, "id") is { } id && TJson.Text(pin, "version") is { } version && TJson.Text(pin, "hash") is { } hash ? new(id, version, hash) : null;
    }

    /// <summary>The run's one work order, at its latest revision: the work order its plan gives.</summary>
    public static RecordEnvelope? Work(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        string? id = TJson.Strings(TJson.Object(goal.Body, "plan"), "workOrders").FirstOrDefault();
        return id is not null && model.ById.TryGetValue(id, out RecordEnvelope? order) && order.Type == "work-order" ? model.Latest(order) : null;
    }

    /// <summary>Each loaded model's resolved flows, by exact pin: a model is one read of the records, and its flows are read once with it.</summary>
    private static readonly ConditionalWeakTable<EstateModel, Dictionary<TemplatePin, (FlowDefinition? Flow, string? Unavailable)>> Resolved = [];

    /// <summary>The flow a run names, resolved with its bases, or why it cannot be here; a reader without the content shows the run and runs none of it.</summary>
    public static (FlowDefinition? Flow, string? Unavailable) Resolve(EstateModel model, RecordEnvelope goal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (Pin(goal) is not TemplatePin pin)
        {
            return (null, "the goal's plan names no flow");
        }

        Dictionary<TemplatePin, (FlowDefinition? Flow, string? Unavailable)> flows = Resolved.GetValue(model, _ => []);
        lock (flows)
        {
            if (flows.TryGetValue(pin, out (FlowDefinition? Flow, string? Unavailable) known))
            {
                return known;
            }
        }

        (FlowDefinition? Flow, string? Unavailable) resolved;
        try
        {
            resolved = (FlowDefinition.Resolve(model, pin), null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            resolved = (null, $"flow {pin.Id} {pin.Version} at {pin.Hash} does not resolve here: {error.Message}");
        }

        lock (flows)
        {
            flows[pin] = resolved;
        }

        return resolved;
    }

    /// <summary>
    /// The product a run serves: the subject's product field when it binds one, else the product its work order is placed for or plans
    /// a release of. Null when it names none.
    /// </summary>
    public static string? Product(EstateModel model, RecordEnvelope goal, FlowDefinition? flow)
    {
        ArgumentNullException.ThrowIfNull(model);
        JsonElement subject = TJson.Object(Plan(goal), "subject");
        foreach ((string name, string type) in flow?.SubjectFields ?? new Dictionary<string, string>())
        {
            JsonElement bound = TJson.Object(subject, name);
            if (type == "record" && TJson.Text(bound, "type") == "product" && TJson.Text(bound, "id") is string id)
            {
                return LivingDocuments.ProductIdentity(model, id);
            }
        }

        RecordEnvelope? order = Work(model, goal);
        string? product = order is null ? null : TJson.Text(TJson.Object(order.Body, "placement"), "product") ?? TJson.Text(TJson.Object(order.Body, "releasePlan"), "product");
        return product is null ? null : LivingDocuments.ProductIdentity(model, product);
    }

    /// <summary>
    /// The steps an estate policy requires of a flow, by key, each with the policy: the active policies in force before a record when
    /// one is given (otherwise now), applying to the estate or to the run's product, whose rule of kind <see cref="PolicyKind"/> names the flow.
    /// </summary>
    public static IReadOnlyDictionary<string, RecordEnvelope> PolicyRequired(EstateModel model, string flowId, string? product, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var required = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        foreach (IGrouping<string, RecordEnvelope> lineage in model.OfType("estate-policy").Where(p => before is null || GoalRules.Before(p, before)).GroupBy(p => model.Chain(p)[0], StringComparer.Ordinal))
        {
            RecordEnvelope policy = before is null ? model.Latest(lineage.First()) : lineage.OrderBy(p => p.At, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).Last();
            if (EstateModel.BodyString(policy, "state") != "active")
            {
                continue;
            }

            JsonElement scope = TJson.Object(policy.Body, "appliesTo");
            string? kind = TJson.Text(scope, "kind");
            if (kind != "estate" && !(kind == "product" && product is not null && TJson.Text(scope, "product") is string named && LivingDocuments.ProductIdentity(model, named) == product))
            {
                continue;
            }

            foreach (JsonElement rule in TJson.Array(policy.Body, "rules").Where(r => TJson.Text(r, "kind") == PolicyKind))
            {
                JsonElement parameters = TJson.Object(rule, "parameters");
                if (TJson.Text(parameters, "flow") != flowId)
                {
                    continue;
                }

                foreach (string step in TJson.Strings(parameters, "steps"))
                {
                    required.TryAdd(step, policy);
                }
            }
        }

        return required;
    }

    /// <summary>
    /// A run's configuration from the records, before a record when one is given: each option from the flow's default, the estate's,
    /// the flow's domain's and the run's product's template-defaults for the exact flow pin, then the goal's plan, the narrowest
    /// winning; and the steps that are off: turned off by an extension, configured off, after the run's until step, or optional and
    /// fed by a step that is off.
    /// </summary>
    public static Configuration Configure(EstateModel model, RecordEnvelope goal, FlowDefinition flow, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(flow);
        JsonElement plan = Plan(goal);
        var values = flow.Options.ToDictionary(o => o.Key, o => new Setting(o.Key, TJson.Text(o.Value, "default")!, "the flow's default", null), StringComparer.Ordinal);
        string? product = Product(model, goal, flow);
        string? domain = TJson.Text(flow.Definition.Content, "domain");
        foreach ((string kind, string? target) in new[] { ("estate", (string?)null), ("domain", domain), ("product", product) })
        {
            if (kind != "estate" && target is null)
            {
                continue;
            }

            RecordEnvelope? defaults = model.Current("template-defaults").Where(r => before is null || GoalRules.Before(r, before)).Where(r =>
                TJson.Text(TJson.Object(r.Body, "template"), "id") == flow.Pin.Id && TJson.Text(TJson.Object(r.Body, "template"), "version") == flow.Pin.Version
                && TJson.Text(TJson.Object(r.Body, "template"), "hash") == flow.Pin.Hash && TJson.Text(TJson.Object(r.Body, "scope"), "kind") == kind
                && (target is null || ScopeTarget(model, kind, TJson.Text(TJson.Object(r.Body, "scope"), "target")) == target))
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            if (defaults is not null)
            {
                Apply(TJson.Object(defaults.Body, "options"), $"the {kind} defaults (template-defaults {defaults.Id})", defaults);
            }
        }

        Apply(TJson.Object(plan, "options"), "the goal's plan", goal);
        var off = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, TemplatePin by) in flow.TurnedOff)
        {
            off[key] = $"turned off by the flow's extension {by.Id} {by.Version}";
        }

        foreach (FlowStep step in flow.Steps.Where(s => !s.Required && !off.ContainsKey(s.Key)))
        {
            if (values.TryGetValue(step.Option, out Setting? setting) && setting.Value == "off")
            {
                off[step.Key] = setting.Record is null ? "off by the flow's default" : $"configured off by {setting.Source}";
            }
        }

        string? until = TJson.Text(plan, "until");
        int end = until is null ? -1 : flow.IndexOf(until);
        if (end >= 0)
        {
            foreach (FlowStep step in flow.Steps.Skip(end + 1))
            {
                off.TryAdd(step.Key, $"after {until}, the step the run ends at");
            }
        }

        // An optional step fed by a step that is off is off too, in the flow's order; a required one stays and waits, refused at validation.
        foreach (FlowStep step in flow.Steps.Where(s => !s.Required && !off.ContainsKey(s.Key)))
        {
            if (step.Inputs.FirstOrDefault(i => i.Step is not null && off.ContainsKey(i.Step)) is FlowInput fed)
            {
                off[step.Key] = $"its input {fed.Name} comes from {fed.Step}, which is off";
            }
        }

        return new Configuration(values, off, until);

        void Apply(JsonElement choices, string source, RecordEnvelope record)
        {
            if (choices.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty choice in choices.EnumerateObject())
            {
                if (choice.Value.ValueKind == JsonValueKind.String && flow.Options.TryGetValue(choice.Name, out JsonElement option)
                    && TJson.Strings(option, "values").Contains(choice.Value.GetString(), StringComparer.Ordinal))
                {
                    values[choice.Name] = new Setting(choice.Name, choice.Value.GetString()!, source, record);
                }
            }
        }
    }

    /// <summary>
    /// Why a configuration a record sets for a flow is refused (design/flow.md §§ 5 and 13): an option the flow does not declare or a
    /// value it does not allow; a required step, a step an extension turned off, or a step an estate policy requires turned off; or a
    /// required or policy-required step left without an input by a step it turns off.
    /// </summary>
    internal static IReadOnlyList<(string Code, string Message)> ConfigurationRefusals(EstateModel model, FlowDefinition flow, JsonElement choices, string? product, RecordEnvelope? before)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);
        var refusals = new List<(string Code, string Message)>();
        if (choices.ValueKind != JsonValueKind.Object)
        {
            return refusals;
        }

        IReadOnlyDictionary<string, RecordEnvelope> required = PolicyRequired(model, flow.Pin.Id, product, before);
        var turnedOff = new List<string>();
        foreach (JsonProperty choice in choices.EnumerateObject())
        {
            string? value = choice.Value.ValueKind == JsonValueKind.String ? choice.Value.GetString() : null;
            FlowStep? step = choice.Name.StartsWith(FlowContract.StepOption, StringComparison.Ordinal) ? flow.Step(choice.Name[FlowContract.StepOption.Length..]) : null;
            if (step is { Required: true })
            {
                refusals.Add(("flow.required", $"Step {step.Key} is required{(step.Reserved is null ? string.Empty : $", reserved as {step.Reserved}")}: configuration turns off only an optional step, and never a reserved marking."));
            }
            else if (step is not null && flow.TurnedOff.ContainsKey(step.Key))
            {
                refusals.Add(("flow.option", $"Step {step.Key} is turned off by the flow's extension, which configuration does not change."));
            }
            else if (!flow.Options.TryGetValue(choice.Name, out JsonElement option) || value is null || !TJson.Strings(option, "values").Contains(value, StringComparer.Ordinal))
            {
                refusals.Add(("flow.option", $"'{choice.Name}' is not an option of flow {flow.Pin.Id} with that value; its options are {string.Join(", ", flow.Options.Keys.Order(StringComparer.Ordinal))}."));
            }
            else if (step is not null && value == "off")
            {
                if (required.TryGetValue(step.Key, out RecordEnvelope? policy))
                {
                    refusals.Add(("flow.policy", $"Step {step.Key} is required by estate policy {EstateModel.BodyString(policy, "key") ?? policy.Id} ({policy.Id}); no configuration turns it off."));
                }
                else
                {
                    turnedOff.Add(step.Key);
                }
            }
        }

        foreach (FlowStep fed in flow.Steps.Where(s => s.Required || required.ContainsKey(s.Key)))
        {
            foreach (FlowInput input in fed.Inputs.Where(i => i.Step is not null && turnedOff.Contains(i.Step)))
            {
                refusals.Add(("flow.required", $"Turning off {input.Step} leaves {(fed.Required ? "required" : "policy-required")} step {fed.Key} without its input {input.Name}."));
            }
        }

        return refusals;
    }

    /// <summary>
    /// Validates a goal's plan.flow (design/flow.md § 12): the exact pin, the subject the flow declares, the options it allows, an until
    /// step that runs in this configuration, and the run's one ordinary work order; with the flow at hand, every configuration refusal
    /// and no agent named in a role the flow marks person. A reader without the flow's content records a warning: it shows the run and
    /// runs none of it.
    /// </summary>
    internal static void Validate(ValidationContext context, BodyReader flow, string? policy, IReadOnlyList<string>? orders)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(flow);
        BodyReader? pin = flow.Nested("pin", true);
        if (pin is not null)
        {
            TemplateRecords.ReadPin(pin);
            pin.NoOtherKeys();
        }

        BodyReader? subject = flow.Nested("subject", true);
        BodyReader? options = flow.Nested("options", false);
        if (options is not null)
        {
            foreach (JsonProperty option in options.Element.EnumerateObject())
            {
                options.Text(option.Name, true, 300);
            }
        }

        string? until = flow.Text("until", false, 100, Patterns.Slug, "a step key");
        flow.NoOtherKeys();
        if (policy != "given" || orders is not { Count: 1 })
        {
            context.Error("flow.work", "A run's work is one ordinary work order its sponsor gives: plan.policy given, with exactly one work order; each step is an assignment of it.");
        }
        else if (context.Find(orders[0]) is { Type: "work-order" } order && EstateModel.BodyString(context.Model.Latest(order), "kind") != "ordinary")
        {
            context.Error("flow.work", "A run's work order is ordinary; its reserved steps are the operator's, never a milestone's acceptance by delegation.");
        }

        if (pin is null || subject is null)
        {
            return;
        }

        (FlowDefinition? resolved, string? unavailable) = Resolve(context.Model, context.Record);
        if (resolved is null)
        {
            context.Warning("flow.unavailable", $"The run's {unavailable}. The goal is shown, and none of its steps is run on this machine.");
            return;
        }

        foreach (JsonProperty bound in subject.Element.EnumerateObject())
        {
            subject.Allow(bound.Name);
            if (!resolved.SubjectFields.ContainsKey(bound.Name))
            {
                subject.Error("flow.subject", bound.Name, $"Flow {resolved.Pin.Id}'s subject has no field '{bound.Name}'.");
            }
        }

        foreach ((string name, string type) in resolved.SubjectFields)
        {
            Bind(context, subject, name, type, TJson.Text(TJson.Array(resolved.Subject, "fields").First(f => TJson.Text(f, "name") == name), "recordType"));
        }

        string? product = Product(context.Model, context.Record, resolved);
        foreach ((string code, string message) in ConfigurationRefusals(context.Model, resolved, options?.Element ?? default, product, context.Record))
        {
            context.Error(code, message);
        }

        IReadOnlyDictionary<string, RecordEnvelope> required = PolicyRequired(context.Model, resolved.Pin.Id, product, context.Record);
        Configuration configuration = Configure(context.Model, context.Record, resolved, context.Record);
        if (until is not null)
        {
            if (resolved.Step(until) is null)
            {
                context.Error("flow.until", $"The run ends at '{until}', which is not a step of flow {resolved.Pin.Id}.");
            }
            else if (configuration.Off.TryGetValue(until, out string? why) && !why.StartsWith("after ", StringComparison.Ordinal))
            {
                context.Error("flow.until", $"The run ends at {until}, which is not part of it: {why}.");
            }
            else
            {
                foreach (FlowStep after in resolved.Steps.Skip(resolved.IndexOf(until) + 1).Where(s => required.ContainsKey(s.Key)))
                {
                    context.Error("flow.policy", $"Ending the run at {until} leaves out {after.Key}, which estate policy {EstateModel.BodyString(required[after.Key], "key") ?? required[after.Key].Id} requires.");
                }
            }
        }

        foreach (JsonElement role in TJson.Array(context.Record.Body, "roles"))
        {
            if (resolved.Role(TJson.Text(role, "role")) is { Holder: "person" } person)
            {
                foreach (string named in TJson.Strings(role, "principals").Where(p => context.Principal(p) is { } principal && EstateModel.BodyString(principal, "kind") != "human"))
                {
                    context.Error("flow.holder", $"Role {person.Id} of flow {resolved.Pin.Id} is a person's: {named} is not a person, and no goal or grant lets an agent hold it.");
                }
            }
        }
    }

    /// <summary>Validates one subject binding against the field the flow declares: a record reference of its type, a text value, or a list of item names.</summary>
    private static void Bind(ValidationContext context, BodyReader subject, string name, string type, string? recordType)
    {
        if (!subject.Has(name))
        {
            subject.Error("flow.subject", name, $"The run binds every field of its flow's subject; '{name}' is missing.");
            return;
        }

        if (type == "record")
        {
            BodyReader? reference = subject.Nested(name, true);
            if (reference is null)
            {
                return;
            }

            string? named = reference.Text("type", true, 100);
            string? id = reference.Ulid("id", true);
            string? hash = reference.Hash("hash", false);
            reference.NoOtherKeys();
            if (named != recordType)
            {
                reference.Error("flow.subject", "type", $"Subject field '{name}' is a {recordType} record.");
            }
            else if (context.RequireReference($"plan.flow.subject.{name}", id, recordType!) is RecordEnvelope target && hash is not null && target.ComputedHash != hash)
            {
                reference.Error("flow.subject", "hash", "The pinned hash does not match the record.");
            }
        }
        else if (type == "text")
        {
            subject.Text(name, true, 2000);
        }
        else
        {
            IReadOnlyList<string>? items = subject.StringArray(name, true, allowEmpty: true, maxItemLength: 100, itemPattern: Patterns.Slug, patternHint: "an item name");
            if (items is not null && items.Distinct(StringComparer.Ordinal).Count() != items.Count)
            {
                subject.Error("flow.subject", name, "Each item of a list field is named once.");
            }
        }
    }

    /// <summary>
    /// The values a run binds: the subject's record fields by their ids, its text fields, and the goal and its work order; an each
    /// step's instance adds its item.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Bindings(EstateModel model, RecordEnvelope goal, FlowDefinition flow, string? item)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal) { ["goal"] = model.Chain(goal)[0] };
        if (Work(model, goal) is RecordEnvelope order)
        {
            bindings["work"] = model.Chain(order)[0];
        }

        if (item is not null)
        {
            bindings["item"] = item;
        }

        JsonElement subject = TJson.Object(Plan(goal), "subject");
        foreach ((string name, string type) in flow.SubjectFields)
        {
            JsonElement value = TJson.Object(subject, name);
            string? bound = type == "record" ? TJson.Text(value, "id") : type == "text" && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (bound is not null)
            {
                bindings["subject." + name] = bound;
            }
        }

        return bindings;
    }

    /// <summary>The items a run's subject list field names, in order.</summary>
    internal static IReadOnlyList<string> Items(RecordEnvelope goal, string field) =>
        TJson.Strings(TJson.Object(Plan(goal), "subject"), field).ToList();

    /// <summary>A predicate with the run's bindings put in; a binding the run does not have leaves a value no record holds.</summary>
    internal static JsonElement Bind(JsonElement predicate, IReadOnlyDictionary<string, string> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        JsonNode node = JsonNode.Parse(predicate.GetRawText())!;
        foreach (string name in new[] { "subject", "product" })
        {
            if (node[name] is JsonValue value && value.TryGetValue(out string? text))
            {
                node[name] = Value(text);
            }
        }

        if (node["fields"] is JsonObject fields)
        {
            foreach (string name in fields.Select(f => f.Key).ToList())
            {
                if (fields[name] is JsonValue value && value.TryGetValue(out string? text))
                {
                    fields[name] = Value(text);
                }
            }
        }

        return JsonSerializer.SerializeToElement(node);

        string Value(string text) =>
            FlowContract.Binding().Match(text) is { Success: true } match ? bindings.GetValueOrDefault(match.Groups["name"].Value) ?? "{unbound " + match.Groups["name"].Value + "}" : text;
    }

    /// <summary>A step instance an assignment names: the step's key, and its item for a step that runs over a list.</summary>
    /// <param name="Key">The step's key.</param>
    /// <param name="Item">The item, or null.</param>
    public sealed record StepRef(string Key, string? Item)
    {
        /// <summary>The instance in words: key, or key[item].</summary>
        public override string ToString() => Item is null ? Key : $"{Key}[{Item}]";
    }

    /// <summary>The step an assignment names, or null for an assignment of ordinary work.</summary>
    public static StepRef? StepOf(RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        JsonElement step = TJson.Object(assignment.Body, "step");
        return TJson.Text(step, "key") is string key ? new StepRef(key, TJson.Text(step, "item")) : null;
    }

    /// <summary>The run a work order is the work of, as of a record when one is given; null when it is the work of none.</summary>
    public static RecordEnvelope? RunOf(EstateModel model, RecordEnvelope order, RecordEnvelope? before = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        return Routing.GoalsOf(model, order, before).FirstOrDefault(IsRun);
    }

    /// <summary>
    /// Validates the step an assignment names (design/flow.md § 6): every assignment of a run's work order names a work or review step
    /// of the run's flow, with its item when the step runs over a list, and pins only inputs the step declares; no other assignment
    /// names a step.
    /// </summary>
    internal static void ValidateStep(ValidationContext context, RecordEnvelope order, BodyReader? step)
    {
        RecordEnvelope? run = RunOf(context.Model, order, context.Record);
        string key = EstateModel.BodyString(context.Model.Latest(order), "key") ?? order.Id;
        if (run is null)
        {
            if (step is not null)
            {
                context.Error("assignment.step", $"A step names a step of a run's flow, and work order {key} is the work of no run.");
            }

            return;
        }

        if (step is null)
        {
            context.Error("assignment.step", $"Work order {key} is the work of run {run.Id}: each assignment of it names the flow step it carries (goal step routes one, work takeover hands one over).");
            return;
        }

        (FlowDefinition? flow, string? unavailable) = Resolve(context.Model, run);
        if (flow is null)
        {
            context.Warning("flow.unavailable", $"The run's {unavailable}; the step this assignment names cannot be checked here.");
            return;
        }

        string? named = TJson.Text(step.Element, "key");
        string? item = TJson.Text(step.Element, "item");
        FlowStep? declared = flow.Step(named);
        if (declared is null)
        {
            context.Error("assignment.step", $"Flow {flow.Pin.Id} has no step '{named}'.");
            return;
        }

        if (!declared.IsRouted)
        {
            context.Error("assignment.step", $"Step {declared.Key} is a {declared.Kind} step; an assignment carries a work or review step.");
        }

        // A reserved act and a person's role are never an agent's (design/flow.md §§ 8 and 13).
        string? contributor = EstateModel.BodyString(context.Record, "contributor");
        if (contributor is not null && context.Principal(contributor) is RecordEnvelope holder && EstateModel.BodyString(holder, "kind") != "human")
        {
            if (declared.Reserved is not null)
            {
                context.Error("assignment.step", $"Step {declared.Key} is a reserved act, {declared.Reserved}: it is assigned only to a person who holds it, never to an agent.");
            }

            if (flow.Role(declared.Role)?.Holder == "person")
            {
                context.Error("assignment.step", $"Step {declared.Key} is carried in the {declared.Role} role, which the flow marks a person's: it is never assigned to an agent.");
            }
        }

        if (declared.Each is not null && (item is null || !Items(run, declared.Each).Contains(item, StringComparer.Ordinal)))
        {
            context.Error("assignment.step", $"Step {declared.Key} runs once for each of the run's {declared.Each}; the assignment names one of them as its item.");
        }
        else if (declared.Each is null && item is not null)
        {
            context.Error("assignment.step", $"Step {declared.Key} runs once; its assignment names no item.");
        }

        foreach (JsonElement input in TJson.Array(step.Element, "inputs"))
        {
            if (declared.Inputs.All(i => i.Name != TJson.Text(input, "name")))
            {
                context.Error("assignment.step", $"Step {declared.Key} declares no input '{TJson.Text(input, "name")}'.");
            }
        }
    }

    private static string? ScopeTarget(EstateModel model, string kind, string? target) =>
        kind == "product" && target is not null ? LivingDocuments.ProductIdentity(model, target) : target;
}
