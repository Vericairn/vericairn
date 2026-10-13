// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>A throwaway estate repository on disk, seeded with a charter, an operator and a builder.</summary>
public sealed class TestEstate : IDisposable
{
    public TestEstate()
    {
        Root = Path.Combine(Path.GetTempPath(), "vericairn-test-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(Root);
        OperatorId = Ulid.NewUlid(Clock).Value;
        Operator = Add("principal", OperatorId, """{"kind":"human","name":"Test Operator","roles":["operator"]}""", id: OperatorId);
        Estate = Add("estate", OperatorId, $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Exercise the record format.","operator":"{{{OperatorId}}}","recordFormat":1,"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""");
        Builder = Add("principal", OperatorId, """{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        WriteManifest(Estate.Id);
    }

    public string Root { get; }

    public string OperatorId { get; }

    public RecordEnvelope Operator { get; }

    public RecordEnvelope Estate { get; }

    public RecordEnvelope Builder { get; }

    public string BuilderId => Builder.Id;

    /// <summary>A fixed, advancing clock so records are ordered deterministically.</summary>
    public DateTimeOffset Clock { get; private set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public RecordEnvelope Add(string type, string by, string bodyJson, RecordReference? subject = null, string? supersedes = null, string? id = null, IRecordSigner? signer = null)
    {
        Clock = Clock.AddSeconds(1);
        using JsonDocument body = JsonDocument.Parse(bodyJson);
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, body.RootElement, subject, supersedes, Clock, id), signer);
        RecordAuthor.Write(Root, record);
        return record.Envelope;
    }

    public void WriteManifest(string estateId, string mode = "source")
    {
        string path = Path.Combine(Root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string manifest = mode == "source"
            ? $$$"""{"estate":"{{{estateId}}}","format":"vericairn.estate-manifest/1","recordFormat":1,"vericairn":{"commit":"0123456789abcdef0123456789abcdef01234567","mode":"source","repository":"Vericairn/vericairn"}}"""
            : $$$"""{"estate":"{{{estateId}}}","format":"vericairn.estate-manifest/1","recordFormat":1,"vericairn":{"mode":"release","repository":"Vericairn/vericairn","version":"0.1.0"}}""";
        using JsonDocument document = JsonDocument.Parse(manifest);
        File.WriteAllBytes(path, Canonical.CanonicalJson.SerializePrettyUtf8(document.RootElement));
    }

    public void WriteFile(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public EstateModel Load() => EstateLoader.Load(Root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
