// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A question put to a principal, kept apart from any notification of it, from record format 2 (the estate's design/goals.md § 8).</summary>
public sealed class AskType : IRecordType
{
    /// <summary>The states an asker records; answered is derived from the decision that names the ask.</summary>
    public static readonly string[] States = ["open", "notified", "withdrawn"];

    /// <inheritdoc />
    public string Name => "ask";

    /// <inheritdoc />
    public string Summary => "A question put to a principal, kept apart from any notification of it: what is asked, the recommendation first, what happens while it stays unanswered, and its state (open, notified or withdrawn). A decision that names it in answers answers it; waiting never does.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("to", "ulid", true, "The principal asked.", 2),
        new("question", "string", true, "What is asked.", 2),
        new("recommendation", "string", true, "The asker's recommendation, given first.", 2),
        new("consequence", "string", true, "What happens while the ask stays unanswered.", 2),
        new("options", "string[]", false, "The choices offered, when there are some.", 2),
        new("goal", "ulid", false, "The goal the ask belongs to, when it does.", 2),
        new("state", string.Join('|', States), true, "open when asked; notified when a notification went out, whose delivery stays unknown; withdrawn when the asker takes it back.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? to = body.Ulid("to", true);
        body.Text("question", true, 4000);
        body.Text("recommendation", true, 4000);
        body.Text("consequence", true, 2000);
        body.StringArray("options", false, allowEmpty: false, maxItemLength: 1000);
        string? goal = body.Ulid("goal", false);
        string? state = body.Enum("state", true, States);
        body.NoOtherKeys();
        context.RequireReference("to", to, "principal");
        context.RequireReference("goal", goal, "goal");
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("ask.author", $"Authoring principal {context.Record.By} has no principal record.");
        }

        RecordEnvelope? previous = context.Find(context.Record.Supersedes);
        if (previous is null)
        {
            if (state is not null and not "open")
            {
                context.Error("ask.first", "An ask starts open; a notification or a withdrawal is a later revision.");
            }

            return;
        }

        if (previous.Type != Name)
        {
            return;
        }

        if (previous.By != context.Record.By || EstateModel.BodyString(previous, "to") != to || EstateModel.BodyString(previous, "question") != EstateModel.BodyString(context.Record, "question"))
        {
            context.Error("ask.revision", "Only the asker revises an ask, and a revision changes its state, never its question or whom it is put to; a new question is a new ask.");
        }

        string? before = EstateModel.BodyString(previous, "state");
        if (before == "withdrawn")
        {
            context.Error("ask.withdrawn", "The ask was withdrawn; a new question is a new ask.");
        }
        else if (before == "notified" && state == "open")
        {
            context.Error("ask.state", "A notified ask does not become open again.");
        }
    }
}
