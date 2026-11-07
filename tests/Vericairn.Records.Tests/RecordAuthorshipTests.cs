// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

public sealed class RecordAuthorshipTests : IDisposable
{
    public const string Code = "record.author";
    public const string ResourceBody = """{"kind":"service","name":"Site","role":"the public site","location":{"url":"https://example.invalid/site"},"visibility":"public"}""";
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-authorship-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public RecordAuthorshipTests() => store = new FileKeyStore(home);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnExistingRecordByAnIdNamingNothingIsInformationAndKeepsItsBytes(long format)
    {
        using var estate = new TestEstate(format);
        string nobody = Ulid.NewUlid().Value;
        RecordEnvelope resource = estate.Add("resource", nobody, ResourceBody);
        string path = Path.Combine(estate.Root, resource.ExpectedPath);
        byte[] before = File.ReadAllBytes(path);

        EstateModel model = estate.Load();

        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(resource.ExpectedPath + "#by", finding.Location);
        Assert.Contains(nobody, finding.Message, StringComparison.Ordinal);
        Assert.Contains("names no record", finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(resource.ComputedHash, model.ById[resource.Id].ComputedHash);
    }

    [Fact]
    public void AnExistingRecordByAnotherRecordTypesIdIsInformation()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope resource = estate.Add("resource", estate.Estate.Id, ResourceBody);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(resource.ExpectedPath + "#by", finding.Location);
        Assert.Contains("type estate", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHistoricalRecordDatedBeforeItsPrincipalIsInformationNamingBothTimes()
    {
        using var estate = new TestEstate(3);
        DateTimeOffset early = estate.Clock;
        RecordEnvelope reviewer = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Late Reviewer","roles":["reviewer"]}""");
        estate.SetClock(early.AddSeconds(-5));
        RecordEnvelope resource = estate.Add("resource", reviewer.Id, ResourceBody);
        // A later supersession by a known author leaves the earlier record's reading as it was.
        estate.SetClock(reviewer.AtTime);
        estate.Add("resource", reviewer.Id, ResourceBody.Replace("Site", "Website", StringComparison.Ordinal), supersedes: resource.Id);

        EstateModel model = estate.Load();

        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.Equal(resource.ExpectedPath + "#by", finding.Location);
        Assert.Contains(reviewer.At, finding.Message, StringComparison.Ordinal);
        Assert.Contains(resource.At, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownAuthorsSignedOrUnsignedHaveNoFinding(bool withSignature)
    {
        using var estate = new TestEstate(3);
        IRecordSigner? signer = null;
        if (withSignature)
        {
            RecordEnvelope operatorDevice = Device(estate, estate.OperatorId);
            estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root."}""", operatorDevice.Reference, signer: new StoreSigner(store, operatorDevice.Id));
            RecordEnvelope builderDevice = Device(estate, estate.BuilderId);
            estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Builder."}""", builderDevice.Reference, signer: new StoreSigner(store, operatorDevice.Id));
            signer = new StoreSigner(store, builderDevice.Id);
        }

        RecordEnvelope resource = estate.Add("resource", estate.BuilderId, ResourceBody, signer: signer);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);
        Assert.Equal(withSignature, resource.Signatures.Count == 1);
    }

    [Fact]
    public void ARecordAtItsPrincipalsOwnTimeAndTheSelfAuthoredOperatorHaveNoFinding()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope reviewer = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Reviewer","roles":["reviewer"]}""");
        estate.SetClock(reviewer.AtTime.AddSeconds(-1));
        RecordEnvelope same = estate.Add("resource", reviewer.Id, ResourceBody);
        Assert.Equal(reviewer.At, same.At);
        EstateModel model = estate.Load();
        Assert.Equal(estate.OperatorId, model.ById[estate.OperatorId].By);
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);
    }

    [Fact]
    public void ATypeSpecificAuthorErrorStaysAnError()
    {
        using var estate = new TestEstate(3);
        estate.Add("work-order", Ulid.NewUlid().Value, """{"key":"W-1","kind":"ordinary","title":"Orphan","intent":"No author.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "work.author" && f.Severity == Severity.Error);
        Assert.Equal(Severity.Info, Assert.Single(model.Findings, f => f.Code == Code).Severity);
    }

    public void Dispose()
    {
        try { Directory.Delete(home, recursive: true); }
        catch (IOException) { /* Best effort for test-owned files. */ }
    }

    private RecordEnvelope Device(TestEstate estate, string principal)
    {
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"Test device","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"none","purpose":"signing"}""", id: id, signer: new StoreSigner(store, id));
    }
}
