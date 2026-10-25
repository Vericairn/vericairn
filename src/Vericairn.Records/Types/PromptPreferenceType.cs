// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;

namespace Vericairn.Records.Types;

/// <summary>Personal hiding and favorites, or the operator's reversible estate-wide disable.</summary>
public sealed class PromptPreferenceType : IRecordType
{
    /// <inheritdoc />
    public string Name => "prompt-preference";
    /// <inheritdoc />
    public string Summary => "A signed personal favorite or hiding choice, or an operator's estate-wide disable, with reversible changes and no effect on past uses.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("prompt", "string", true, "Stable source:key identity, independent of the selected revision.", 3),
        new("scope", "person|estate", true, "Personal choices or an estate-wide operator choice.", 3),
        new("person", "ulid", false, "Required for personal choices; the author whose choices these are.", 3),
        new("hidden", "boolean", false, "Required for personal choices. Hides from that person's offers.", 3),
        new("favorite", "boolean", false, "Required for personal choices. Orders offers within their category.", 3),
        new("disabled", "boolean", false, "Required only at estate scope. Disables new uses without deleting history.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Body.Text("prompt", true, 200, SavedPrompts.IdentityPattern, "source:key");
        string? scope = context.Body.Enum("scope", true, ["person", "estate"]);
        string? person = context.Body.Ulid("person", scope == "person");
        context.Body.Flag("hidden", scope == "person");
        context.Body.Flag("favorite", scope == "person");
        context.Body.Flag("disabled", scope == "estate");
        context.Body.NoOtherKeys();
        SavedPrompts.RequireSigned(context);
        if (scope == "estate")
        {
            if (!PolicyAuthority.OperatorAt(context.Model, context.Record.By, context.Record.At)) context.Error("prompt.authority", "Only the operator disables prompts estate-wide.");
            if (person is not null || context.Body.Has("hidden") || context.Body.Has("favorite"))
                context.Error("prompt.preference", "Estate choices only carry disabled; personal choices belong to their author.");
        }
        else
        {
            context.RequireReference("person", person, "principal");
            if (person != context.Record.By || context.Principal(person) is null)
                context.Error("prompt.authority", "Only the named principal changes their own prompt preferences.");
            if (context.Body.Has("disabled")) context.Error("prompt.preference", "Personal choices cannot disable a prompt estate-wide.");
        }
        SavedPrompts.ValidateRevision(context, Name, ["prompt", "scope", "person"]);
    }
}
