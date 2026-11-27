// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <summary>The fingerprint of the identity this host presents to paired devices, when it serves them.</summary>
    public string? DesktopHandoffNode { get; set; }

    /// <inheritdoc />
    public DesktopHandoffsResponse DesktopHandoffs(string principal)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (HandoffPrincipalRefusal(model, principal) is { } problem) return new([], problem);
            try
            {
                string directory = HandoffDirectory(principal);
                if (!Directory.Exists(directory)) return new([]);
                DesktopHandoff[] handoffs = Directory.EnumerateFiles(directory, "*.prepared")
                    .Select(path => ReadHandoff(path) ?? throw new IOException("Missing prepared handoff."))
                    .Select(kept => ObserveHandoff(model, directory, kept))
                    .OrderByDescending(item => item.PreparedAt, StringComparer.Ordinal)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                return new(handoffs);
            }
            catch (Exception exception) when (HandoffStorageFailure(exception))
            {
                return new([], HandoffUnavailable());
            }
        }
    }

    /// <inheritdoc />
    public DesktopHandoffResponse PrepareDesktopHandoff(DesktopHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (HandoffPrincipalRefusal(model, request.By) is { } problem) return new(null, problem);
            if (session.IsReadOnly) return new(null, new("denied", "This session is read-only.", []));
            if (!Guid.TryParseExact(request.RequestId, "N", out Guid id) || id == Guid.Empty || request.Subject is null ||
                string.IsNullOrWhiteSpace(request.Task) || request.Task.Length > 512 ||
                string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2048 ||
                request.Draft?.Length > 32768 || request.Evidence?.Count > 16)
                return new(null, new("invalid", "Use an exact subject, a stable request identity, a task and a reason. The draft and evidence have size limits.", []));

            string key = id.ToString("N");
            IReadOnlyList<AttentionReference> evidence = request.Evidence ?? [];
            string directory = HandoffDirectory(request.By);
            string path = Path.Combine(directory, key + ".prepared");
            try
            {
                DesktopHandoff? kept = ReadHandoff(path);
                if (kept is null)
                {
                    if (ExactHandoffRecord(model, request.Subject) is null ||
                        evidence.Any(pin => ExactHandoffRecord(model, pin) is null))
                        return new(null, new("conflict", "The exact subject or an evidence reference could not be checked. Refresh the subject and review it before preparing a handoff.", []));
                    string at = RecordParser.FormatTimestamp(WorkerOptions.Clock.GetUtcNow());
                    var prepared = new DesktopHandoff(key, session.Manifest.Estate, DesktopHandoffNode, request.By, request.Subject,
                        request.Task, request.Reason, request.Draft, evidence, at);
                    StoreHandoffObservation(directory, path, JsonSerializer.SerializeToUtf8Bytes(prepared, ProtocolJson.Options));
                    kept = ReadHandoff(path) ?? throw new IOException("Missing prepared handoff.");
                }

                if (kept.Subject != request.Subject || kept.Task != request.Task || kept.Reason != request.Reason ||
                    kept.Draft != request.Draft || !kept.Evidence.SequenceEqual(evidence))
                    return new(null, new("conflict", "This request identity already belongs to a different handoff.", []));
                return new(ObserveHandoff(model, directory, kept));
            }
            catch (Exception exception) when (HandoffStorageFailure(exception))
            {
                return new(null, HandoffUnavailable());
            }
        }
    }

    /// <inheritdoc />
    public DesktopHandoffResponse OpenDesktopHandoff(DesktopHandoffOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (HandoffPrincipalRefusal(model, request.By) is { } problem) return new(null, problem);
            if (session.IsReadOnly || requestDevice.Value is { Closed: false })
                return new(null, new("denied", "Open the handoff from Workbench on the runtime's machine.", []));
            if (!Guid.TryParseExact(request.Id, "N", out Guid id))
                return new(null, new("unknown", "No handoff with that identity is available for this person.", []));

            string directory = HandoffDirectory(request.By);
            try
            {
                if (ReadHandoff(Path.Combine(directory, id.ToString("N") + ".prepared")) is not { } kept)
                    return new(null, new("unknown", "No handoff with that identity is available for this person.", []));
                string path = HandoffOpenedPath(directory, kept.Id);
                if (!File.Exists(path))
                    StoreHandoffObservation(directory, path, System.Text.Encoding.UTF8.GetBytes(RecordParser.FormatTimestamp(WorkerOptions.Clock.GetUtcNow())));
                return new(ObserveHandoff(model, directory, kept));
            }
            catch (Exception exception) when (HandoffStorageFailure(exception))
            {
                return new(null, HandoffUnavailable());
            }
        }
    }

    private Problem? HandoffPrincipalRefusal(EstateModel model, string principal)
    {
        if (AttentionPrincipalRefusal(model, principal) is { } denied) return denied;
        return EstateModel.BodyString(model.CurrentPrincipal(principal)!, "kind") == "human"
            ? null : new("denied", "Desktop handoffs are personal.", []);
    }

    private string HandoffDirectory(string principal) => Path.Combine(Path.GetDirectoryName(session.Live.Path)!, "desktop-handoffs", principal);

    private static bool HandoffStorageFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or JsonException;

    private static Problem HandoffUnavailable() => new("unavailable", "The runtime could not read or retain the handoff. Refresh Handoffs to reconcile this request.", []);

    private static RecordEnvelope? ExactHandoffRecord(EstateModel model, AttentionReference? pin) =>
        pin is { Id: not null, Hash: not null } && model.ById.TryGetValue(pin.Id, out RecordEnvelope? record) &&
        record.Type == pin.Type && record.ComputedHash == pin.Hash ? record : null;

    private static DesktopHandoff? ReadHandoff(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<DesktopHandoff>(File.ReadAllBytes(path), ProtocolJson.Options)
                ?? throw new JsonException("Empty handoff.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static string HandoffOpenedPath(string directory, string id) => Path.Combine(directory, id + ".opened");

    private static DesktopHandoff ObserveHandoff(EstateModel model, string directory, DesktopHandoff kept)
    {
        string? openedAt = File.Exists(HandoffOpenedPath(directory, kept.Id)) ? File.ReadAllText(HandoffOpenedPath(directory, kept.Id)) : null;
        RecordEnvelope? record = ExactHandoffRecord(model, kept.Subject);
        string state = record is null || kept.Evidence.Any(pin => ExactHandoffRecord(model, pin) is null)
            ? "unavailable" : ReportedCurrent(model, record) ? "current" : "superseded";
        string? status = state != "current" ? null : record?.Type switch
        {
            "work-order" => EstateStatus.StatusOf(model, record),
            "operation" => EstateModel.BodyString(record, "status"),
            _ => null,
        };
        return kept with { OpenedAt = openedAt, SubjectState = state, SubjectStatus = status };
    }

    private static void StoreHandoffObservation(string directory, string path, byte[] bytes)
    {
        OwnerOnlyState.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerOnlyState.FileMode;
            using (var file = new FileStream(temporary, options))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // The first complete preparation or opening wins even across runtime processes.
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
