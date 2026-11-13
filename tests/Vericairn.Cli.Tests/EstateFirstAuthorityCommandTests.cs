// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// <c>vericairn estate first-authority</c> (CODE-72): the listing with each candidate's evidence, the check that shows the change and the
/// plan hash, and the write by the root operator with that hash, as text and as JSON, with the other self-admission left for the operator
/// to remove.
/// </summary>
[Collection("cli")]
public sealed class EstateFirstAuthorityCommandTests
{
    [Fact]
    public void TheCommandListsChecksAndNamesTheFirstAuthority()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Stray device");
        RecordEnvelope stray = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: f.Signer(device));
        string op = f.Estate.OperatorId;

        CliHarness.Outcome listed = CliHarness.Ok("--estate", f.Root, "estate", "first-authority");
        Assert.Contains($"root operator     {op}", listed.Out, StringComparison.Ordinal);
        Assert.Contains("manifest names    no first authority", listed.Out, StringComparison.Ordinal);
        Assert.Contains("first authority   none", listed.Out, StringComparison.Ordinal);
        Assert.Contains("error: manifest.first-authority estate/manifest.json: ", listed.Out, StringComparison.Ordinal);
        Assert.Contains($"  {first.Id}  device {f.OperatorDevice.Id} \"Test signing device\"  linux, file-owner-only, presence none  principal {op}", listed.Out, StringComparison.Ordinal);
        Assert.Contains($"  {stray.Id}  device {device.Id} \"Stray device\"", listed.Out, StringComparison.Ordinal);
        Assert.Contains("(a claim, not evidence of order)", listed.Out, StringComparison.Ordinal);
        Assert.Contains("admits 1 other device; its device signed", listed.Out, StringComparison.Ordinal);
        Assert.Contains("    nameable", listed.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("plan:", listed.Out, StringComparison.Ordinal);

        CliHarness.Outcome check = CliHarness.Ok("--estate", f.Root, "estate", "first-authority", "--device", f.OperatorDevice.Id, "--check");
        Assert.Contains($"change: estate/manifest.json firstAuthority (none) -> {first.Id}", check.Out, StringComparison.Ordinal);
        string plan = check.Out.Split('\n').Single(l => l.StartsWith("plan: ", StringComparison.Ordinal))["plan: ".Length..].Trim();
        Assert.Contains($"Nothing was written. The root operator writes it with vericairn estate first-authority --device {f.OperatorDevice.Id} --expect-plan <plan> --by <root operator>.", check.Out, StringComparison.Ordinal);
        Assert.Null(f.Load().Manifest!.FirstAuthority);

        // Writing asks for the check's plan and the root operator.
        CliHarness.Outcome notChecked = CliHarness.Run("--estate", f.Root, "estate", "first-authority", "--device", f.OperatorDevice.Id, "--by", op);
        Assert.NotEqual(0, notChecked.Code);
        Assert.Contains("read the change with --check first", notChecked.Err, StringComparison.Ordinal);
        CliHarness.Outcome byAgent = CliHarness.Run("--estate", f.Root, "estate", "first-authority", "--device", f.OperatorDevice.Id, "--expect-plan", plan, "--by", f.Estate.BuilderId);
        Assert.NotEqual(0, byAgent.Code);
        Assert.Contains("the root operator's act", byAgent.Err, StringComparison.Ordinal);

        CliHarness.Outcome written = CliHarness.Ok("--estate", f.Root, "--json", "estate", "first-authority", "--device", f.OperatorDevice.Id, "--expect-plan", plan, "--by", op);
        FirstAuthorityResponse response = written.Json.Deserialize<FirstAuthorityResponse>(ProtocolJson.Options)!;
        Assert.True(response.Applied);
        Assert.Equal(first.Id, response.FirstAuthority);
        Assert.Equal(first.Id, f.Load().Manifest!.FirstAuthority);
        FindingDto remaining = Assert.Single(response.Remaining);
        Assert.Equal("signature.self-admission", remaining.Code);

        // The listing now reads the named one, and the estate check reports the other self-admission until the operator removes it.
        CliHarness.Outcome after = CliHarness.Ok("--estate", f.Root, "estate", "first-authority");
        Assert.Contains($"first authority   {first.Id}", after.Out, StringComparison.Ordinal);
        Assert.Contains("not nameable: ", after.Out, StringComparison.Ordinal);
        Assert.Equal(1, CliHarness.Run("--estate", f.Root, "estate", "check").Code);
        f.Remove(stray);
        Assert.Equal(0, CliHarness.Run("--estate", f.Root, "estate", "check").Code);
    }

    [Fact]
    public void AWriteTellsTheOperatorToRemoveTheOtherSelfAdmissions()
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Stray device");
        RecordEnvelope stray = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: f.Signer(device));
        CliHarness.Outcome check = CliHarness.Ok("--estate", f.Root, "--json", "estate", "first-authority", "--admission", first.Id, "--check");
        string plan = check.Json.GetProperty("planHash").GetString()!;
        CliHarness.Outcome written = CliHarness.Ok("--estate", f.Root, "estate", "first-authority", "--admission", first.Id, "--expect-plan", plan, "--by", f.Estate.OperatorId);
        Assert.Contains($"changed: estate/manifest.json firstAuthority (none) -> {first.Id}", written.Out, StringComparison.Ordinal);
        Assert.Contains($"remaining error: signature.self-admission {stray.ExpectedPath}#signatures: ", written.Out, StringComparison.Ordinal);
        Assert.Contains("Remove its record file yourself, as the operator, and commit; this command removes nothing.", written.Out, StringComparison.Ordinal);
        Assert.Contains("Commit estate/manifest.json with the estate's records", written.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("plan:", written.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(f.Root, stray.ExpectedPath)));
        Assert.Equal(JsonValueKind.Object, check.Json.GetProperty("change").ValueKind);
    }
}
