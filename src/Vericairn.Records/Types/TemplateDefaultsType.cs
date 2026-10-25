// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Templates;

namespace Vericairn.Records.Types;

/// <summary>Recorded defaults for one exact template at an adoption scope, without granting any authority.</summary>
public sealed class TemplateDefaultsType : IRecordType
{
    /// <inheritdoc />
    public string Name => "template-defaults";
    /// <inheritdoc />
    public string Summary => "Option defaults for an exact template version at estate, domain, product or project scope. Narrower scopes win when an instance is created; existing instances keep their recorded choices.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("template", "object", true, "The exact content pin: id (namespace/name), version (semantic version) and hash (canonical SHA-256).", 3),
        new("scope", "object", true, "kind (estate|domain|product|project), and target for every kind except estate. One current declaration per template version and scope.", 3),
        new("options", "object", true, "Allowed option choices: template option ids, or section-id.option-id and field-id.option-id, mapped to string values. Unspecified values inherit wider defaults.", 3),
        new("reason", "string", true, "Why this adoption scope uses these defaults. Recording them assigns no role and grants no authority.", 3),
    ];
    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        BodyReader? template = body.Nested("template", true);
        if (template is not null) { TemplateRecords.ReadPin(template); template.NoOtherKeys(); }
        BodyReader? scope = body.Nested("scope", true);
        TemplateRecords.ReadScope(scope, true);
        BodyReader? choices = body.Nested("options", true);
        if (choices is not null) foreach (System.Text.Json.JsonProperty choice in choices.Element.EnumerateObject()) choices.Text(choice.Name, true, 300);
        body.Text("reason", true, 2000);
        body.NoOtherKeys();
        if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer))
            context.Error("template.defaults-author", "Template defaults require the operator or existing administer authority.");
        if (template is null || scope is null) return;
        string identity = Canonical.CanonicalJson.Serialize(template.Element) + Canonical.CanonicalJson.Serialize(scope.Element);
        if (context.Model.IsCurrent(context.Record) && context.Model.Current(Name).Any(r => r.Id != context.Record.Id
            && r.Body.TryGetProperty("template", out var other) && r.Body.TryGetProperty("scope", out var otherScope)
            && Canonical.CanonicalJson.Serialize(other) + Canonical.CanonicalJson.Serialize(otherScope) == identity))
            context.Error("template.defaults-conflict", "Revise the current defaults at this scope instead of declaring competing defaults.");
        if (context.Find(context.Record.Supersedes) is { } previous
            && Canonical.CanonicalJson.Serialize(previous.Body.GetProperty("template")) + Canonical.CanonicalJson.Serialize(previous.Body.GetProperty("scope")) != identity)
            context.Error("template.defaults-scope", "A defaults revision keeps its template version and scope.");
        try
        {
            var definition = TemplateCatalog.Resolve(context.Model, new(TJson.Text(template.Element, "id")!, TJson.Text(template.Element, "version")!, TJson.Text(template.Element, "hash")!));
            var allowed = TemplateDefinition.OptionDefinitions(definition.Content);
            if (choices is not null) foreach (var choice in choices.Element.EnumerateObject())
                if (choice.Value.ValueKind != System.Text.Json.JsonValueKind.String || !allowed.TryGetValue(choice.Name, out var option)
                    || !TJson.Strings(option, "values").Contains(choice.Value.GetString(), StringComparer.Ordinal))
                    context.Error("template.defaults-option", "Defaults must name allowed template options and values.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { context.Warning("template.unavailable", "The exact template for these retained defaults is unavailable here."); }
    }
}
