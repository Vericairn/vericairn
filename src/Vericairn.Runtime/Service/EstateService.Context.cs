// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Objects;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private const string ContextDenied = "Completeness cannot be confirmed.";

    /// <inheritdoc />
    public ContextResponse Context(string workOrder, long? maxBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrder);
        lock (gate)
        {
            IReadOnlyList<RecordFile> files = session.Files();
            EstateModel model = EstateLoader.Load(session.Root, files);
            if (RequestDeviceRefusal(model) is not null)
            {
                return ContextRefusal("denied", ContextDenied);
            }

            RecordEnvelope? order = WorkView.FindOrder(model, workOrder);
            return order is null ? ContextRefusal("not-found", "No work order has that identity.") : ContextLocked(model, files, order, maxBytes);
        }
    }

    private ContextResponse ContextLocked(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, long? maxBytes = null, bool checkBatches = true)
    {
        List<FindingDto> errors = model.Findings.Where(f => f.Severity == Severity.Error)
            .Select(f => new FindingDto("error", f.Code, f.Location, f.Message)).ToList();
        if (checkBatches)
        {
            errors.AddRange(InterruptedBatches(model).Select(f => new FindingDto("error", f.Code, f.Location, f.Message)));
        }
        if (errors.Count > 0)
        {
            const string message = "Context refused: restore or reconcile the missing or invalid records before continuing.";
            return new ContextResponse(message, null, new Problem("incomplete-context", message, errors));
        }

        List<ContextAssembly.Source> sources;
        try
        {
            sources = ContextSelection.Work(model, files, order, session.Objects);
        }
        catch (ObjectCorruptedException)
        {
            return ContextRefusal("incomplete-context", "A retained original failed hash verification; restore its exact bytes before continuing.");
        }
        catch (UnauthorizedAccessException)
        {
            return ContextRefusal("denied", ContextDenied);
        }
        catch (IOException)
        {
            return ContextRefusal("incomplete-context", "A selected original could not be read from this copy; no complete package was produced.");
        }

        List<ContextAssembly.Source> missing = sources.Where(s => s.Mandatory && s.Original is null).ToList();
        if (missing.Count > 0)
        {
            const string message = "Context refused: required originals are unavailable; restore their pinned bytes before the dependent step.";
            return new ContextResponse(message, null, new Problem("incomplete-context", message, missing.Select(s =>
                new FindingDto("error", "context.original-unavailable", s.Identity["path"]!.GetValue<string>(), s.Dependency)).ToList()));
        }

        JsonObject basis = ContextSelection.Basis(model, "This work step's instructions, governing decisions, effects and pinned input records, plus the current document catalogue. Referenced prerequisite work remains an input, not another assignment. Historical file bodies outside this step, unrelated records, output objects and unrecorded conversation are outside this selection.", order);
        if (!checkBatches)
        {
            basis["preflight"] = "May include a signed pending assignment. The worker must admit it together with its run intent before dispatch; this artifact grants no authority and does not resolve an interrupted batch.";
        }
        ContextResponse response = ContextAssembly.Select(basis, sources, WorkView.RenderBrief(model, order), maxBytes);
        if (response.Package is { } package)
        {
            try
            {
                if (RetainContext(package, sources) is Problem retention)
                {
                    return new ContextResponse(retention.Message + "\n", null, retention);
                }
            }
            catch (UnauthorizedAccessException)
            {
                return ContextRefusal("denied", ContextDenied);
            }
            catch (Exception exception) when (exception is ObjectCorruptedException or IOException)
            {
                return ContextRefusal("incomplete-context", "Context refused: the exact originals and manifest could not be retained and verified in this copy.");
            }
        }

        return response;
    }

    private BriefResponse BriefLocked(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, bool checkBatches = true)
    {
        ContextResponse context = ContextLocked(model, files, order, checkBatches: checkBatches);
        if (context.Package is not { } package)
        {
            return new BriefResponse(context.Text, Element(new JsonObject { ["problem"] = JsonSerializer.SerializeToNode(context.Problem, ProtocolJson.Options) }), Problem: context.Problem);
        }

        JsonObject json = JsonNode.Parse(WorkView.RenderBriefJson(model, order))!.AsObject();
        json["context"] = JsonSerializer.SerializeToNode(package, ProtocolJson.Options);
        ContextPackage machine = ContextAccounting.Measure(json, null);
        (string text, ContextTextMeasurement measurement) = ContextRendering.Render(WorkView.RenderBrief(model, order), package);
        return new BriefResponse(text, machine.Payload, measurement, JsonMeasurement: new ContextTextMeasurement(machine.Hash, machine.Bytes, "exact canonical pretty UTF-8 of the complete machine brief, including its context package and accounting"));
    }

    private Problem? RetainContext(ContextPackage package, IEnumerable<ContextAssembly.Source> sources)
    {
        List<ContextAssembly.Source> retainedSources = sources.Where(s => s.Original is not null).ToList();
        byte[] manifestBytes = CanonicalJson.SerializePrettyUtf8(package.Payload.GetProperty("manifest").GetProperty("content"));
        byte[] payloadBytes = CanonicalJson.SerializePrettyUtf8(package.Payload);
        if (payloadBytes.LongLength > EstateLayout.MaxObjectBytes || manifestBytes.LongLength > EstateLayout.MaxObjectBytes ||
            retainedSources.Any(s => s.Original!.LongLength > EstateLayout.MaxObjectBytes))
        {
            return new Problem("context-retention-limit", "Context refused: retaining a selected original, manifest or payload would exceed the estate's 8 MiB per-object limit. Use a smaller admitted selection or bounded original reads; nothing was truncated.", []);
        }

        foreach (ContextAssembly.Source source in retainedSources)
        {
            string hash = session.Objects.Put(source.Original!);
            // Put is idempotent; verify an already held object as well as a newly retained one.
            _ = session.Objects.Read(hash);
        }

        string manifest = session.Objects.Put(manifestBytes);
        _ = session.Objects.Read(manifest);
        string retained = session.Objects.Put(payloadBytes);
        _ = session.Objects.Read(retained);
        return null;
    }

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string manifest, string source)
    {
        lock (gate)
        {
            // A handle is never a capability. This happens before checking its existence or returning any metadata.
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is not null)
            {
                return new ContextReadResponse(null, null, new Problem("denied", ContextDenied, []));
            }

            if (!ContentHash.IsValid(manifest) || source is null || source.Length != 7 || source[0] != 's' || source.AsSpan(1).ContainsAnyExceptInRange('0', '9'))
            {
                return new ContextReadResponse(null, null, new Problem("invalid", "Supply the manifest and source key generated by the context package.", []));
            }

            JsonElement? identity = null;
            try
            {
                byte[]? manifestBytes = session.Objects.Read(manifest);
                if (manifestBytes is null)
                {
                    return new ContextReadResponse(null, null, new Problem("not-found", "The retained context manifest is unavailable in this copy.", []));
                }

                using JsonDocument document = JsonDocument.Parse(manifestBytes);
                JsonElement root = document.RootElement;
                if (root.GetProperty("format").GetString() != ContextAssembly.ManifestFormat || root.GetProperty("basis").GetProperty("estate").GetString() != model.Manifest?.Estate)
                {
                    return new ContextReadResponse(null, null, new Problem("invalid", "This is not a supported context manifest for this estate.", []));
                }

                foreach (JsonElement entry in root.GetProperty("sources").EnumerateArray())
                {
                    if (entry.GetProperty("key").GetString() == source)
                    {
                        identity = entry.Clone();
                        break;
                    }
                }

                if (identity is not JsonElement pinned)
                {
                    return new ContextReadResponse(null, null, new Problem("not-found", "That source is not selected by the retained manifest.", []));
                }

                byte[]? original = session.Objects.Read(pinned.GetProperty("byteHash").GetString()!);
                if (original is null)
                {
                    return new ContextReadResponse(identity, null, new Problem("original-unavailable", "The pinned original is unavailable in this copy. No latest revision was substituted.", []));
                }

                if (pinned.GetProperty("bytes") is { ValueKind: JsonValueKind.Number } size && size.GetInt64() != original.LongLength)
                {
                    throw new InvalidOperationException();
                }

                if (pinned.GetProperty("kind").GetString() == "record")
                {
                    var findings = new FindingList();
                    RecordEnvelope? record = RecordParser.Parse(original, pinned.GetProperty("path").GetString()!, findings);
                    if (record is null || findings.HasErrors || record.Id != pinned.GetProperty("id").GetString() || record.ComputedHash != pinned.GetProperty("hash").GetString())
                    {
                        throw new InvalidOperationException();
                    }
                }

                return new ContextReadResponse(identity, original, null);
            }
            catch (UnauthorizedAccessException)
            {
                return new ContextReadResponse(null, null, new Problem("denied", ContextDenied, []));
            }
            catch (Exception exception) when (exception is ObjectCorruptedException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or IOException)
            {
                return new ContextReadResponse(identity, null, new Problem("invalid-context", "The pinned manifest or original failed verification; no bytes were returned.", []));
            }
        }
    }

    private static ContextResponse ContextRefusal(string code, string message) => new(message + "\n", null, new Problem(code, message, []));
}
