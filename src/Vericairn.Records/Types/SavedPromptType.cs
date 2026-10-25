// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A signed, shared prompt definition; retirement and revision preserve earlier uses.</summary>
public sealed class SavedPromptType : IRecordType
{
    /// <inheritdoc />
    public string Name => "saved-prompt";
    /// <inheritdoc />
    public string Summary => "A categorized prompt with described placeholders and suited roles, revised by supersession and retired without deleting its history. Its text grants no authority.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("key", "string", true, "Stable lower-case prompt key.", 3),
        new("title", "string", true, "Short title.", 3),
        new("description", "string", true, "When and why to use the prompt.", 3),
        new("text", "string", true, "Comment text with optional {name} placeholders; never authority.", 3),
        new("category", "string", true, "One category, with slash-separated nested segments.", 3),
        new("placeholders", "object[]", true, "Each placeholder's name and description; every named value is required.", 3),
        new("tags", "string[]", true, "Free discovery tags.", 3),
        new("roles", "string[]", true, "Suited principal roles; an empty list suits every role.", 3),
        new("state", "active|retired", true, "Retirement removes the prompt from offers and keeps every earlier revision.", 3),
        new("origin", "object", false, "Copied built-in identity: source, key, version and exact definition hash.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SavedPrompts.ValidateDefinition(context.Body);
        string? state = context.Body.Enum("state", true, ["active", "retired"]);
        if (context.Body.Nested("origin", false) is { } origin) SavedPrompts.ValidateOrigin(origin);
        context.Body.NoOtherKeys();
        SavedPrompts.RequireSigned(context);
        if (!SavedPrompts.MayWrite(context))
            context.Error("prompt.authority", "Writing or retiring shared prompts requires the operator or an explicit manage-prompts grant.");
        SavedPrompts.ValidateRevision(context, Name, ["key"]);
        if (context.Find(context.Record.Supersedes) is { } previous)
        {
            if (EstateModel.BodyString(previous, "state") == "retired")
                context.Error("prompt.retired", "A retired prompt stays retired; copy its definition under a new key.");
            if (!SavedPrompts.SameField(previous.Body, context.Record.Body, "origin"))
                context.Error("prompt.origin", "A revision keeps its original copied source.");
        }
        else if (state != "active") context.Error("prompt.state", "A new prompt starts active.");
    }
}
