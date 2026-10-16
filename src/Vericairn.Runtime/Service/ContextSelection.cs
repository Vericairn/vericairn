// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Objects;

namespace Vericairn.Runtime.Service;

/// <summary>The working instructions and effects of one step, their pinned inputs, and the optional document catalogue.</summary>
internal static partial class ContextSelection
{
    internal static List<ContextAssembly.Source> Work(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, IObjectStore objects)
    {
        var working = new HashSet<string>(model.Chain(order), StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var requiredDocuments = new HashSet<string>(StringComparer.Ordinal);
        if (model.CurrentEstate is { } charter)
        {
            working.UnionWith(model.Chain(charter));
        }

        working.UnionWith(model.Current("authority").Select(r => r.Id));
        working.UnionWith(model.Current("roster").Select(r => r.Id));
        // A prerequisite's original record is an input, not another active assignment. Do not walk
        // back through everything sharing a decision, or require every historical file behind it.
        bool changed;
        do
        {
            changed = false;
            foreach (RecordEnvelope record in model.Records)
            {
                if (working.Contains(record.Id))
                {
                    foreach (string id in References(record).Where(model.ById.ContainsKey))
                    {
                        RecordEnvelope input = model.ById[id];
                        AddChain(model, required, input);
                        if (input.Type == "document-revision")
                        {
                            requiredDocuments.Add(id);
                        }

                        if (input.Type is "decision" or "operation" or "goal")
                        {
                            changed |= AddChain(model, working, input);
                        }
                    }
                }
                else if ((record.Subject is { } subject && working.Contains(subject.Id)) ||
                    (record.Type == "decision" && record.Body.TryGetProperty("affects", out JsonElement affects) && Strings(affects).Any(working.Contains)) ||
                    (record.Type is "operation" or "goal" or "ask" && References(record).Any(id => working.Contains(id) &&
                        model.ById[id].Type is "work-order" or "assignment" or "attempt" or "result" or "operation" or "goal")))
                {
                    changed |= AddChain(model, working, record);
                }
            }
        }
        while (changed);

        required.UnionWith(working);
        foreach (RecordEnvelope record in model.Records.Where(r => required.Contains(r.Id)).ToList())
        {
            if (model.CurrentPrincipal(record.By) is { } principal)
            {
                required.UnionWith(model.Chain(principal));
            }

            foreach (var signature in record.Signatures)
            {
                if (model.ById.TryGetValue(signature.Device, out RecordEnvelope? device))
                {
                    AddChain(model, required, device);
                    foreach (RecordEnvelope admission in model.OfType("admission").Where(r => References(r).Contains(device.Id)))
                    {
                        AddChain(model, required, admission);
                    }
                }
            }
        }

        Dictionary<string, RecordFile> byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var sources = model.Records.Where(r => required.Contains(r.Id) || (r.Type == "document-revision" && model.IsCurrent(r)))
            .Select(r => ContextAssembly.Record(r, byPath[r.ExpectedPath], required.Contains(r.Id),
                required.Contains(r.Id) ? "Read before continuing " + EstateModel.BodyString(order, "key") : "optional for this step; document catalogue")).ToList();
        foreach (RecordEnvelope revision in model.OfType("document-revision").Where(r => requiredDocuments.Contains(r.Id) || model.IsCurrent(r)))
        {
            string path = EstateModel.BodyString(revision, "path")!;
            string hash = EstateModel.BodyString(revision, "hash")!;
            byte[]? bytes = objects.Read(hash) ?? LocalOriginal(model.Root, path, hash);
            bool mandatory = requiredDocuments.Contains(revision.Id);
            sources.Add(new ContextAssembly.Source(new JsonObject
            {
                ["kind"] = "document", ["path"] = path, ["revision"] = revision.Id,
                ["revisionHash"] = revision.ComputedHash, ["byteHash"] = hash,
                // A declaration alone does not verify a retained size.
                ["bytes"] = bytes?.LongLength,
            }, bytes, mandatory, mandatory ? "Read before continuing " + EstateModel.BodyString(order, "key") : "optional for this step; document catalogue"));
        }

        return sources;
    }

    internal static JsonObject Basis(EstateModel model, string scope, RecordEnvelope? order = null) => new()
    {
        ["estate"] = model.Manifest!.Estate,
        ["recordFormat"] = model.Manifest.RecordFormat,
        ["workOrder"] = order is null ? null : new JsonObject { ["id"] = order.Id, ["hash"] = order.ComputedHash },
        ["generator"] = ContextAssembly.Format,
        ["asOf"] = model.Records.OrderBy(r => r.At, StringComparer.Ordinal).LastOrDefault()?.At,
        ["asOfMethod"] = "latest recorded timestamp in the observed file set; not live external health",
        ["scope"] = scope,
        ["selection"] = order is null ? "deterministic complete record selection; every retained record original is mandatory" : "deterministic working instructions and effects, governing decisions, exact input records and signature devices; prerequisite work remains an input, not another active assignment; historical file bodies outside this step are not selected; mandatory originals before optional catalogue sources",
        ["audience"] = "authorized estate reader",
        ["access"] = "estate-wide read permission; rechecked on retrieval",
        ["nextTask"] = order is null ? "Review current state before continuing on another surface or copy." : "Continue the recorded work; inspect current constraints and return contract.",
        ["communication"] = "presentation supplied by the caller; no depth selection by the context contract",
        ["execution"] = null,
    };

    private static bool AddChain(EstateModel model, HashSet<string> selected, RecordEnvelope record)
    {
        int before = selected.Count;
        selected.UnionWith(model.Chain(model.Latest(record)));
        return selected.Count != before;
    }

    private static IEnumerable<string> References(RecordEnvelope record)
    {
        if (record.Subject is { } subject)
        {
            yield return subject.Id;
        }

        if (record.Supersedes is string predecessor)
        {
            yield return predecessor;
        }

        foreach (var signature in record.Signatures)
        {
            yield return signature.Device;
        }

        // A decision's affected peers are targets of that decision, not extra inputs to this step.
        foreach (string value in record.Body.EnumerateObject().Where(p => record.Type != "decision" || p.Name != "affects").SelectMany(p => Strings(p.Value)))
        {
            yield return value;
            foreach (Match match in RecordIds().Matches(value))
            {
                yield return match.Value;
            }
        }
    }

    [GeneratedRegex(@"\b[0-7][0-9A-HJKMNP-TV-Z]{25}\b", RegexOptions.CultureInvariant)]
    private static partial Regex RecordIds();

    private static IEnumerable<string> Strings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString()!;
        }
        else if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            IEnumerable<JsonElement> children = element.ValueKind == JsonValueKind.Object ? element.EnumerateObject().Select(p => p.Value) : element.EnumerateArray();
            foreach (string value in children.SelectMany(Strings))
            {
                yield return value;
            }
        }
    }

    private static byte[]? LocalOriginal(string root, string relative, string hash)
    {
        // Only normalized repository files are eligible; never follow a link into machine-local custody.
        string[] parts = relative.Split('/');
        if (parts.Length < 2 || parts[0] is not ("design" or "docs") || parts.Any(p => p is "" or "." or ".." || p.Contains('\\', StringComparison.Ordinal)))
        {
            return null;
        }

        string path = root;
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
            if (new FileInfo(path).LinkTarget is not null)
            {
                return null;
            }
        }

        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        return ContentHash.Sha256(bytes) == hash ? bytes : null;
    }
}
