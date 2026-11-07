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
    private static readonly string[] RetainedAdapterSourceFields = ["path", "revision", "revisionHash"];

    /// <inheritdoc />
    public ContextResponse Context(string workOrder, long? maxBytes = null) => ContextFor(workOrder, maxBytes, null, null);

    private ContextResponse ContextFor(string workOrder, long? maxBytes, IReadOnlyList<string>? searches, string? principal)
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
            if (order is null) return ContextRefusal("not-found", "No work order has that identity.");
            ContextResponse result = ContextLocked(model, files, order, maxBytes, searches: searches, principal: principal);
            try
            {
                if (result.Package is { } package && PresentationUse(model, Vericairn.Records.Templates.PresentationTemplates.Context) is { } use)
                    return result with { Text = RenderContextTemplate(use, package, result.Text, use.Depth).Text };
                return result;
            }
            catch (InvalidOperationException) { return ContextRefusal("template-unavailable", "The explicitly adopted context template or its complete basis is unavailable."); }
        }
    }

    private ContextResponse ContextLocked(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, long? maxBytes = null, bool checkBatches = true, bool fullSelectedScope = false, RecordEnvelope? progressGoal = null,
        IReadOnlyList<string>? searches = null, string? principal = null)
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

        JsonArray? selections = null;
        if (searches is { Count: > 0 })
        {
            (List<ContextAssembly.Source>? found, JsonArray? recorded, Problem? problem) = SearchSources(model, files, sources, searches, principal);
            if (problem is not null) return new ContextResponse(problem.Message + "\n", null, problem);
            sources.AddRange(found!);
            selections = recorded;
        }

        JsonObject basis = ContextSelection.Basis(model, "This work step's instructions, governing decisions, effects and pinned input records, plus optional documents named by that work, its inputs and governing decisions. Referenced prerequisite work remains an input, not another assignment. Historical file bodies outside this step, unrelated records, output objects and unrecorded conversation are outside this selection.", order);
        if (selections is not null)
        {
            // Search selects; it never authorizes. Candidates are optional sources pinned by exact hash, after the explicit selection.
            basis["selections"] = selections;
            basis["selection"] = basis["selection"]!.GetValue<string>() + "; then up to five recorded searches, whose top three new candidates each are optional sources pinned by exact hash that grant no authority and never reorder the explicit selection";
        }
        if (!checkBatches)
        {
            basis["preflight"] = "May include a signed pending assignment. The worker must admit it together with its run intent before dispatch; this artifact grants no authority and does not resolve an interrupted batch.";
        }
        return ContextAssembly.Select(basis, sources, WorkView.RenderBrief(model, order, includeDocumentCatalogue: false, progressGoal), maxBytes, fullSelectedScope);
    }

    private BriefResponse BriefLocked(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, bool checkBatches = true, bool fullSelectedScope = true, long? maxBytes = null, CommunicationProfile? profile = null, JsonObject? promptUse = null, RecordEnvelope? progressGoal = null,
        IReadOnlyList<string>? searches = null, string? principal = null)
    {
        ContextResponse context = ContextLocked(model, files, order, maxBytes, checkBatches, fullSelectedScope, progressGoal, searches, principal);
        if (context.Package is not { } package)
        {
            return new BriefResponse(context.Text, Element(new JsonObject { ["problem"] = JsonSerializer.SerializeToNode(context.Problem, ProtocolJson.Options) }), Problem: context.Problem);
        }

        JsonObject json = JsonNode.Parse(WorkView.RenderBriefJson(model, order, progressGoal))!.AsObject();
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
        PresentationTemplateUse? use;
        try { use = PresentationUse(model, Vericairn.Records.Templates.PresentationTemplates.Context); }
        catch (InvalidOperationException) { return new BriefResponse("The adopted context template is unavailable.", Element(new JsonObject()), Problem: new("template-unavailable", "Resolve the explicit context template adoption before continuing.", [])); }
        if (use is not null) json["presentationTemplate"] = JsonSerializer.SerializeToNode(use, ProtocolJson.Options);
        ContextPackage machine = ContextAccounting.Measure(json, null);
        string lead = WorkView.RenderBrief(model, order, includeDocumentCatalogue: false, progressGoal);
        if (promptUse is not null) lead += SavedPrompts.Addendum(Element(promptUse));
        (string text, ContextTextMeasurement measurement) = ContextRendering.Render(profile is null ? lead : CommunicationPresentation.Brief(lead, profile), package);
        if (use is not null)
        {
            var rendered = RenderContextTemplate(use, package, text, profile is null ? use.Depth : CommunicationPresentation.Depth(profile, "handoff"));
            text = rendered.Text;
            measurement = new(rendered.Hash, rendered.Bytes, "exact UTF-8 bytes of the complete adopted template presentation, including its budget");
        }
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
                bool verifiedDocument = false;
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
                        EstateModel.BodyString(record, "path") == path && EstateModel.BodyString(record, "hash") == byteHash
                        && EstateModel.BodyString(record, "hashMode") == (pinned.TryGetProperty("hashMode", out JsonElement mode) ? mode.GetString() : null))
                    {
                        verifiedDocument = true;
                        original = ContextSelection.LocalOriginal(model.Root, path, byteHash,
                            pinned.TryGetProperty("history", out JsonElement history) && history.GetString() == "local" ? Providers.Source("git") : null,
                            EstateModel.BodyString(record, "hashMode"));
                    }
                }

                if (verifiedDocument && (original is null || ContentHash.Sha256(original) != byteHash))
                    original = RetainedAdapterOriginal(model, pinned, byteHash);
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

    // Adapter removal must not strand a normalized optional original. Reuse the exact context already retained for the
    // admitted run, verified against its immutable package identity. No new object, record, cache or retrieval route is needed.
    private static byte[]? RetainedAdapterOriginal(EstateModel model, JsonElement source, string hash)
    {
        if (source.GetProperty("kind").GetString() != "document") return null;
        foreach (AgentRuns.Run run in AgentRuns.All(model).OrderByDescending(r => r.Intent.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!run.Intent.Body.TryGetProperty("execution", out JsonElement execution)
                || !execution.TryGetProperty("adapter", out JsonElement adapter) || adapter.GetString() != Vericairn.Records.Representation.RepresentationContract.LocalLossless
                || !execution.TryGetProperty("contextPackage", out JsonElement packageHash)) continue;
            try
            {
                var directory = new Workers.RunDirectory(model.Manifest!.Estate, run.Id);
                var file = new FileInfo(directory.ContextFile);
                if (!file.Exists || file.Length > 64 * 1024 * 1024) continue;
                using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(directory.ContextFile));
                ContextPackage? package = retained.RootElement.GetProperty("context").Deserialize<ContextPackage>(ProtocolJson.Options);
                if (package is null || package.Hash != packageHash.GetString() || !ContextAccounting.Verify(package)) continue;
                foreach (JsonElement original in package.Payload.GetProperty("originals").EnumerateArray())
                {
                    if (original.GetProperty("kind").GetString() != "document" || original.GetProperty("mandatory").GetBoolean()
                        || original.GetProperty("byteHash").GetString() != hash) continue;
                    if (RetainedAdapterSourceFields.Any(field => !source.TryGetProperty(field, out JsonElement expected)
                        || !original.TryGetProperty(field, out JsonElement actual) || expected.GetRawText() != actual.GetRawText())) continue;
                    byte[] bytes = original.GetProperty("content").GetBytesFromBase64();
                    if (ContentHash.Sha256(bytes) == hash) return bytes;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { /* An unavailable snapshot is not evidence for another revision. */ }
        }
        return null;
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
