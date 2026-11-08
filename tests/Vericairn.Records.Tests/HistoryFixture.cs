// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// A disposable format-3 estate for history checkpoints: the root operator's first device admitted by itself, an agent builder's device
/// admitted by it, and helpers that prepare, sign and store checkpoints with their inventory objects. Keys live in a temporary store.
/// </summary>
public sealed class HistoryFixture : IDisposable
{
    private readonly string keyHome = Path.Combine(Path.GetTempPath(), "vericairn-history-test-keys-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public HistoryFixture()
    {
        Estate = new TestEstate(3);
        store = new FileKeyStore(keyHome);
        OperatorDevice = Device(Estate.OperatorId);
        OperatorSigner = new StoreSigner(store, OperatorDevice.Id);
        Estate.Add("admission", Estate.OperatorId, Admission(Estate.OperatorId, "The first authority."), OperatorDevice.Reference, signer: OperatorSigner);
        BuilderDevice = Device(Estate.BuilderId);
        BuilderSigner = new StoreSigner(store, BuilderDevice.Id);
        BuilderAdmission = Estate.Add("admission", Estate.OperatorId, Admission(Estate.BuilderId, "The builder's device."), BuilderDevice.Reference, signer: OperatorSigner);
    }

    public TestEstate Estate { get; }

    public RecordEnvelope OperatorDevice { get; }

    public StoreSigner OperatorSigner { get; }

    public RecordEnvelope BuilderDevice { get; }

    public StoreSigner BuilderSigner { get; }

    public RecordEnvelope BuilderAdmission { get; }

    public string Root => Estate.Root;

    public const string OrderBody = """{"key":"HIS-1","kind":"ordinary","title":"Witness the history","intent":"Exercise checkpoints.","scope":["checkpoints"],"exitQuestion":"Does it hold?","inputs":[],"deliverables":["a test"]}""";

    public static string Admission(string principal, string note, string status = "admitted", string? revokedAt = null) =>
        revokedAt is null
            ? $$"""{"principal":"{{principal}}","status":"{{status}}","note":"{{note}}"}"""
            : $$"""{"principal":"{{principal}}","status":"{{status}}","note":"{{note}}","revokedAt":"{{revokedAt}}"}""";

    public static string Order(string key, string title = "A recorded work order") =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"{{title}}","intent":"Exercise checkpoints.","scope":["checkpoints"],"exitQuestion":"Does it hold?","inputs":[],"deliverables":["a test"]}""";

    /// <summary>A new device for a principal, signed by its own key.</summary>
    public RecordEnvelope Device(string principal, string name = "Test signing device")
    {
        string id = Ulid.NewUlid(Estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return Estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"{{name}}","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"none","purpose":"signing"}""",
            id: id, signer: new StoreSigner(store, id));
    }

    public StoreSigner Signer(RecordEnvelope device) => new(store, device.Id);

    /// <summary>An admitted device for a principal, admitted by the operator's first device; returns the device and its admission.</summary>
    public (RecordEnvelope Device, RecordEnvelope Admission) Admitted(string principal, string name = "Another signing device")
    {
        RecordEnvelope device = Device(principal, name);
        return (device, Estate.Add("admission", Estate.OperatorId, Admission(principal, name + "."), device.Reference, signer: OperatorSigner));
    }

    /// <summary>The operator's recovery device, its key kept offline, admitted by the operator's first device.</summary>
    public RecordEnvelope RecoveryDevice()
    {
        string id = Ulid.NewUlid(Estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        RecordEnvelope device = Estate.Add("device", Estate.OperatorId, $$"""{"principal":"{{Estate.OperatorId}}","name":"Recovery key","platform":"linux","custody":"offline","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"none","purpose":"recovery"}""",
            id: id, signer: new StoreSigner(store, id));
        Estate.Add("admission", Estate.OperatorId, Admission(Estate.OperatorId, "The recovery key."), device.Reference, signer: OperatorSigner);
        return device;
    }

    /// <summary>A rotation of a device to a new key, signed by its current key and the new one; returns the revision and the new key's signer.</summary>
    public (RecordEnvelope Revision, StoreSigner Signer) Rotate(RecordEnvelope device, IRecordSigner current, string? identity = null)
    {
        string keyId = Ulid.NewUlid().Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(keyId));
        JsonObject body = Body(device);
        body["publicKey"] = publicKey;
        var next = new StoreSigner(store, keyId, identity ?? device.Id);
        string principal = body["principal"]!.GetValue<string>();
        return (Estate.Add("device", principal, body.ToJsonString(), supersedes: device.Id, signers: [current, next]), next);
    }

    /// <summary>The revocation of a device from now on, superseding its admission, by the operator unless another author is given.</summary>
    public RecordEnvelope Revoke(RecordEnvelope device, RecordEnvelope admission, IRecordSigner? signer = null, string? by = null)
    {
        string principal = Body(device)["principal"]!.GetValue<string>();
        string revokedAt = RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1));
        return Estate.Add("admission", by ?? Estate.OperatorId, Admission(principal, "Revoked.", "revoked", revokedAt), device.Reference, supersedes: admission.Id, signer: signer ?? OperatorSigner);
    }

    /// <summary>A decision the operator signs, the basis of the grants a test records.</summary>
    public RecordEnvelope Decision() => Estate.Add("decision", Estate.OperatorId,
        $$"""{"title":"Basis","statement":"Grant authority.","summary":"A basis.","synthesis":"For tests.","effects":["grant"],"affects":[],"decidedBy":"{{Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":["publication"]}""",
        signer: OperatorSigner);

    /// <summary>A grant of scopes to a principal, or a revision of one, by the operator.</summary>
    public RecordEnvelope Grant(string grantee, RecordEnvelope basis, string[] scopes, string? supersedes = null) => Estate.Add("authority", Estate.OperatorId,
        new JsonObject { ["grantee"] = grantee, ["scope"] = new JsonArray([.. scopes.Select(s => (JsonNode)s)]), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = basis.Id }.ToJsonString(),
        supersedes: supersedes, signer: OperatorSigner);

    /// <summary>A closing checkpoint, by the root operator unless another author is given.</summary>
    public RecordEnvelope Close(string kind, string id, string? by = null, IRecordSigner? signer = null) =>
        Checkpoint(by, signer, "closing", closes: (kind, id));

    /// <summary>Moves the charter to required signatures by the root operator's signed revision.</summary>
    public RecordEnvelope Require() => Estate.Add("estate", Estate.OperatorId,
        $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Exercise the record format.","operator":"{{{Estate.OperatorId}}}","recordFormat":3,"policies":{"signatures":"required","releaseAcceptance":["operator"]}}""",
        supersedes: Estate.Estate.Id, signer: OperatorSigner);

    public EstateModel Load() => Estate.Load();

    /// <summary>Prepares the next checkpoint from the estate as it is now, stores its objects and signs it.</summary>
    public RecordEnvelope Checkpoint(string? by = null, IRecordSigner? signer = null, string? trigger = null, IEnumerable<RecordEnvelope>? anchored = null,
        IEnumerable<(string Id, string Hash, string Reason)>? removed = null, (string Kind, string Id)? closes = null, bool sign = true, long maxObjectBytes = HistoryInventory.MaxObjectBytes)
    {
        EstateModel model = Load();
        CheckpointDraft draft = SignedHistory.Prepare(model, trigger ?? (SignedHistory.Of(model).Heads.Count == 0 ? "adoption" : "manual"), anchored, removed, closes, maxObjectBytes: maxObjectBytes);
        StoreObjects(draft);
        return Estate.Add("history-checkpoint", by ?? Estate.OperatorId, draft.Body.GetRawText(), signer: sign ? signer ?? OperatorSigner : null);
    }

    /// <summary>The adoption checkpoint, by the root operator, anchoring every unsigned record present.</summary>
    public RecordEnvelope Adopt()
    {
        EstateModel model = Load();
        return Checkpoint(trigger: "adoption", anchored: model.Records.Where(r => r.Signatures.Count == 0));
    }

    public void StoreObjects(CheckpointDraft draft)
    {
        foreach (byte[] bytes in draft.Objects)
        {
            string path = ObjectPath(ContentHash.Sha256(bytes));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    public string ObjectPath(string hash) => Path.Combine(Root, EstateLayout.ObjectsDirectory, hash[ContentHash.Prefix.Length..]);

    public string PathOf(RecordEnvelope record) => Path.Combine(Root, record.ExpectedPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Deletes a record's file and returns its bytes.</summary>
    public byte[] Remove(RecordEnvelope record)
    {
        byte[] bytes = File.ReadAllBytes(PathOf(record));
        File.Delete(PathOf(record));
        return bytes;
    }

    public void Restore(RecordEnvelope record, byte[] bytes) => File.WriteAllBytes(PathOf(record), bytes);

    public static JsonObject Body(RecordEnvelope record) => JsonNode.Parse(record.Body.GetRawText())!.AsObject();

    public static string[] Codes(EstateModel model) => [.. model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.Code)];

    public void Dispose()
    {
        Estate.Dispose();
        try
        {
            Directory.Delete(keyHome, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
