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

/// <summary>Conservative dependency closure for a work step, followed by the brief's optional document catalogue.</summary>
internal static partial class ContextSelection
{
    internal static List<ContextAssembly.Source> Work(EstateModel model, IReadOnlyList<RecordFile> files, RecordEnvelope order, IObjectStore objects)
    {
        var required = new HashSet<string>(model.Chain(order), StringComparer.Ordinal);
        if (model.CurrentEstate is { } charter)
        {
            required.UnionWith(model.Chain(charter));
        }

        // Working constraints are seeds too, so their governing decisions and revisions enter the closure.
        required.UnionWith(model.Current("authority").Select(r => r.Id));
        required.UnionWith(model.Current("roster").Select(r => r.Id));
        // Reverse edges follow work/effect records, never every record authored by a shared principal.
        bool changed;
        do
        {
            changed = false;
            foreach (RecordEnvelope record in model.Records)
            {
                if (required.Contains(record.Id))
                {
                    foreach (string id in References(record).Where(model.ById.ContainsKey))
                    {
                        changed |= required.Add(id);
                    }

                    changed |= required.Add(model.Latest(record).Id);
                }
                else if (References(record).Any(id => required.Contains(id) && model.ById.TryGetValue(id, out RecordEnvelope? target) &&
                    (target.Type is not ("principal" or "estate" or "device" or "resource" or "document-revision") || (target.Type == "device" && record.Type == "admission"))))
                {
                    changed |= required.Add(record.Id);
                }
            }
        }
        while (changed);

        foreach (RecordEnvelope record in model.Records.Where(r => required.Contains(r.Id)).ToList())
        {
            if (model.CurrentPrincipal(record.By) is { } principal)
            {
                required.UnionWith(model.Chain(principal));
            }
        }

        Dictionary<string, RecordFile> byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var sources = model.Records.Where(r => required.Contains(r.Id) || (r.Type == "document-revision" && model.IsCurrent(r)))
            .Select(r => ContextAssembly.Record(r, byPath[r.ExpectedPath], required.Contains(r.Id),
                required.Contains(r.Id) ? "Read before continuing " + EstateModel.BodyString(order, "key") : "optional for this step; document catalogue")).ToList();
        foreach (RecordEnvelope revision in model.OfType("document-revision").Where(r => required.Contains(r.Id) || model.IsCurrent(r)))
        {
            string path = EstateModel.BodyString(revision, "path")!;
            string hash = EstateModel.BodyString(revision, "hash")!;
            byte[]? bytes = objects.Read(hash) ?? LocalOriginal(model.Root, path, hash);
            bool mandatory = required.Contains(revision.Id);
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
        ["selection"] = "deterministic record dependency closure, including record identities named in text and signature devices; mandatory originals before optional catalogue sources",
        ["audience"] = "authorized estate reader",
        ["access"] = "estate-wide read permission; rechecked on retrieval",
        ["nextTask"] = order is null ? "Review current state before continuing on another surface or copy." : "Continue the recorded work; inspect current constraints and return contract.",
        ["communication"] = "presentation supplied by the caller; no depth selection by the context contract",
        ["execution"] = null,
    };

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

        foreach (string value in Strings(record.Body))
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
