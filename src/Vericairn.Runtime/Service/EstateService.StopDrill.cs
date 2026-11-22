// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The timed stop drill (OPS-21, decision 01M7DH3HRQH21S1G8A5EYA5FBT). Before the carrier runs work unattended, the time it takes to stop
/// and contain a run is measured, and measured again monthly, by a recurring duty whose proof is this drill's verification: the worker on
/// the runtime's machine starts a scratch run, asks it to stop as <c>worker stop</c> does, and records the time from the stop to the observed
/// containment and to the run's last write. A scratch run not contained within the limit fails the drill, and a failed verification is
/// required attention until a later drill on the same machine passes.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>How long from the stop to containment a drill allows when its request names no limit.</summary>
    internal const int DefaultDrillLimitSeconds = 60;

    /// <inheritdoc />
    public StopDrillResponse StopDrill(StopDrillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int grace = request.GraceSeconds ?? (int)Math.Ceiling(WorkerOptions.Grace.TotalSeconds);
        int limit = request.LimitSeconds ?? DefaultDrillLimitSeconds;
        StopDrillResponse Refused(string code, string message) => new(false, message, false, null, null, null, null, null, grace, limit, null, new Problem(code, message, []));
        if (request.GraceSeconds is < 0 or > 3600) return Refused("refused", "A drill's grace is from 0 to 3600 seconds.");
        if (request.LimitSeconds is < 1 or > 3600) return Refused("refused", "A drill's limit is from 1 to 3600 seconds.");

        string estate;
        string commit;
        string device;
        RecordReference charter;
        lock (gate)
        {
            if (session.IsReadOnly) return Refused("denied", "This session is read-only; the drill records its measurement, so nothing was started.");
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return Refused(denied.Code, denied.Message);
            // From 0.9.4 (FIX-159): no scratch run starts while the estate reports an error, as no run is admitted then.
            if (ActingRefusal.BeforeActing(model, "starting the stop drill's scratch run") is { } invalid)
                return new(false, invalid.Message, false, null, null, null, null, null, grace, limit, null, invalid);
            if (request.By is null || !Ulid.IsValid(request.By) || model.CurrentPrincipal(request.By) is null)
                return Refused("refused", "Name the principal running the drill by its ULID, a principal of this estate.");
            if (model.CurrentEstate is not { } current) return Refused("refused", "The estate has no charter for the drill's verification to name.");
            charter = current.Reference;
            using JsonDocument probe = JsonDocument.Parse("{}");
            (string? held, string? refusal) = HeldSigner(new RecordDraft("verification", request.By, probe.RootElement, charter), model);
            if (held is null)
                return Refused("refused", $"The drill records its measurement as a verification signed by an admitted device of {request.By} held on this machine, and none is: {refusal ?? "no key is held here"}. Nothing was started.");
            device = held;
            if (DrillCommit() is not { } named) return Refused("refused", "The drill's verification names the commit of the runtime it measured, and neither this build nor the estate's repository names one. Nothing was started.");
            commit = named;
            estate = session.Manifest.Estate;
        }

        // The scratch run is started, stopped and observed without the gate, so every other operation keeps answering while it runs.
        DrillMeasurement measured = Workers.StopDrill.Run(estate, request.By, WorkerOptions, TimeSpan.FromSeconds(limit), grace);
        if (measured.RequestedAt is null)
            return Refused("unavailable", "The drill measured nothing, so nothing was recorded: " + measured.Problem + ".");

        bool passed = measured.WithinLimit;
        static string InSeconds(TimeSpan? span) => span is null ? "unknown" : span.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        string summary = passed
            ? $"The stop drill's scratch run was contained {InSeconds(measured.StopToContained)} seconds after its stop was requested, within the limit of {limit} seconds; its last write came {InSeconds(measured.StopToLastWrite)} seconds after the stop."
            : measured.Contained
                ? $"The stop drill's scratch run was not contained within the limit of {limit} seconds after its stop was requested; once the drill escalated the stop itself it was contained {InSeconds(measured.StopToContained)} seconds after the request, its last write {InSeconds(measured.StopToLastWrite)} seconds after it."
                : $"The stop drill's scratch run was not contained within the limit of {limit} seconds after its stop was requested, and its containment was not observed: {measured.Problem}.";
        var evidence = new JsonArray(
            Evidence($"The stop was requested at {measured.RequestedAt} by {request.By}, naming a grace of {grace} seconds."),
            Evidence(measured.StopToContained is { } contained
                ? $"From the stop request to the observed containment of the scratch run's whole boundary: {Milliseconds(contained)} milliseconds."
                : "The containment of the scratch run's boundary was not observed."),
            Evidence(measured.StopToLastWrite is { } written
                ? $"From the stop request to the scratch run's last write, at {measured.LastWriteAt}: {Milliseconds(written)} milliseconds."
                : "The scratch run's last write could not be read."));
        if (measured.Stop is { TransmittedAt: { } transmittedAt } stop)
            evidence.Add(Evidence($"The worker transmitted the stop at {transmittedAt} ({stop.Transmission})" + (stop.EscalatedAt is { } escalatedAt ? $" and escalated it at {escalatedAt} ({stop.Escalation})." : ".")));
        var limitations = new JsonArray(
            "The scratch run ignores the stop it is sent, as an unresponsive agent would, so it is contained by the worker's escalation after the grace; an agent that stops when asked is contained sooner.",
            $"Measured by the worker on the machine of the device that signs this, observing at its heartbeat of {WorkerOptions.Heartbeat.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds; another machine's worker is not measured.",
            "The last write is the scratch run's own, read to within its write interval of 50 milliseconds; a real run's effects outside its workspace are not observed.");
        if (measured.Hurried) limitations.Add("The limit passed before containment was observed, so the drill escalated the stop itself.");
        DateTimeOffset started = DateTimeOffset.Parse(measured.StartedAt, CultureInfo.InvariantCulture);
        DateTimeOffset finished = DateTimeOffset.Parse(measured.FinishedAt, CultureInfo.InvariantCulture);
        var body = new JsonObject
        {
            ["method"] = Workers.StopDrill.Method,
            ["commit"] = commit,
            ["command"] = "vericairn worker drill",
            ["environment"] = new JsonObject { ["device"] = device, ["platform"] = VericairnHome.Platform() },
            ["role"] = "automation",
            ["outcome"] = passed ? "passed" : "failed",
            ["summary"] = summary,
            ["durationMs"] = (long)(finished - started).TotalMilliseconds,
            ["startedAt"] = measured.StartedAt,
            ["finishedAt"] = measured.FinishedAt,
            ["evidence"] = evidence,
            ["limitations"] = limitations,
        };
        WriteOutcome recorded;
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordReference subject = model.CurrentEstate?.Reference ?? charter;
            recorded = WriteLocked(new RecordDraft("verification", request.By, Element(body), subject), model);
        }

        long? toContained = measured.StopToContained is { } c ? Milliseconds(c) : null;
        long? toLastWrite = measured.StopToLastWrite is { } w ? Milliseconds(w) : null;
        if (!recorded.Ok)
            return new(false, "The drill measured, and its verification was refused: " + recorded.Problem!.Message + " " + summary, passed, measured.RequestedAt, measured.ContainedAt,
                toContained, measured.LastWriteAt, toLastWrite, grace, limit, null, recorded.Problem);
        return new(true, summary + (passed ? string.Empty : " A failed drill is required attention until a later drill on this machine passes."), passed, measured.RequestedAt, measured.ContainedAt,
            toContained, measured.LastWriteAt, toLastWrite, grace, limit, recorded.Written);
    }

    private static JsonObject Evidence(string summary) => new() { ["summary"] = summary };

    private static long Milliseconds(TimeSpan span) => (long)Math.Round(span.TotalMilliseconds, MidpointRounding.AwayFromZero);

    // The source commit of the runtime the drill measured, from its build's identity; else the estate repository's checked-out commit.
    private string? DrillCommit()
    {
        string version = typeof(EstateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        Match build = Regex.Match(version, @"\+([0-9a-f]{40})\b");
        if (build.Success) return build.Groups[1].Value;
        try
        {
            return Providers.Source("git")?.Inspect(session.Root).Commit is { } commit && Patterns.Commit.IsMatch(commit) ? commit : null;
        }
        catch (ProviderRefusedException)
        {
            return null;
        }
    }
}
