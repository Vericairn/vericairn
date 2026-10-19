// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>A throwaway estate repository on disk, seeded with a charter, an operator and a builder, at a record format (1 unless given).</summary>
public sealed class TestEstate : IDisposable
{
    public TestEstate(long recordFormat = 1)
    {
        RecordFormat = recordFormat;
        Root = Path.Combine(Path.GetTempPath(), "vericairn-test-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(Root);
        OperatorId = Ulid.NewUlid(Clock).Value;
        Operator = Add("principal", OperatorId, """{"kind":"human","name":"Test Operator","roles":["operator"]}""", id: OperatorId);
        Estate = Add("estate", OperatorId, $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Exercise the record format.","operator":"{{{OperatorId}}}","recordFormat":{{{recordFormat}}},"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""");
        Builder = Add("principal", OperatorId, """{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        WriteManifest(Estate.Id);
    }

    public string Root { get; }

    /// <summary>The estate's record format: the one its manifest states and its records are written in unless a test names another.</summary>
    public long RecordFormat { get; private set; }

    public string OperatorId { get; }

    public RecordEnvelope Operator { get; }

    public RecordEnvelope Estate { get; }

    public RecordEnvelope Builder { get; }

    public string BuilderId => Builder.Id;

    /// <summary>A fixed, advancing clock so records are ordered deterministically.</summary>
    public DateTimeOffset Clock { get; private set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves the manifest to another record format, as an upgrade does; records added afterwards state that format unless a test names another.</summary>
    public void MoveTo(long recordFormat, bool writeManifest = true)
    {
        RecordFormat = recordFormat;
        if (writeManifest) WriteManifest(Estate.Id);
    }

    /// <summary>Sets the clock, so a test can write a record that claims an earlier time than the records before it.</summary>
    public void SetClock(DateTimeOffset at) => Clock = at;

    public RecordEnvelope Add(string type, string by, string bodyJson, RecordReference? subject = null, string? supersedes = null, string? id = null, IRecordSigner? signer = null, long? format = null, IReadOnlyList<IRecordSigner>? signers = null)
    {
        Clock = Clock.AddSeconds(1);
        using JsonDocument body = JsonDocument.Parse(bodyJson);
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, body.RootElement, subject, supersedes, Clock, id, format ?? RecordFormat), signers ?? (signer is null ? [] : [signer]));
        RecordAuthor.Write(Root, record);
        return record.Envelope;
    }

    public void WriteManifest(string estateId, string mode = "source")
    {
        string path = Path.Combine(Root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string manifest = mode == "source"
            ? $$$"""{"estate":"{{{estateId}}}","format":"vericairn.estate-manifest/1","recordFormat":{{{RecordFormat}}},"vericairn":{"commit":"0123456789abcdef0123456789abcdef01234567","mode":"source","repository":"Vericairn/vericairn"}}"""
            : $$$"""{"estate":"{{{estateId}}}","format":"vericairn.estate-manifest/1","recordFormat":{{{RecordFormat}}},"vericairn":{"mode":"release","repository":"Vericairn/vericairn","version":"0.1.0"}}""";
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
            // Git's object files are read-only on Windows, which Directory.Delete refuses. Clear that first, without
            // following links and without skipping hidden entries, since Git for Windows hides .git.
            var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string file in Directory.EnumerateFiles(Root, "*", walk))
            {
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
