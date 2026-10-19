// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Packs.Software.CompanionInput;

namespace Vericairn.Packs.Software;

/// <summary>Selects a retained build artifact at the candidate commit. Rendering uses artifact-relative identities, never a checkout path.</summary>
public static class SoftwareCompanionSources
{
    /// <summary>The Git adapter supplies only exact-commit regular files; an unavailable inventory never falls back to the clone's head.</summary>
    public static DocumentSourceSelection Select(RecordEnvelope declaration, DocumentGeneratorDescriptor generator,
        string commit, Func<string, DocumentSourceSnapshot?> read)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            if (DocumentChecks.Generator(declaration) is not (_, IReadOnlyList<string> arguments)
                || EstateModel.BodyString(declaration, "path") is not { } destination || !DocumentPatterns.IsSafePath(destination)) return Unavailable();
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string argument in arguments)
            {
                int equal = argument.IndexOf('=');
                if (equal < 1 || !parameters.TryAdd(argument[..equal], argument[(equal + 1)..])) return Unavailable();
            }
            string parent = destination.Contains('/', StringComparison.Ordinal) ? destination[..(destination.LastIndexOf('/') + 1)] : "";
            DocumentSourceSnapshot manifest = read(parent + "manifest.json") ?? throw new CompanionUnavailableException();
            using JsonDocument parsed = JsonDocument.Parse(manifest.Bytes.AsMemory());
            JsonElement root = parsed.RootElement;
            CheckUnique(root);
            if (String(root, "commit") != parameters["build-commit"]) return Unavailable();
            JsonElement set = root.GetProperty("companions");
            if (String(set, "format") != "vericairn.software-companions/1" || String(set, "implementation") != generator.Implementation
                || String(set, "implementationHash") != generator.ImplementationHash) return Unavailable();
            string repository = String(set, "repository");
            JsonElement[] outputs = Array(set, "outputs");
            if (outputs.Select(o => String(o, "path")).Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length) return Unavailable();
            string scope = String(set, "scope");
            if (scope is not ("references" or "release" or "artifacts")) return Unavailable();
            string[] references = ["cli-reference.md", "cli-client-outcomes.md", "record-reference.md", "api-reference.md", "reference-index.md"];
            string[] reports = ["frozen-index.md", "workflow-actions.md", "components.md", "checksums.md", "documentation-contents.md"];
            if (scope != "artifacts" && !outputs.Select(o => String(o, "path")).ToHashSet(StringComparer.Ordinal).SetEquals(
                scope == "references" ? references : references.Concat(reports))) return Unavailable();
            var byPath = outputs.ToDictionary(o => String(o, "path"), StringComparer.Ordinal);
            JsonElement output = outputs.Single(o => parent + String(o, "path") == destination);
            if (String(output, "generator") != generator.Id || output.GetProperty("contract").GetInt32() != generator.ContractVersion
                || String(output, "source") != parameters["source"]) return Unavailable();
            if (CanonicalJson.Serialize(JsonSerializer.SerializeToElement(parameters)) != CanonicalJson.Serialize(output.GetProperty("parameters"))) return Unavailable();
            JsonElement[] listed = Array(set, "inputs").Concat(set.TryGetProperty("referenceInputs", out _) ? Array(set, "referenceInputs") : [])
                .Concat(set.TryGetProperty("artifactInputs", out _) ? Array(set, "artifactInputs") : []).ToArray();
            var inventory = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in listed)
            {
                string path = String(item, "path");
                if (!DocumentPatterns.IsSafeSourcePath(path) || !inventory.TryAdd(path, item)) return Unavailable();
            }
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var complete = new HashSet<string>(StringComparer.Ordinal);
            var files = new Dictionary<string, DocumentSourceSnapshot>(StringComparer.Ordinal);
            string[] needed = outputs.SelectMany(o => Array(o, "inputs")).Select(v => v.GetString()!).ToArray();
            if (!needed.ToHashSet(StringComparer.Ordinal).SetEquals(inventory.Keys)) return Unavailable();
            foreach (string path in inventory.Keys) _ = FileAt(path);
            ImmutableArray<DocumentSourceSnapshot> sources = SelectOutput(output, generator);
            return new(DocumentGenerationStatus.Success, sources, commit, null);

            DocumentSourceSnapshot FileAt(string path)
            {
                if (files.TryGetValue(path, out DocumentSourceSnapshot? cached)) return cached;
                if (!inventory.TryGetValue(path, out JsonElement item) || String(item, "path") != path) throw new CompanionUnavailableException();
                DocumentSourceSnapshot file = read(parent + path) ?? throw new CompanionUnavailableException();
                if (file.Hash != String(item, "hash") || file.Bytes.Length != item.GetProperty("bytes").GetInt64()) throw new CompanionUnavailableException();
                return files[path] = DocumentSourceSnapshot.Content("file", path, repository, path, file.Hash, file.Bytes.ToArray());
            }

            ImmutableArray<DocumentSourceSnapshot> SelectOutput(JsonElement item, DocumentGeneratorDescriptor descriptor)
            {
                string path = String(item, "path");
                if (!DocumentPatterns.IsSafePath(path) || !visiting.Add(path)) throw new CompanionUnavailableException();
                var selected = ImmutableArray.CreateBuilder<DocumentSourceSnapshot>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement value in Array(item, "inputs"))
                {
                    string input = value.GetString()!;
                    if (!seen.Add(input)) throw new CompanionUnavailableException();
                    if (byPath.TryGetValue(input, out JsonElement dependency) && !complete.Contains(input))
                    {
                        IDocumentGenerator reader = DocumentGenerators.Find(String(dependency, "generator")) ?? throw new CompanionUnavailableException();
                        if (reader.Descriptor is not { } contract || contract.ImplementationHash != descriptor.ImplementationHash) throw new CompanionUnavailableException();
                        ImmutableArray<DocumentSourceSnapshot> originals = SelectOutput(dependency, contract);
                        DocumentRendition generated = DocumentRendering.Render(reader, Context(dependency, contract, originals));
                        if (generated.Status != DocumentGenerationStatus.Success || !generated.Bytes.AsSpan().SequenceEqual(FileAt(input).Bytes.AsSpan())) throw new CompanionUnavailableException();
                        complete.Add(input);
                    }
                    selected.Add(FileAt(input));
                }
                if (!seen.Contains(String(item, "source"))) throw new CompanionUnavailableException();
                ImmutableArray<DocumentSourceSnapshot> result = selected.ToImmutable();
                if (Context(item, descriptor, result).InputDigest != String(item, "inputDigest")) throw new CompanionUnavailableException();
                visiting.Remove(path);
                return result;
            }

            DocumentGenerationContext Context(JsonElement item, DocumentGeneratorDescriptor descriptor, ImmutableArray<DocumentSourceSnapshot> sources)
            {
                var inputs = item.GetProperty("parameters").EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
                if (inputs["source"] != String(item, "source") || inputs["build-commit"] != parameters["build-commit"]
                    || String(item, "generator") != descriptor.Id || item.GetProperty("contract").GetInt32() != descriptor.ContractVersion)
                    throw new CompanionUnavailableException();
                return new(DocumentGeneration.InterfaceVersion, descriptor, inputs, inputs["selection"], null, sources);
            }
        }
        catch (Exception error) when (error is CompanionUnavailableException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return Unavailable();
        }
        DocumentSourceSelection Unavailable() => new(DocumentGenerationStatus.Unavailable, [], commit, "The candidate's complete retained build inventory is unavailable or inconsistent.");
    }
}
