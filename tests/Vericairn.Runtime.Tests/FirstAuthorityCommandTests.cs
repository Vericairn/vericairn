// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// <c>vericairn estate first-authority</c> at the runtime's boundary (CODE-72): it lists the self-admissions that could be the estate's
/// first authority with the evidence for each, and names one of the root operator's own devices in the manifest after showing the change
/// and its plan hash, refusing where witnessed history speaks to the root, where the first authority already works or the copy reads
/// unsettled, for a device that is no candidate, for a device-signed request and for anyone but the root operator. The estates are those
/// of 0.9.2's first-authority cases (<see cref="FirstAuthorityTests"/>); how the root of trust is judged is theirs and unchanged.
/// </summary>
public sealed class FirstAuthorityCommandTests
{
    private static readonly DateTimeOffset Early = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SeveralSelfAdmissionsWithNoneNamedAreListedAndNamingOneMakesTheEstateReadIt()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-1"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        (RecordEnvelope device, RecordEnvelope stray) = Earlier(f);
        using EstateService api = EstateService.Open(f.Root);
        byte[] before = Manifest(f);

        // The listing writes nothing and shows both, with what each one's device did and how the estate reads with each.
        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        Assert.Equal(f.Estate.OperatorId, listed.RootOperator);
        Assert.Null(listed.Named);
        Assert.Null(listed.FirstAuthority);
        Assert.Contains(listed.Findings, x => x.Code == "manifest.first-authority" && x.Message.Contains("vericairn estate first-authority", StringComparison.Ordinal));
        Assert.Equal(new[] { first.Id, stray.Id }.Order(StringComparer.Ordinal), listed.Candidates.Select(c => c.Admission));
        FirstAuthorityCandidate ours = listed.Candidates.Single(c => c.Admission == first.Id);
        FirstAuthorityCandidate theirs = listed.Candidates.Single(c => c.Admission == stray.Id);
        Assert.True(ours.Nameable && theirs.Nameable);
        Assert.Equal(f.OperatorDevice.Id, ours.Device);
        Assert.Equal(device.Id, theirs.Device);
        Assert.Equal(f.Estate.OperatorId, ours.Principal);
        Assert.Equal("Earlier device", theirs.Name);
        // Read as the first authority, the operator's device admits the builder's and signed every record it wrote; the stray one admits none.
        Assert.Equal(1, ours.Admits);
        Assert.Equal(0, theirs.Admits);
        Assert.True(ours.RecordsSigned > theirs.RecordsSigned);
        // Its device record, its admission and the work order it signed.
        Assert.Equal(3, theirs.RecordsSigned);
        Assert.False(ours.Revoked || ours.WitnessedFirst || ours.Named || ours.Reads);
        Assert.Null(listed.Change);
        Assert.Null(listed.PlanHash);
        Assert.Equal(before, Manifest(f));

        // The check shows the change and its plan hash, writing nothing; the write with that hash names it.
        FirstAuthorityResponse check = Ok(api.FirstAuthority(new FirstAuthorityRequest(Device: f.OperatorDevice.Id, Check: true)));
        Assert.Equal(new FirstAuthorityChange(EstateLayout.ManifestPath, null, first.Id), check.Change);
        Assert.StartsWith("sha256:", check.PlanHash, StringComparison.Ordinal);
        Assert.False(check.Applied);
        Assert.Equal(before, Manifest(f));

        FirstAuthorityResponse named = Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: check.PlanHash)));
        Assert.True(named.Applied);
        Assert.Equal(first.Id, named.Named);
        Assert.Equal(first.Id, named.FirstAuthority);
        Assert.Empty(named.Findings);
        // The other self-admission still reads signature.self-admission; removing it is the operator's.
        FindingDto remaining = Assert.Single(named.Remaining);
        Assert.Equal("signature.self-admission", remaining.Code);
        Assert.StartsWith(stray.ExpectedPath, remaining.Location, StringComparison.Ordinal);
        Assert.True(File.Exists(f.PathOf(stray)));

        EstateModel model = f.Load();
        Assert.Equal(first.Id, model.Manifest!.FirstAuthority);
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Counts(model.ById[real.Id]));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.DoesNotContain("manifest.first-authority", HistoryFixture.Codes(model));
        // Only the manifest's firstAuthority changed.
        JsonObject was = JsonNode.Parse(before)!.AsObject(), now = JsonNode.Parse(Manifest(f))!.AsObject();
        now.Remove("firstAuthority");
        Assert.True(JsonNode.DeepEquals(was, now));

        // Once it works, the command never moves it, nor names it again.
        Assert.Contains("never moves a working first authority", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: stray.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("first authority already", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))), StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestNamingNoSelfAdmissionIsReplaced()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-2"), signer: f.OperatorSigner);
        RecordEnvelope first = FirstAuthority(f);
        f.Estate.NameFirstAuthority(f.BuilderAdmission.Id);
        using EstateService api = EstateService.Open(f.Root);
        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        Assert.Equal(f.BuilderAdmission.Id, listed.Named);
        Assert.Null(listed.FirstAuthority);
        Assert.Contains(listed.Findings, x => x.Code == "manifest.first-authority");

        string plan = Ok(api.FirstAuthority(new FirstAuthorityRequest(Device: f.OperatorDevice.Id, Check: true))).PlanHash!;
        FirstAuthorityResponse named = Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Device: f.OperatorDevice.Id, ExpectPlan: plan)));
        Assert.Equal(new FirstAuthorityChange(EstateLayout.ManifestPath, f.BuilderAdmission.Id, first.Id), named.Change);
        Assert.Empty(named.Remaining);
        EstateModel model = f.Load();
        Assert.Equal(first.Id, model.Manifest!.FirstAuthority);
        Assert.True(model.Counts(model.ById[real.Id]));
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAdoptedEstateIsRefusedWhereItsHistorySpeaksOrItsRootIsSettled(bool named)
    {
        using var f = new HistoryFixture();
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-3"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        if (named) f.Estate.NameFirstAuthority(first.Id);
        f.Adopt();
        (_, RecordEnvelope stray) = Earlier(f);
        using EstateService api = EstateService.Open(f.Root);
        byte[] before = Manifest(f);

        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        // With none named and two candidates no device is admitted, so no checkpoint counts as the estate reads now.
        Assert.Equal(named, listed.Adopted);
        FirstAuthorityCandidate ours = listed.Candidates.Single(c => c.Admission == first.Id);
        Assert.True(ours.WitnessedFirst);
        Assert.False(listed.Candidates.Single(c => c.Admission == stray.Id).WitnessedFirst);
        Assert.All(listed.Candidates, c => Assert.False(c.Nameable));
        if (named)
        {
            // The manifest names it and the adoption confirms it: the root is settled.
            Assert.Equal(first.Id, listed.FirstAuthority);
            Assert.Contains("never moves a working first authority", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: stray.Id, Check: true))), StringComparison.Ordinal);
            Assert.Contains("first authority already", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))), StringComparison.Ordinal);
        }
        else
        {
            // With none named the estate reads none, yet its history witnesses the operator's first: the command chooses none.
            Assert.Null(listed.FirstAuthority);
            foreach (string admission in new[] { first.Id, stray.Id })
            {
                string refusal = Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: admission, Check: true)));
                Assert.Contains("Witnessed history speaks to the first authority", refusal, StringComparison.Ordinal);
                Assert.Contains(first.Id, refusal, StringComparison.Ordinal);
                Assert.Contains("estate check --expect", refusal, StringComparison.Ordinal);
            }
        }

        Assert.Equal(before, Manifest(f));
    }

    [Fact]
    public void ABeforeAdoptionNamedFirstAuthorityIsNeverMoved()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        f.Estate.NameFirstAuthority(first.Id);
        (RecordEnvelope device, RecordEnvelope stray) = Earlier(f);
        using EstateService api = EstateService.Open(f.Root);
        Assert.False(Ok(api.FirstAuthority(new FirstAuthorityRequest())).Adopted);
        string refusal = Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: device.Id, Check: true)));
        Assert.Contains("never moves a working first authority", refusal, StringComparison.Ordinal);
        Assert.Contains(first.Id, refusal, StringComparison.Ordinal);
        Assert.Equal(first.Id, f.Load().Manifest!.FirstAuthority);
    }

    [Fact]
    public void AnAdoptionWitnessingBothSettlesTheNamedOneAndNamesNeitherFirst()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        f.Estate.NameFirstAuthority(first.Id);
        // Another self-admission present when the estate adopts: the adoption witnesses both in one checkpoint, so neither is first.
        (_, RecordEnvelope stray) = Earlier(f);
        f.Adopt();
        using EstateService api = EstateService.Open(f.Root);
        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        Assert.True(listed.Adopted);
        Assert.Equal(first.Id, listed.FirstAuthority);
        Assert.All(listed.Candidates, c => Assert.False(c.WitnessedFirst));
        Assert.Contains("never moves a working first authority", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: stray.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("first authority already", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsettledCopyIsRefusedWhicheverIsNamed()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        f.Adopt();
        (_, RecordEnvelope stray) = Earlier(f);
        f.Estate.NameFirstAuthority(stray.Id);
        using EstateService api = EstateService.Open(f.Root);
        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        Assert.True(listed.Unsettled);
        Assert.Contains(listed.Findings, x => x.Code == "history.manifest-root");
        Assert.All(listed.Candidates, c => Assert.Contains("estate check --expect", c.NotNameable, StringComparison.Ordinal));
        foreach (string admission in new[] { first.Id, stray.Id })
            Assert.Contains("reads unsettled", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: admission, Check: true))), StringComparison.Ordinal);
        Assert.Equal(stray.Id, f.Load().Manifest!.FirstAuthority);
    }

    [Fact]
    public void AnAdoptedEstateWhoseManifestNamesANonCandidateIsRefusedAndTheOperatorsEditRepairsIt()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        f.Adopt();
        f.Estate.NameFirstAuthority(f.BuilderAdmission.Id);
        using EstateService api = EstateService.Open(f.Root);
        Assert.True(Ok(api.FirstAuthority(new FirstAuthorityRequest())).Unsettled);
        Assert.Contains("reads unsettled", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))), StringComparison.Ordinal);

        // The repair the upgrade guide gives for this case: the operator's own edit of firstAuthority back to the witnessed self-admission.
        f.Estate.NameFirstAuthority(first.Id);
        EstateModel model = f.Load();
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.False(model.Trust.Unsettled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAddedSelfAdmissionWithItsOwnAdoptionIsNeverNamed(bool named)
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        if (named) f.Estate.NameFirstAuthority(first.Id);
        (RecordEnvelope device, RecordEnvelope stray) = Earlier(f);
        // An adoption under the earlier device, prepared while the first authority's file is away so that it never lists it.
        byte[] bytes = f.Remove(first);
        f.Estate.NameFirstAuthority(null);
        f.Checkpoint(signer: f.Signer(device), trigger: "adoption");
        f.Restore(first, bytes);
        if (named) f.Estate.NameFirstAuthority(first.Id);
        using EstateService api = EstateService.Open(f.Root);

        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        Assert.Equal(named, listed.Unsettled);
        // Its own adoption witnesses the added one first when it is read as the first authority, which is never reason to name it.
        Assert.True(listed.Candidates.Single(c => c.Admission == stray.Id).WitnessedFirst);
        foreach (string admission in new[] { first.Id, stray.Id })
        {
            string refusal = Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: admission, Check: true)));
            Assert.Contains(named ? "reads unsettled" : "Witnessed history speaks to the first authority", refusal, StringComparison.Ordinal);
        }

        Assert.Equal(named ? first.Id : null, f.Load().Manifest!.FirstAuthority);
    }

    [Fact]
    public void TwoWitnessedFirstAuthoritiesAreRefused()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        f.Estate.NameFirstAuthority(first.Id);
        f.Adopt();
        (RecordEnvelope device, RecordEnvelope stray) = Earlier(f);
        byte[] bytes = f.Remove(first);
        f.Estate.NameFirstAuthority(null);
        f.Checkpoint(signer: f.Signer(device), trigger: "adoption");
        f.Restore(first, bytes);
        using EstateService api = EstateService.Open(f.Root);
        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        Assert.All(listed.Candidates, c => Assert.True(c.WitnessedFirst));
        foreach (string admission in new[] { first.Id, stray.Id })
        {
            string refusal = Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: admission, Check: true)));
            Assert.Contains("are each witnessed before every other", refusal, StringComparison.Ordinal);
        }

        Assert.Null(f.Load().Manifest!.FirstAuthority);
    }

    [Fact]
    public void ARevokedFirstDeviceStaysNameableAndShowsRevoked()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = FirstAuthority(f);
        // The operator's second device, admitted by the first, later revokes the first; a stray self-admission is added after.
        (RecordEnvelope second, RecordEnvelope secondAdmission) = f.Admitted(f.Estate.OperatorId, "The operator's second device");
        f.Revoke(f.OperatorDevice, first, f.Signer(second));
        RecordEnvelope work = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-4", "Signed by the second device"), signer: f.Signer(second));
        (_, RecordEnvelope stray) = Earlier(f);
        using EstateService api = EstateService.Open(f.Root);
        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        FirstAuthorityCandidate ours = listed.Candidates.Single(c => c.Admission == first.Id);
        Assert.True(ours.Revoked);
        Assert.True(ours.Nameable);
        Assert.Equal(2, ours.Admits);
        Assert.False(listed.Candidates.Single(c => c.Admission == stray.Id).Revoked);

        string plan = Ok(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))).PlanHash!;
        Assert.True(Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan))).Applied);
        EstateModel model = f.Load();
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(SignatureChecks.IsAdmitted(model, second));
        Assert.True(model.Counts(model.ById[secondAdmission.Id]));
        Assert.True(model.Counts(model.ById[work.Id]));
    }

    [Fact]
    public void ADeviceThatIsNoCandidateIsRefused()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = FirstAuthority(f);
        (_, RecordEnvelope stray) = Earlier(f);
        // A device of the agent builder's admitting itself in the root operator's name, and a recovery device, self-admitted and admitted.
        RecordEnvelope agents = f.Device(f.Estate.BuilderId, "An agent's device");
        RecordEnvelope agentAdmission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "Itself."), agents.Reference, signer: f.Signer(agents));
        RecordEnvelope recovery = f.RecoveryDevice();
        RecordEnvelope recoveryItself = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), recovery.Reference, signer: f.Signer(recovery));
        using EstateService api = EstateService.Open(f.Root);
        byte[] before = Manifest(f);

        FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
        FirstAuthorityCandidate agent = listed.Candidates.Single(c => c.Admission == agentAdmission.Id);
        Assert.False(agent.Nameable);
        Assert.Contains(f.Estate.BuilderId, agent.NotNameable, StringComparison.Ordinal);
        Assert.DoesNotContain(listed.Candidates, c => c.Admission == recoveryItself.Id);

        Assert.Contains("one of the root operator's own devices", Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: agents.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("recovery device", Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: recovery.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("admits a recovery device", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: recoveryItself.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("is a device of " + f.Estate.BuilderId, Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: f.BuilderDevice.Id, Check: true))), StringComparison.Ordinal);
        RecordEnvelope byBuilder = f.Estate.Add("admission", f.Estate.BuilderId, HistoryFixture.Admission(f.Estate.BuilderId, "By the builder."), f.Device(f.Estate.BuilderId, "Builder's other device").Reference, signer: f.BuilderSigner);
        Assert.Contains("is by " + f.Estate.BuilderId, Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: byBuilder.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("not signed by the device it admits", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: f.BuilderAdmission.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("is a work-order record", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-5"), signer: f.OperatorSigner).Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("No record", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: "01ARZ3NDEKTSV4RRFFQ69G5FAV", Check: true))), StringComparison.Ordinal);
        Assert.Contains("not both", Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: f.OperatorDevice.Id, Admission: first.Id, Check: true))), StringComparison.Ordinal);

        // A device that admitted itself twice is named by its admission.
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Again."), f.OperatorDevice.Reference, signer: f.OperatorSigner);
        Assert.Contains("name one with --admission", Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: f.OperatorDevice.Id, Check: true))), StringComparison.Ordinal);
        Assert.Equal(before, Manifest(f));
        Assert.True(File.Exists(f.PathOf(stray)));
    }

    [Fact]
    public void OnlyTheRootOperatorWritesWithTheCheckedPlanAndNeverByADeviceSignedRequest()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = FirstAuthority(f);
        (RecordEnvelope device, _) = Earlier(f);
        using EstateService api = EstateService.Open(f.Root);
        byte[] before = Manifest(f);
        string plan = Ok(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))).PlanHash!;

        Assert.Contains("pass --by " + f.Estate.OperatorId, Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, ExpectPlan: plan))), StringComparison.Ordinal);
        Assert.Contains("root operator's act", Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.BuilderId, Admission: first.Id, ExpectPlan: plan))), StringComparison.Ordinal);
        Assert.Contains("--expect-plan", Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id))), StringComparison.Ordinal);
        Assert.Contains("no longer reads as the check", Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: "sha256:" + new string('0', 64)))), StringComparison.Ordinal);
        Assert.Contains("--expect-plan names a change", Refused(api.FirstAuthority(new FirstAuthorityRequest(ExpectPlan: plan))), StringComparison.Ordinal);

        // A device-signed request neither lists nor names.
        string publicKey = HistoryFixture.Body(f.OperatorDevice)["publicKey"]!.GetValue<string>();
        using (api.ForDeviceRequest(f.OperatorDevice.Id, publicKey))
        {
            Assert.Contains("device-signed request", Refused(api.FirstAuthority(new FirstAuthorityRequest())), StringComparison.Ordinal);
            Assert.Contains("device-signed request", Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan))), StringComparison.Ordinal);
        }

        // The estate changing after the check makes its plan stale: another self-admission is a new candidate.
        RecordEnvelope another = f.Device(f.Estate.OperatorId, "Another device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), another.Reference, signer: f.Signer(another));
        Assert.Contains("no longer reads as the check", Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan))), StringComparison.Ordinal);
        Assert.Equal(before, Manifest(f));

        // A read-only copy shows the change and writes none.
        using EstateService readOnly = EstateService.OpenReadOnly(f.Root);
        string fresh = Ok(readOnly.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))).PlanHash!;
        Assert.Contains("read-only", Refused(readOnly.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: fresh))), StringComparison.Ordinal);
        Assert.True(Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: fresh))).Applied);
        Assert.Equal(first.Id, f.Load().Manifest!.FirstAuthority);
    }

    [Fact]
    public void TheUpgradeGuidesRepairNamesTheFirstDeviceThenTheOperatorRemovesTheOthersAndTheUpgradeProceeds()
    {
        using var f = new HistoryFixture();
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-6"), signer: f.OperatorSigner);
        RecordEnvelope first = FirstAuthority(f);
        (RecordEnvelope device, RecordEnvelope stray) = Earlier(f, order: false);
        Publish(f, "0.9.2");
        using EstateService api = EstateService.Open(f.Root);

        // Refused, as any estate with errors is, with manifest.first-authority pointing at the command.
        (UpgradeResponse? refused, Problem? problem) = api.Upgrade(new UpgradeRequest("0.9.2", f.Estate.OperatorId, false));
        Assert.Null(refused);
        Assert.Contains(problem!.Findings, x => x.Code == "manifest.first-authority" && x.Message.Contains("vericairn estate first-authority", StringComparison.Ordinal));

        string plan = Ok(api.FirstAuthority(new FirstAuthorityRequest(Device: f.OperatorDevice.Id, Check: true))).PlanHash!;
        FirstAuthorityResponse named = Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Device: f.OperatorDevice.Id, ExpectPlan: plan)));
        Assert.Equal("signature.self-admission", Assert.Single(named.Remaining).Code);

        // Still refused while the other self-admission is in the copy.
        (refused, problem) = api.Upgrade(new UpgradeRequest("0.9.2", f.Estate.OperatorId, false));
        Assert.Null(refused);
        Assert.Contains(problem!.Findings, x => x.Code == "signature.self-admission" && x.Location.StartsWith(stray.ExpectedPath, StringComparison.Ordinal));

        // The operator removes it, as the upgrade guide says; the upgrade then proceeds and keeps the first authority named.
        f.Remove(stray);
        (UpgradeResponse? applied, Problem? upgradeProblem) = api.Upgrade(new UpgradeRequest("0.9.2", f.Estate.OperatorId, false));
        Assert.True(upgradeProblem is null, JsonSerializer.Serialize(upgradeProblem, ProtocolJson.Options));
        Assert.True(applied!.Applied);
        EstateModel model = f.Load();
        Assert.Equal(first.Id, model.Manifest!.FirstAuthority);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASelfAdmissionWhoseSignatureDoesNotVerifyIsNeverNamed(bool statedEarlier)
    {
        // An admission of the root operator's real first device whose signature names that device but was made with an unrelated key:
        // anyone able to add a file can write one, stated later than the genuine self-admission or earlier.
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-7"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = FirstAuthority(f);
        string keys = Path.Combine(Path.GetTempPath(), "vericairn-first-authority-forged-" + Records.Identity.Ulid.NewUlid().Value);
        try
        {
            var store = new FileKeyStore(keys);
            string unrelated = Records.Identity.Ulid.NewUlid().Value;
            store.Create(unrelated);
            DateTimeOffset resume = f.Estate.Clock;
            f.Estate.SetClock(statedEarlier ? Early : resume.AddDays(30));
            RecordEnvelope forged = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "The first authority."), f.OperatorDevice.Reference,
                signer: new StoreSigner(store, unrelated, f.OperatorDevice.Id));
            f.Estate.SetClock(resume);
            using EstateService api = EstateService.Open(f.Root);
            byte[] before = Manifest(f);

            FirstAuthorityResponse listed = Ok(api.FirstAuthority(new FirstAuthorityRequest()));
            FirstAuthorityCandidate bad = listed.Candidates.Single(c => c.Admission == forged.Id);
            FirstAuthorityCandidate good = listed.Candidates.Single(c => c.Admission == first.Id);
            Assert.False(bad.SignatureVerifies);
            Assert.StartsWith("signature.invalid: ", bad.SignatureProblem, StringComparison.Ordinal);
            Assert.False(bad.Nameable);
            Assert.Contains("signature does not verify", bad.NotNameable, StringComparison.Ordinal);
            Assert.True(good.SignatureVerifies);
            Assert.Null(good.SignatureProblem);
            Assert.True(good.Nameable);

            string refusal = Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: forged.Id, Check: true)));
            Assert.Contains("signature does not verify", refusal, StringComparison.Ordinal);
            Assert.Contains("signature.invalid", refusal, StringComparison.Ordinal);
            Assert.Contains("name one with --admission", Refused(api.FirstAuthority(new FirstAuthorityRequest(Device: f.OperatorDevice.Id, Check: true))), StringComparison.Ordinal);
            Assert.Equal(before, Manifest(f));

            // The genuine one is named; nothing remains on it, and the estate reads it with the operator's records counting.
            string plan = Ok(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))).PlanHash!;
            FirstAuthorityResponse named = Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan)));
            Assert.DoesNotContain(named.Remaining, x => x.Location.StartsWith(first.ExpectedPath, StringComparison.Ordinal));
            EstateModel model = f.Load();
            Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
            Assert.True(model.Counts(model.ById[real.Id]));
            Assert.Contains(model.Findings, x => x.Code == "signature.invalid" && x.Location.StartsWith(forged.ExpectedPath, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
        }
    }

    [Theory]
    [InlineData("unknown-key")]
    [InlineData("duplicate-first-authority")]
    [InlineData("not-json")]
    [InlineData("not-canonical")]
    public void AManifestThatDoesNotReadCleanlyListsAndNamesNothing(string how)
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = FirstAuthority(f);
        (_, RecordEnvelope stray) = Earlier(f);
        // A charter with a lower id than the estate's, its own operator and two self-admissions of that operator's devices: what a model
        // read without the manifest would fall back to.
        const string forgedOperator = "01ARZ3NDEKTSV4RRFFQ69G5FA0";
        f.Estate.Add("principal", forgedOperator, """{"kind":"human","name":"Another Operator","roles":["operator"]}""", id: forgedOperator);
        f.Estate.Add("estate", forgedOperator, $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Another root.","operator":"{{{forgedOperator}}}","recordFormat":3,"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""", id: "01ARZ3NDEKTSV4RRFFQ69G5FA1");
        RecordEnvelope d1 = f.Device(forgedOperator, "Another one"), d2 = f.Device(forgedOperator, "Another two");
        RecordEnvelope a1 = f.Estate.Add("admission", forgedOperator, HistoryFixture.Admission(forgedOperator, "Itself."), d1.Reference, signer: f.Signer(d1));
        f.Estate.Add("admission", forgedOperator, HistoryFixture.Admission(forgedOperator, "Itself."), d2.Reference, signer: f.Signer(d2));
        // The service is open before the manifest changes, as a running daemon is.
        using EstateService api = EstateService.Open(f.Root);
        Assert.Equal(f.Estate.OperatorId, Ok(api.FirstAuthority(new FirstAuthorityRequest())).RootOperator);
        string plan = Ok(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))).PlanHash!;
        string path = Path.Combine(f.Root, EstateLayout.ManifestPath);
        string text = File.ReadAllText(path);
        int brace = text.IndexOf('{', StringComparison.Ordinal) + 1;
        text = how switch
        {
            "unknown-key" => text[..brace] + "\n  \"note\": \"x\"," + text[brace..],
            "duplicate-first-authority" => text[..brace] + "\n  \"firstAuthority\": \"" + stray.Id + "\",\n  \"firstAuthority\": \"" + first.Id + "\"," + text[brace..],
            "not-json" => text + "}}",
            _ => text + "\n",
        };
        File.WriteAllText(path, text);
        byte[] before = File.ReadAllBytes(path);

        Assert.Contains("does not read cleanly", Refused(api.FirstAuthority(new FirstAuthorityRequest())), StringComparison.Ordinal);
        Assert.Contains("does not read cleanly", Refused(api.FirstAuthority(new FirstAuthorityRequest(Admission: a1.Id, Check: true))), StringComparison.Ordinal);
        Assert.Contains("does not read cleanly", Refused(api.FirstAuthority(new FirstAuthorityRequest(forgedOperator, Admission: a1.Id, ExpectPlan: plan))), StringComparison.Ordinal);
        Assert.Contains("does not read cleanly", Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan))), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ThePlanBindsEveryCandidatesEvidence()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = FirstAuthority(f);
        (RecordEnvelope device, RecordEnvelope stray) = Earlier(f, order: false);
        using EstateService api = EstateService.Open(f.Root);
        string Plan() => Ok(api.FirstAuthority(new FirstAuthorityRequest(Admission: first.Id, Check: true))).PlanHash!;
        string Stale(string plan) => Refused(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan)));

        // The stray device admits another device: its admits changes.
        string plan = Plan();
        RecordEnvelope other = f.Device(f.Estate.OperatorId, "Resting on the stray");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "By the stray."), other.Reference, signer: f.Signer(device));
        Assert.Equal(1, Ok(api.FirstAuthority(new FirstAuthorityRequest())).Candidates.Single(c => c.Admission == stray.Id).Admits);
        Assert.Contains("no longer reads as the check", Stale(plan), StringComparison.Ordinal);

        // It signs a record: its recordsSigned changes.
        plan = Plan();
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-8"), signer: f.Signer(device));
        Assert.Contains("no longer reads as the check", Stale(plan), StringComparison.Ordinal);

        // It is revoked, read as the first authority: its revoked changes.
        plan = Plan();
        RecordEnvelope strayAdmission = f.Load().ById[stray.Id];
        f.Revoke(device, strayAdmission, f.Signer(other));
        Assert.True(Ok(api.FirstAuthority(new FirstAuthorityRequest())).Candidates.Single(c => c.Admission == stray.Id).Revoked);
        Assert.Contains("no longer reads as the check", Stale(plan), StringComparison.Ordinal);

        // A fresh check's plan is taken.
        plan = Plan();
        Assert.True(Ok(api.FirstAuthority(new FirstAuthorityRequest(f.Estate.OperatorId, Admission: first.Id, ExpectPlan: plan))).Applied);
    }

    /// <summary>The estate's only self-admission so far, the root operator's first device's.</summary>
    private static RecordEnvelope FirstAuthority(HistoryFixture f) => SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;

    /// <summary>A device of the root operator, admitting itself, dated before the estate began, and a work order it signs unless asked not to.</summary>
    private static (RecordEnvelope Device, RecordEnvelope Admission) Earlier(HistoryFixture f, bool order = true)
    {
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(Early);
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Earlier device");
        RecordEnvelope admission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: f.Signer(device));
        f.Estate.SetClock(resume);
        if (order) f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CFA-E" + device.Id[^4..], "Signed by the earlier device"), signer: f.Signer(device));
        return (device, admission);
    }

    private static byte[] Manifest(HistoryFixture f) => File.ReadAllBytes(Path.Combine(f.Root, EstateLayout.ManifestPath));

    private static FirstAuthorityResponse Ok((FirstAuthorityResponse? Response, Problem? Problem) result)
    {
        Assert.True(result.Problem is null, result.Problem?.Message);
        return result.Response!;
    }

    private static string Refused((FirstAuthorityResponse? Response, Problem? Problem) result)
    {
        Assert.Null(result.Response);
        Assert.Equal("refused", result.Problem!.Code);
        return result.Problem.Message;
    }

    /// <summary>A published release of the version given, recorded in the estate, which an upgrade reads without a network.</summary>
    private static void Publish(HistoryFixture f, string version)
    {
        string op = f.Estate.OperatorId;
        RecordEnvelope product = f.Estate.Add("product", op, new JsonObject
        {
            ["name"] = "Example", ["slug"] = "example", ["summary"] = "Test upgrade product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Test edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Test surface." }),
        }.ToJsonString(), signer: f.OperatorSigner);
        JsonObject body = new()
        {
            ["version"] = version, ["repository"] = "Example/example", ["commit"] = new string('1', 40), ["status"] = "candidate",
            ["artifacts"] = new JsonArray(new JsonObject { ["name"] = EstateUpgrade.DistributableName(version, "linux-x64"), ["hash"] = "sha256:" + new string('1', 64), ["bytes"] = 10, ["platform"] = "linux-x64" }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(),
            ["signing"] = "unsigned test artifact", ["limitations"] = new JsonArray(),
        };
        RecordEnvelope candidate = f.Estate.Add("release", op, body.ToJsonString(), subject: new RecordReference("product", product.Id, null), signer: f.OperatorSigner);
        f.Estate.Add("acceptance", op, new JsonObject { ["statement"] = "Test release accepted.", ["basis"] = new JsonArray(), ["self"] = true }.ToJsonString(), subject: candidate.Reference, signer: f.OperatorSigner);
        body["status"] = "published";
        body["publishedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        body["url"] = "https://example.invalid/releases/" + version;
        f.Estate.Add("release", op, body.ToJsonString(), subject: candidate.Subject, supersedes: candidate.Id, signer: f.OperatorSigner);
    }
}
