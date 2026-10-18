// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>Regenerates the synthetic golden fixture with fresh disposable signing keys; never uses runtime key storage.</summary>
public static class PolicyGoldenFixtureWriter
{
    public static void Write(string source, string output)
    {
        if (Path.GetFileName(source) != "format-3-policy-estate" || Directory.Exists(output))
            throw new ArgumentException("Read the named synthetic fixture and write a new disposable directory.");
        EstateModel original = EstateLoader.Load(source);
        Assert.False(original.Findings.HasErrors, string.Join('\n', original.Findings));
        Dictionary<string, MemorySigner> keys = original.OfType("device").ToDictionary(r => r.Id, r => new MemorySigner(r.Id), StringComparer.Ordinal);
        try
        {
            Dictionary<string, JsonObject> bodies = original.Records.ToDictionary(r => r.Id, PolicyFixture.Body, StringComparer.Ordinal);
            foreach ((string id, MemorySigner key) in keys) bodies[id]["publicKey"] = RecordSigning.EncodePublicKey(key.Key.ExportSubjectPublicKeyInfo());
            Dictionary<string, string> hashes = original.Records.ToDictionary(r => r.ComputedHash, r => r.ComputedHash, StringComparer.Ordinal);
            for (int iteration = 0; ; iteration++)
            {
                if (iteration > original.Records.Count * 2) throw new InvalidOperationException("The fixture's reference graph did not converge.");
                var next = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (RecordEnvelope policy in original.OfType("estate-policy"))
                    next[DefinitionHash(PolicyFixture.Body(policy))] = DefinitionHash(Rewrite(bodies[policy.Id], hashes).AsObject());
                foreach (RecordEnvelope record in original.Records)
                    next[record.ComputedHash] = Rebuild(record, hashes, []).Envelope.ComputedHash;
                if (next.Count == hashes.Count && next.All(p => hashes.GetValueOrDefault(p.Key) == p.Value)) break;
                hashes = next;
            }

            Directory.CreateDirectory(output);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "records" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && Path.GetFileName(p) != "inventory.sha256"))
            {
                string destination = Path.Combine(output, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            foreach (RecordEnvelope record in original.Records)
                RecordAuthor.Write(output, Rebuild(record, hashes, record.Signatures.Select(s => (IRecordSigner)keys[s.Device]).ToArray()));

            if (!original.OfType("living-document").Any(r => r.Body.TryGetProperty("pathPattern", out _)))
            {
                string principal = EstateModel.BodyString(original.CurrentEstate!, "operator")!;
                MemorySigner signer = keys[original.OfType("device").First(r => EstateModel.BodyString(r, "principal") == principal).Id];
                JsonObject body = DocumentGenerationTests.Declaration();
                DateTimeOffset at = original.Records.Max(r => r.AtTime).AddMinutes(1);
                RecordAuthor.Write(output, RecordAuthor.Create(new RecordDraft("living-document", principal, JsonSerializer.SerializeToElement(body), At: at, Format: 3), [signer]));
            }
            EstateModel generated = EstateLoader.Load(output);
            Assert.False(generated.Findings.HasErrors, string.Join('\n', generated.Findings));
            Assert.All(generated.OfType("living-document"), r => Assert.True(SignatureChecks.IsAuthentic(generated, r)));
            File.WriteAllText(Path.Combine(output, "inventory.sha256"), string.Join('\n', Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
                .Select(p => (Path: Path.GetRelativePath(output, p).Replace('\\', '/'), Hash: ContentHash.Sha256File(p)))
                .OrderBy(p => p.Path, StringComparer.Ordinal).Select(p => p.Hash + "  " + p.Path)) + "\n");

            AuthoredRecord Rebuild(RecordEnvelope record, Dictionary<string, string> replacements, IReadOnlyList<IRecordSigner> signers)
            {
                RecordReference? subject = record.Subject is { } reference ? reference with { Hash = reference.Hash is null ? null : replacements.GetValueOrDefault(reference.Hash, reference.Hash) } : null;
                return RecordAuthor.Create(new RecordDraft(record.Type, record.By, JsonSerializer.SerializeToElement(Rewrite(bodies[record.Id], replacements)), subject, record.Supersedes, record.AtTime, record.Id, record.Format), signers);
            }
        }
        finally { foreach (MemorySigner key in keys.Values) key.Dispose(); }
    }

    private static string DefinitionHash(JsonObject body)
    {
        var definition = (JsonObject)body.DeepClone();
        definition.Remove("basis");
        return ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(definition)));
    }

    private static JsonNode Rewrite(JsonNode node, Dictionary<string, string> hashes) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value is null ? null : Rewrite(p.Value, hashes)))),
        JsonArray array => new JsonArray(array.Select(n => n is null ? null : Rewrite(n, hashes)).ToArray()),
        JsonValue value when value.TryGetValue<string>(out string? text) => JsonValue.Create(hashes.GetValueOrDefault(text, text))!,
        _ => node.DeepClone(),
    };

    private sealed class MemorySigner(string id) : IRecordSigner, IDisposable
    {
        public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string DeviceId => id;
        public RecordSignature Sign(ReadOnlySpan<byte> digest) => new(RecordSigning.Algorithm, id,
            Base64Url.EncodeToString(Key.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        public void Dispose() => Key.Dispose();
    }
}
