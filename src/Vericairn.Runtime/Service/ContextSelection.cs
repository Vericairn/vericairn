// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

/// <summary>The working instructions and effects of one step, their pinned inputs, and only the optional documents they name.</summary>
internal static partial class ContextSelection
{
    internal static List<ContextAssembly.Source> Work(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, ISourceAdapter? history = null)
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

        // Optional material is named by the work, its inputs or governing decisions, never
        // inferred merely from catalogue membership. An exact revision reference stays exact.
        List<RecordEnvelope> naming = model.Records.Where(r => required.Contains(r.Id) &&
            r.Type is not ("estate" or "principal" or "device" or "admission" or "authority" or "roster" or "document-revision")).ToList();
        var documents = new HashSet<string>(requiredDocuments, StringComparer.Ordinal);
        documents.UnionWith(naming.SelectMany(References).Where(id => model.ById.TryGetValue(id, out RecordEnvelope? r) && r.Type == "document-revision"));
        List<string> names = naming.SelectMany(r => Strings(r.Body)).ToList();
        foreach (RecordEnvelope revision in model.Current("document-revision"))
        {
            if (names.Any(value => NamesPath(value, EstateModel.BodyString(revision, "path")!)))
            {
                documents.Add(revision.Id);
            }
        }

        Dictionary<string, RecordFile> byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var sources = model.Records.Where(r => required.Contains(r.Id) || documents.Contains(r.Id))
            .Select(r => ContextAssembly.Record(r, byPath[r.ExpectedPath], required.Contains(r.Id),
                required.Contains(r.Id) ? "Read before continuing " + EstateModel.BodyString(order, "key") : "optional for this step; named by the work, its inputs or governing decisions")).ToList();
        foreach (RecordEnvelope revision in model.OfType("document-revision").Where(r => documents.Contains(r.Id)))
        {
            string path = EstateModel.BodyString(revision, "path")!;
            string hash = EstateModel.BodyString(revision, "hash")!;
            bool mandatory = requiredDocuments.Contains(revision.Id);
            byte[]? bytes = LocalOriginal(model.Root, path, hash, mandatory ? history : null);
            var identity = new JsonObject
            {
                ["kind"] = "document", ["path"] = path, ["revision"] = revision.Id,
                ["revisionHash"] = revision.ComputedHash, ["byteHash"] = hash,
                // A declaration alone does not verify a retained size.
                ["bytes"] = bytes?.LongLength,
            };
            // The self-contained pin preserves retrieval behavior: optional originals stay working-tree-only.
            if (mandatory)
            {
                identity["history"] = "local";
            }
            sources.Add(new ContextAssembly.Source(identity, bytes, mandatory, mandatory ? "Read before continuing " + EstateModel.BodyString(order, "key") : "optional for this step; named by the work, its inputs or governing decisions"));
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
        ["selection"] = order is null ? "deterministic complete record selection; every retained record original is mandatory" : "deterministic working instructions and effects, governing decisions, exact input records and signature devices; prerequisite work remains an input, not another active assignment; historical file bodies outside this step are not selected; mandatory originals before explicitly named optional sources; unmentioned catalogue entries are outside this selection",
        ["outsideSelection"] = order is null ? null : new JsonObject
        {
            ["scope"] = "The rest of the document catalogue is outside this selection, not withheld material. Listing it does not read its original bodies.",
            ["list"] = "vericairn context catalogue",
            ["api"] = "/v1/records?type=document-revision",
            ["mcp"] = "records_list with type=document-revision",
        },
        ["audience"] = "authorized estate reader",
        ["access"] = "estate-wide read permission; rechecked on retrieval",
        ["nextTask"] = order is null ? "Review current state before continuing on another surface or copy." : "Continue the recorded work; inspect current constraints and return contract.",
        ["communication"] = "presentation supplied by the caller; no depth selection by the context contract",
        ["execution"] = null,
    };

    private static bool NamesPath(string value, string path)
    {
        int start = 0;
        while ((start = value.IndexOf(path, start, StringComparison.Ordinal)) >= 0)
        {
            int end = start + path.Length;
            if ((start == 0 || !PathCharacter(value[start - 1])) && (end == value.Length || !PathCharacter(value[end]) ||
                (value[end] == '.' && (end + 1 == value.Length || char.IsWhiteSpace(value[end + 1])))))
            {
                return true;
            }

            start = end;
        }

        return false;
    }

    private static bool PathCharacter(char value) => char.IsLetterOrDigit(value) || value is '/' or '.' or '_' or '-';

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

    internal static byte[]? LocalOriginal(string root, string relative, string hash, ISourceAdapter? history = null)
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

        byte[]? bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            bytes = null;
        }

        // File.Exists also returns false on denied directory traversal. Only genuine absence is
        // missing; access failures propagate to the context's generic, metadata-free refusal.
        if (bytes is not null && ContentHash.Sha256(bytes) == hash)
        {
            return bytes;
        }

        try
        {
            bytes = history?.ReadFileAtHash(root, relative, hash);
        }
        catch (ProviderRefusedException)
        {
            return null;
        }
        // The adapter is not a substitute for verifying the pinned identity at the context boundary.
        return bytes is not null && ContentHash.Sha256(bytes) == hash ? bytes : null;
    }
}
