// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

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

    private ContextResponse ContextLocked(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, long? maxBytes = null, bool checkBatches = true, bool fullSelectedScope = false)
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
            sources = ContextSelection.Work(model, files, order, Providers.Source("git"));
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
            const string message = "Context refused: required originals are unavailable; restore their exact pinned bytes or use a complete estate clone with matching local history before the dependent step. Shallow or missing history cannot supply originals; no other revision is substituted.";
            return new ContextResponse(message, null, new Problem("incomplete-context", message, missing.Select(s =>
                new FindingDto("error", "context.original-unavailable", s.Identity["path"]!.GetValue<string>(), s.Dependency)).ToList()));
        }

        JsonObject basis = ContextSelection.Basis(model, "This work step's instructions, governing decisions, effects and pinned input records, plus optional documents named by that work, its inputs and governing decisions. Referenced prerequisite work remains an input, not another assignment. Historical file bodies outside this step, unrelated records, output objects and unrecorded conversation are outside this selection.", order);
        if (!checkBatches)
        {
            basis["preflight"] = "May include a signed pending assignment. The worker must admit it together with its run intent before dispatch; this artifact grants no authority and does not resolve an interrupted batch.";
        }
        return ContextAssembly.Select(basis, sources, WorkView.RenderBrief(model, order, includeDocumentCatalogue: false), maxBytes, fullSelectedScope);
    }

    private BriefResponse BriefLocked(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, bool checkBatches = true, bool fullSelectedScope = true, long? maxBytes = null, CommunicationProfile? profile = null, JsonObject? promptUse = null)
    {
        ContextResponse context = ContextLocked(model, files, order, maxBytes, checkBatches, fullSelectedScope);
        if (context.Package is not { } package)
        {
            return new BriefResponse(context.Text, Element(new JsonObject { ["problem"] = JsonSerializer.SerializeToNode(context.Problem, ProtocolJson.Options) }), Problem: context.Problem);
        }

        JsonObject json = JsonNode.Parse(WorkView.RenderBriefJson(model, order))!.AsObject();
        if (promptUse is not null)
        {
            JsonObject payload = JsonNode.Parse(package.Payload.GetRawText())!.AsObject();
            payload["promptUse"] = promptUse.DeepClone();
            package = ContextAccounting.Measure(payload, maxBytes);
            if (maxBytes is long cap && package.Bytes > cap)
                return new BriefResponse("The prompt and mandatory context exceed the byte allocation.", Element(new JsonObject()),
                    Problem: new("context-overflow", "Increase the explicit context allocation within existing authority or choose a smaller prompt; the spending cap is unchanged.", []));
        }
        json["documentsScope"] = "The documents field is the catalogue, not the context selection. The context manifest alone names selected sources; list the catalogue with vericairn context catalogue.";
        json["context"] = JsonSerializer.SerializeToNode(package, ProtocolJson.Options);
        ContextPackage machine = ContextAccounting.Measure(json, null);
        string lead = WorkView.RenderBrief(model, order, includeDocumentCatalogue: false);
        if (promptUse is not null) lead += SavedPrompts.Addendum(Element(promptUse));
        (string text, ContextTextMeasurement measurement) = ContextRendering.Render(profile is null ? lead : CommunicationPresentation.Brief(lead, profile), package);
        return new BriefResponse(text, machine.Payload, measurement, JsonMeasurement: new ContextTextMeasurement(machine.Hash, machine.Bytes, "exact canonical pretty UTF-8 of the complete machine brief, including its context package and accounting"));
    }

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string pin)
    {
        lock (gate)
        {
            JsonElement? identity = null;
            try
            {
                // The pin is an identity, never a capability. Check access before decoding it.
                IReadOnlyList<RecordFile> files = session.Files();
                EstateModel model = EstateLoader.Load(session.Root, files);
                if (RequestDeviceRefusal(model) is not null)
                {
                    return new ContextReadResponse(null, null, new Problem("denied", ContextDenied, []));
                }

                JsonElement pinned = ContextRetrieval.Decode(pin, model.Manifest!.Estate);
                identity = pinned;
                string kind = pinned.GetProperty("kind").GetString()!;
                string byteHash = pinned.GetProperty("byteHash").GetString()!;
                byte[]? original = null;
                if (kind == "record")
                {
                    string id = pinned.GetProperty("id").GetString()!;
                    if (model.ById.TryGetValue(id, out RecordEnvelope? record))
                    {
                        if (record.Type != pinned.GetProperty("type").GetString() || record.ComputedHash != pinned.GetProperty("hash").GetString() || record.ExpectedPath != pinned.GetProperty("path").GetString())
                        {
                            return Unavailable(identity);
                        }

                        original = files.Single(f => f.Path == record.ExpectedPath).Bytes;
                    }
                }
                else if (kind == "document")
                {
                    string revision = pinned.GetProperty("revision").GetString()!;
                    string path = pinned.GetProperty("path").GetString()!;
                    if (model.ById.TryGetValue(revision, out RecordEnvelope? record) && record.Type == "document-revision" &&
                        record.ComputedHash == pinned.GetProperty("revisionHash").GetString() &&
                        EstateModel.BodyString(record, "path") == path && EstateModel.BodyString(record, "hash") == byteHash)
                    {
                        original = ContextSelection.LocalOriginal(model.Root, path, byteHash,
                            pinned.TryGetProperty("history", out JsonElement history) && history.GetString() == "local" ? Providers.Source("git") : null);
                    }
                }

                if (original is null || ContentHash.Sha256(original) != byteHash ||
                    (pinned.TryGetProperty("bytes", out JsonElement size) && size.ValueKind == JsonValueKind.Number && size.GetInt64() != original.LongLength))
                {
                    return Unavailable(identity);
                }

                return new ContextReadResponse(identity, original, null);
            }
            catch (UnauthorizedAccessException)
            {
                return new ContextReadResponse(null, null, new Problem("denied", ContextDenied, []));
            }
            catch (IOException)
            {
                return Unavailable(identity);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
            {
                return new ContextReadResponse(null, null, new Problem("invalid-context", "Supply the complete pinned identity generated by the context package; no bytes were returned.", []));
            }
        }
    }

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string manifest, string source)
    {
        lock (gate)
        {
            // Compatibility route for the unreleased manifest handle. Reads no retained objects.
            return RequestDeviceRefusal(session.Load()) is not null
                ? new ContextReadResponse(null, null, new Problem("denied", ContextDenied, []))
                : Unavailable(null);
        }
    }

    private static ContextReadResponse Unavailable(JsonElement? identity) => new(identity, null,
        new Problem("original-unavailable", "The pinned original is unavailable in this copy. Restore its exact bytes or, for a required original, use a complete estate clone with matching local history. No other revision was substituted. Prepare context again to obtain a self-contained pinned identity.", []));

    private static ContextResponse ContextRefusal(string code, string message) => new(message + "\n", null, new Problem(code, message, []));
}
