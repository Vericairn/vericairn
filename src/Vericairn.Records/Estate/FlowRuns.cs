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

    private static readonly string[] OnOffValues = ["on", "off"];

    /// <summary>A setting of a run's configuration: its value and where it comes from.</summary>
    /// <param name="Option">The option: a flow option, or step.&lt;key&gt;.</param>
    /// <param name="Value">The effective value.</param>
    /// <param name="Source">Where the value comes from, in words.</param>
    /// <param name="Record">The template-defaults record or goal it comes from; null for the flow's default.</param>
    public sealed record Setting(string Option, string Value, string Source, RecordEnvelope? Record);

    /// <summary>A run's configuration: every option's effective value, and each step and item that is off with why.</summary>
    /// <param name="Values">Every option, by name.</param>
    /// <param name="Off">The steps that are not part of the run, by key, each with the reason.</param>
    /// <param name="ItemsOff">The optional items that are off, by step.item, each with the reason.</param>
    /// <param name="Until">The step the run ends at, when the plan names one.</param>
    public sealed record Configuration(IReadOnlyDictionary<string, Setting> Values, IReadOnlyDictionary<string, string> Off, IReadOnlyDictionary<string, string> ItemsOff, string? Until)
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
    /// The steps and items an estate policy requires of a flow, by step key or step.item, each with the policy: the active policies in
    /// force before a record when one is given (otherwise now), applying to the estate or to the run's product, whose rule of kind
    /// <see cref="PolicyKind"/> names the flow.
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

                foreach (string step in TJson.Strings(parameters, "steps").Concat(TJson.Strings(parameters, "items")))
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

            RecordEnvelope? defaults = model.OfType("template-defaults").Where(r => CurrentBefore(model, r, before)).Where(r =>
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

        // An optional item turned off by an extension or configured off counts for nothing; only an optional item has an option.
        var itemsOff = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, TemplatePin by) in flow.TurnedOffItems)
        {
            itemsOff[key] = $"turned off by the flow's extension {by.Id} {by.Version}";
        }

        foreach (FlowStep step in flow.Steps)
        {
            foreach (FlowOutput output in step.Outputs.Where(o => !o.Required))
            {
                if (!itemsOff.ContainsKey(step.Key + "." + output.Name) && values.TryGetValue(step.ItemOption(output), out Setting? setting) && setting.Value == "off")
                {
                    itemsOff[step.Key + "." + output.Name] = setting.Record is null ? "off by the flow's default" : $"configured off by {setting.Source}";
                }
            }
        }

        // An optional step fed by a step or an item that is off is off too, in the flow's order; a required one stays and waits,
        // refused at validation.
        foreach (FlowStep step in flow.Steps.Where(s => !s.Required && !off.ContainsKey(s.Key)))
        {
            if (step.Inputs.FirstOrDefault(i => i.Step is not null && (off.ContainsKey(i.Step) || itemsOff.ContainsKey(i.Step + "." + i.Output))) is FlowInput fed)
            {
                off[step.Key] = off.ContainsKey(fed.Step!) ? $"its input {fed.Name} comes from {fed.Step}, which is off" : $"its input {fed.Name} is item {fed.Output} of {fed.Step}, which is off";
            }
        }

        return new Configuration(values, off, itemsOff, until);

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
        var itemsOff = new List<string>();
        foreach (JsonProperty choice in choices.EnumerateObject())
        {
            string? value = choice.Value.ValueKind == JsonValueKind.String ? choice.Value.GetString() : null;
            if (choice.Name.StartsWith(FlowContract.ItemOption, StringComparison.Ordinal))
            {
                string[] parts = choice.Name[FlowContract.ItemOption.Length..].Split('.');
                FlowStep? owner = parts.Length == 2 ? flow.Step(parts[0]) : null;
                FlowOutput? item = owner?.Outputs.FirstOrDefault(o => o.Name == parts[1]);
                if (item is { Required: true })
                {
                    refusals.Add(("flow.required", $"Item {item.Name} of step {owner!.Key} is required: configuration turns off only an optional item."));
                }
                else if (item is not null && flow.TurnedOffItems.ContainsKey(owner!.Key + "." + item.Name))
                {
                    refusals.Add(("flow.option", $"Item {item.Name} of step {owner.Key} is turned off by the flow's extension, which configuration does not change."));
                }
                else if (item is null || value is null || !flow.Options.ContainsKey(choice.Name) || !OnOffValues.Contains(value))
                {
                    refusals.Add(("flow.option", $"'{choice.Name}' is not an option of flow {flow.Pin.Id} with that value; its options are {string.Join(", ", flow.Options.Keys.Order(StringComparer.Ordinal))}."));
                }
                else if (value == "off" && required.TryGetValue(owner!.Key + "." + item.Name, out RecordEnvelope? requiring))
                {
                    refusals.Add(("flow.policy", $"Item {item.Name} of step {owner.Key} is required by estate policy {EstateModel.BodyString(requiring, "key") ?? requiring.Id} ({requiring.Id}); no configuration turns it off."));
                }
                else if (value == "off")
                {
                    itemsOff.Add(owner!.Key + "." + item.Name);
                }

                continue;
            }

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
            foreach (FlowInput input in fed.Inputs.Where(i => i.Step is not null && (turnedOff.Contains(i.Step) || itemsOff.Contains(i.Step + "." + i.Output))))
            {
                string what = turnedOff.Contains(input.Step!) ? input.Step! : $"item {input.Output} of {input.Step}";
                refusals.Add(("flow.required", $"Turning off {what} leaves {(fed.Required ? "required" : "policy-required")} step {fed.Key} without its input {input.Name}."));
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

        // The run's effective configuration, not only the goal's own options, carries every step a policy requires: a wider default,
        // the flow's own default or an extension that leaves one off needs the goal to turn it on, or another flow.
        foreach ((string key, RecordEnvelope requiring) in required)
        {
            bool item = key.Contains('.', StringComparison.Ordinal);
            string option = (item ? FlowContract.ItemOption : FlowContract.StepOption) + key;
            if ((item ? configuration.ItemsOff : configuration.Off).TryGetValue(key, out string? why) && !why.StartsWith("after ", StringComparison.Ordinal)
                && !(options?.Element is { ValueKind: JsonValueKind.Object } own && own.TryGetProperty(option, out JsonElement chosen) && chosen.ValueKind == JsonValueKind.String && chosen.GetString() == "off"))
            {
                context.Error("flow.policy", $"{(item ? "Item" : "Step")} {key} is required by estate policy {EstateModel.BodyString(requiring, "key") ?? requiring.Id} ({requiring.Id}), and this run leaves it off: {why}. "
                    + (why.StartsWith("turned off by", StringComparison.Ordinal) ? "A run of a flow that carries the step is needed." : "The goal's options turn it on."));
            }
        }
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
                foreach (FlowStep after in resolved.Steps.Skip(resolved.IndexOf(until) + 1))
                {
                    string? key = required.ContainsKey(after.Key) ? after.Key : required.Keys.FirstOrDefault(k => k.StartsWith(after.Key + ".", StringComparison.Ordinal));
                    if (key is not null)
                    {
                        context.Error("flow.policy", $"Ending the run at {until} leaves out {after.Key}, whose {(key == after.Key ? "step" : "item " + key[(after.Key.Length + 1)..])} estate policy {EstateModel.BodyString(required[key], "key") ?? required[key].Id} requires.");
                    }
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

    /// <summary>Validates one subject binding against the field the flow declares: a record reference of its type, a text value, or a list of instance names.</summary>
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
            IReadOnlyList<string>? names = subject.StringArray(name, true, allowEmpty: true, maxItemLength: 100, itemPattern: Patterns.Slug, patternHint: "an instance name");
            if (names is not null && names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            {
                subject.Error("flow.subject", name, "Each instance of a list field is named once.");
            }
        }
    }

    /// <summary>
    /// The values a run binds: the subject's record fields by their ids, its text fields, and the goal and its work order; an each
    /// step's instance adds its instance name.
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
            bindings["instance"] = item;
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

    /// <summary>The instances a run's subject list field names, in order.</summary>
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

    /// <summary>
    /// A step instance an assignment names: the step's key, its instance for a step that runs over a list, and whether the assignment
    /// carries the step's review before hand-off rather than the step itself (design/flow.md § 6).
    /// </summary>
    /// <param name="Key">The step's key.</param>
    /// <param name="Instance">The instance under each, or null.</param>
    /// <param name="Review">True for an assignment of the step's review before hand-off, whose contributor reviews and never authors it.</param>
    public sealed record StepRef(string Key, string? Instance, bool Review = false)
    {
        /// <summary>The step instance itself, without the review marking.</summary>
        public StepRef Work => Review ? this with { Review = false } : this;

        /// <summary>The step instance in words: key, or key[instance], and "review" for its review.</summary>
        public override string ToString() => (Instance is null ? Key : $"{Key}[{Instance}]") + (Review ? " review" : string.Empty);
    }

    /// <summary>A review before hand-off an assignment carries: the items it reviews (none for the step's result) and the exact subject.</summary>
    /// <param name="Items">The items it reviews; empty for a step that declares none, whose result it reviews.</param>
    /// <param name="Subject">The exact record that carries them.</param>
    public sealed record ReviewRef(IReadOnlyList<string> Items, RecordReference Subject);

    /// <summary>The step an assignment names, or null for an assignment of ordinary work.</summary>
    public static StepRef? StepOf(RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        JsonElement step = TJson.Object(assignment.Body, "step");
        return TJson.Text(step, "key") is string key ? new StepRef(key, TJson.Text(step, "instance"), TJson.Object(step, "review").ValueKind == JsonValueKind.Object) : null;
    }

    /// <summary>The review before hand-off an assignment carries, or null when it carries none.</summary>
    public static ReviewRef? ReviewOf(RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        JsonElement review = TJson.Object(TJson.Object(assignment.Body, "step"), "review");
        JsonElement subject = TJson.Object(review, "subject");
        return review.ValueKind == JsonValueKind.Object && TJson.Text(subject, "type") is string type && TJson.Text(subject, "id") is string id
            ? new ReviewRef(TJson.Strings(review, "items").ToList(), new RecordReference(type, id, TJson.Text(subject, "hash")))
            : null;
    }

    /// <summary>
    /// The review an item takes before its step closes (design/flow.md §§ 5 and 6): its own; else its step's; else, for a work step's item
    /// that feeds a reserved step or leaves the estate, an independent review; else, for a work step, the flow's review option; else none. A
    /// configuration sets only the flow's option, so it never lowers the review an item that feeds a reserved step or leaves the estate
    /// takes by default. A step that declares no item takes its own review, or the flow's option, on its result.
    /// </summary>
    public static FlowReview ReviewRequirement(FlowDefinition flow, Configuration configuration, FlowStep step, FlowOutput? output)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(step);
        if ((output?.Review ?? step.Review) is FlowReview declared)
        {
            return declared;
        }

        if (output is not null && step.Kind == "work" && Consequential(flow, step, output) is not null)
        {
            return new FlowReview("independent", null);
        }

        return step.Kind == "work" && configuration.Value("review") is string option && FlowContract.Reviews.Contains(option) ? new FlowReview(option, null) : new FlowReview("none", null);
    }

    /// <summary>
    /// Why an item's review is independent by default (design/flow.md § 17, recommendation 2): the first step it reaches, through the
    /// steps that take it and theirs, that is a reserved act or leaves the estate (an effect or an exchange); null when it reaches none.
    /// </summary>
    public static string? Consequential(FlowDefinition flow, FlowStep step, FlowOutput output)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(output);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Step, string Output)>([(step.Key, output.Name)]);
        while (pending.Count > 0)
        {
            (string producer, string name) = pending.Dequeue();
            foreach (FlowStep consumer in flow.Steps.Where(s => s.Inputs.Any(i => i.Step == producer && i.Output == name)))
            {
                if (consumer.Reserved is string act)
                {
                    return $"it feeds step {consumer.Key}, a reserved act ({act})";
                }

                if (consumer.Kind is "effect" or "exchange")
                {
                    return $"it leaves the estate through step {consumer.Key}, an {consumer.Kind}";
                }

                if (seen.Add(consumer.Key))
                {
                    foreach (FlowOutput next in consumer.Outputs)
                    {
                        pending.Enqueue((consumer.Key, next.Name));
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The authors of a step instance's work (design/goals.md § 3): the contributor of each assignment that carried it, and whoever recorded
    /// an attempt or a result under one; a review assignment's contributor reviews and is not among them.
    /// </summary>
    public static IReadOnlySet<string> Authors(EstateModel model, IEnumerable<RecordEnvelope> assignments)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignments);
        var authors = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope assignment in assignments.Where(a => StepOf(a) is not { Review: true }))
        {
            if (EstateModel.BodyString(assignment, "contributor") is string contributor)
            {
                authors.Add(contributor);
            }

            foreach (RecordEnvelope attempt in WorkView.AttemptsOf(model, assignment))
            {
                authors.Add(attempt.By);
                if (WorkView.ResultOf(model, attempt) is RecordEnvelope result)
                {
                    authors.Add(result.By);
                }
            }
        }

        return authors;
    }

    /// <summary>An agent principal's harness, as its record names its product; null for a person or an agent that names none.</summary>
    public static string? Harness(EstateModel model, string? principal)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.CurrentPrincipal(principal) is RecordEnvelope record && EstateModel.BodyString(record, "kind") == "agent"
            ? TJson.Text(TJson.Object(record.Body, "agent"), "product")
            : null;
    }

    /// <summary>
    /// Why a principal may not carry a review of a step instance's items before hand-off (design/flow.md § 6), the builder's
    /// independence checks: an author of the step's work reviews it only under a self review; a person's review is a person's, and in
    /// this build only an operator records a review of another's work as a person; another harness's is an agent's on a harness none of
    /// the authors used; an agent never reviews in a role the flow marks a person's. Empty when it may.
    /// </summary>
    public static IReadOnlyList<string> ReviewerRefusals(EstateModel model, FlowDefinition flow, FlowReview requirement, string role, string reviewer, IReadOnlySet<string> authors)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(authors);
        var refusals = new List<string>();
        RecordEnvelope? principal = model.CurrentPrincipal(reviewer);
        string kind = principal is null ? "missing" : EstateModel.BodyString(principal, "kind") ?? "?";
        if (requirement.Independence == "none")
        {
            refusals.Add("the item takes no review");
            return refusals;
        }

        if (requirement.Independence == "self")
        {
            return refusals;
        }

        if (authors.Contains(reviewer))
        {
            refusals.Add("it authored the step's work, as a holder of the step or by an attempt or a result, and a review by its author is not independent");
        }

        if (kind == "human" && !model.HasRole(reviewer, Roles.Operator))
        {
            refusals.Add("a person other than an operator records no review of another's work in this build");
        }
        else if (kind is not ("human" or "agent"))
        {
            refusals.Add($"a {kind} principal records no review");
        }

        if (requirement.Independence == "person" && kind != "human")
        {
            refusals.Add("the review is a person's, and it is not a person");
        }

        if (requirement.Independence == "other-harness")
        {
            string? harness = Harness(model, reviewer);
            List<string> used = authors.Select(a => Harness(model, a)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            if (kind != "agent" || harness is null)
            {
                refusals.Add("the review is an agent's on another harness, and it is not an agent that names its harness");
            }
            else if (used.Contains(harness, StringComparer.OrdinalIgnoreCase))
            {
                refusals.Add($"the review is on another harness than the step's authors used, and it runs on {harness}, as they did");
            }
        }

        if (flow.Role(role)?.Holder == "person" && kind != "human")
        {
            refusals.Add($"the flow marks the {role} role a person's, and an agent never holds it");
        }

        return refusals;
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
    /// of the run's flow, with its instance when the step runs over a list, and pins only inputs the step declares; no other assignment
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
        string? item = TJson.Text(step.Element, "instance");
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

            if (flow.Role(declared.Role)?.Holder == "person" && TJson.Object(step.Element, "review").ValueKind != JsonValueKind.Object)
            {
                context.Error("assignment.step", $"Step {declared.Key} is carried in the {declared.Role} role, which the flow marks a person's: it is never assigned to an agent.");
            }
        }

        if (declared.Each is not null && (item is null || !Items(run, declared.Each).Contains(item, StringComparer.Ordinal)))
        {
            context.Error("assignment.step", $"Step {declared.Key} runs once for each of the run's {declared.Each}; the assignment names one of them as its instance.");
        }
        else if (declared.Each is null && item is not null)
        {
            context.Error("assignment.step", $"Step {declared.Key} runs once; its assignment names no instance.");
        }

        foreach (JsonElement input in TJson.Array(step.Element, "inputs"))
        {
            if (declared.Inputs.All(i => i.Name != TJson.Text(input, "name")))
            {
                context.Error("assignment.step", $"Step {declared.Key} declares no input '{TJson.Text(input, "name")}'.");
            }
        }

        if (TJson.Object(step.Element, "review") is { ValueKind: JsonValueKind.Object } review)
        {
            ValidateReview(context, order, run, flow, declared, item, review, contributor);
        }
    }

    /// <summary>
    /// Validates an assignment of a step's review before hand-off (design/flow.md § 6), the builder's independence checks at the record:
    /// it reviews a step other than a review step, items the step declares and that take one review requirement, and its contributor
    /// meets that requirement against the step's authors as the records stood when it was written: never an author unless every item
    /// takes a self review.
    /// </summary>
    private static void ValidateReview(ValidationContext context, RecordEnvelope order, RecordEnvelope run, FlowDefinition flow, FlowStep declared, string? instance, JsonElement review, string? contributor)
    {
        if (declared.Kind == "review")
        {
            context.Error("assignment.review", $"Step {declared.Key} is a review step: its own assignment carries the review, and a review is not reviewed again.");
            return;
        }

        List<string> items = TJson.Strings(review, "items").ToList();
        List<FlowOutput?> reviewed = items.Count == 0 ? [null] : items.Select(name => declared.Outputs.FirstOrDefault(o => o.Name == name)).ToList();
        if (items.Count == 0 && declared.Outputs.Count > 0)
        {
            context.Error("assignment.review", $"Step {declared.Key} declares items: its review names the items it reviews.");
            return;
        }

        for (int index = 0; index < items.Count; index++)
        {
            if (reviewed[index] is null)
            {
                context.Error("assignment.review", $"Step {declared.Key} declares no item '{items[index]}'.");
                return;
            }
        }

        Configuration configuration = Configure(context.Model, run, flow, context.Record);
        // Requirements compare as routing groups them: a review that names no role is the estate's reviewer role's.
        List<FlowReview> requirements = reviewed.Select(o => ReviewRequirement(flow, configuration, declared, o)).Select(r => r with { Role = r.Role ?? Roles.Reviewer }).Distinct().ToList();
        if (requirements.Count != 1)
        {
            context.Error("assignment.review", $"The items of one review take one review requirement; these take {string.Join(", ", requirements.Select(r => r.Independence + " in the " + r.Role + " role"))}.");
            return;
        }

        // The subject is what carries the items: a result of the step's own work for a file or the step's result, or a record of the
        // type a record item is counted from.
        var work = new StepRef(declared.Key, instance);
        List<RecordEnvelope> carried = Routing.Assignments(context.Model, order, work).Where(a => GoalRules.Before(a, context.Record)).ToList();
        RecordEnvelope? subject = TJson.Text(TJson.Object(review, "subject"), "id") is string id ? context.Find(id) : null;
        if (subject is not null)
        {
            IReadOnlyList<string> holders = carried.SelectMany(a => context.Model.Chain(context.Model.Latest(a))).ToList();
            bool result = subject.Type == "result" && WorkView.AttemptOfResult(context.Model, subject) is RecordEnvelope attempt
                && WorkView.AssignmentOfAttempt(context.Model, attempt) is RecordEnvelope holder && holders.Contains(holder.Id);
            if (reviewed.Any(o => o is null || o.Kind == "file") ? !result : reviewed.Any(o => !(o!.Counts ?? string.Empty).Split('|').Contains(subject.Type, StringComparer.Ordinal)))
            {
                context.Error("assignment.review", $"A review of step {work} names the record that carries its items: a result of the step's own work for a file or its result, or a record of the type a record item is counted from; {subject.Type} {subject.Id} is neither.");
                return;
            }
        }

        if (contributor is null)
        {
            return;
        }

        FlowReview requirement = requirements[0];
        string role = requirement.Role ?? Roles.Reviewer;
        foreach (string refusal in ReviewerRefusals(context.Model, flow, requirement, role, contributor, Authors(context.Model, carried)))
        {
            context.Error("assignment.review", $"{Routing.NameOf(context.Model, contributor)} does not review step {work}: {refusal}.");
        }
    }

    /// <summary>Whether a record is current: now, or as the records stood when a later record was written.</summary>
    private static bool CurrentBefore(EstateModel model, RecordEnvelope record, RecordEnvelope? before) =>
        before is null ? model.IsCurrent(record)
            : GoalRules.Before(record, before) && !(model.SupersededBy.TryGetValue(record.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) && GoalRules.Before(successor, before));

    private static string? ScopeTarget(EstateModel model, string kind, string? target) =>
        kind == "product" && target is not null ? LivingDocuments.ProductIdentity(model, target) : target;
}
