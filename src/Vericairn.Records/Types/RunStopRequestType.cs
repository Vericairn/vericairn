// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A person's signed request to cancel or contain an exact run, separate from the worker's observed stop facts.</summary>
public sealed class RunStopRequestType : IRecordType
{
    /// <inheritdoc />
    public string Name => "run-stop-request";

    /// <inheritdoc />
    public string Summary => "A request to cancel or contain a run, pinning the exact run operation reviewed. The runtime's request and the worker's subsequent observations are separate facts.";

    /// <inheritdoc />
    public string? SubjectType => "operation";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 3;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("run", "ULID", true, "The run's original operation id."),
        new("operation", "cancel|contain", true, "Cancel with the runtime's normal grace, or contain with no additional grace."),
        new("requestId", "string", true, "The original request identity from the reviewing device."),
        new("reason", "string", true, "The request's reason as it will appear in the run's stop observations."),
        new("expectedStopReason", "string", false, "The exact prior stop to tighten, for containment only."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? run = body.Ulid("run", true);
        string? operation = body.Enum("operation", true, "cancel", "contain");
        string? id = body.Text("requestId", true, 32);
        string? reason = body.Text("reason", true, 512);
        string? prior = body.Text("expectedStopReason", false, 512);
        body.NoOtherKeys();

        if (!Guid.TryParseExact(id, "N", out Guid request) || request == Guid.Empty || request.ToString("N") != id)
            context.Error("run-stop-request.identity", "The request keeps a nonzero request identity in 32 lowercase hexadecimal digits.");
        if (reason is not null && (reason.Length == 0 || reason != reason.Trim()))
            context.Error("run-stop-request.reason", "A stop's reason has nonblank text and no surrounding whitespace.");
        if (prior is not null && (operation != "contain" || prior == reason))
            context.Error("run-stop-request.containment", "Containment names the earlier stop and its new reason separately.");
        if (context.Record.Supersedes is not null)
            context.Error("run-stop-request.supersession", "A run stop request keeps its original identity and cannot supersede another record.");
        if (context.Principal(context.Record.By) is not { } principal || EstateModel.BodyString(principal, "kind") != "human")
            context.Error("run-stop-request.person", "A reviewed stop request is authored by a person.");

        RecordEnvelope? reviewed = context.Find(context.Record.Subject?.Id);
        if (reviewed is null || reviewed.Type != "operation" || EstateModel.BodyString(reviewed, "action") != OperationType.AgentRun ||
            context.Model.Chain(reviewed)[0] != run || EstateModel.BodyString(reviewed, "status") is not ("not-started" or "dispatched" or "uncertain"))
            context.Error("run-stop-request.run", "The subject pins an in-flight operation revision of the named run.");
    }
}
