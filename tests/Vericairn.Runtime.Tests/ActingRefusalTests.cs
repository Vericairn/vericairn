// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.TestSupport;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (FIX-159): the one refusal every acting surface calls before it acts. While the estate reports a duplicate identifier, release
/// submission, every record a device signs on this machine or is prepared for a device to sign, run admission and launch, and a standing agent's
/// goal step refuse, naming the finding, before any presence prompt, signature, checkpoint, workspace or provider call; while it reports another
/// error the surfaces with effects outside the records refuse as well, and a record is judged by the write boundary as before. An estate that
/// reports no error acts as before.
/// </summary>
[Collection("home")]
public sealed class ActingRefusalTests
{
    private const string ProductBody = """
        {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],
         "release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/widget"}}}}
        """;

    private static readonly DateTimeOffset EarlierMonth = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static string Decision(string by) => $$"""{"title":"A decision","statement":"Nothing.","summary":"Nothing.","synthesis":"Nothing.","effects":["nothing"],"affects":[],"decidedBy":"{{by}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["spending"]}""";

    /// <summary>
    /// A release-capable estate whose operator signs with an admitted device on this machine, with the stand-in release provider; at record
    /// format 2, as the release dispatch tests' estate, unless another is named.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture(long format = 2)
        {
            Estate = new TestEstate(format);
            Product = Estate.Add("product", Estate.OperatorId, ProductBody);
            Request = new ReleaseSubmitRequest(Product.Id, "1.0.0", "Example/widget", Estate.OperatorId);
            Service = EstateService.Open(Estate.Root);
            var registry = new ProviderRegistry();
            registry.Register(Provider);
            Service.Providers = registry;
            Device = RunEstate.Ok(Service.DeviceCreate(new DeviceCreateRequest(Estate.OperatorId, "Operator's laptop", "file"))).Id;
            RunEstate.Ok(Service.DeviceAdmit(new AdmitRequest(Device, Estate.OperatorId, "The first authority.")));
            Signatures = 0;
            Service.BeforeSigningRecord = () => Signatures++;
        }

        public TestEstate Estate { get; }

        public FakeReleaseProvider Provider { get; } = new();

        public EstateService Service { get; }

        public RecordEnvelope Product { get; }

        public ReleaseSubmitRequest Request { get; }

        public string Device { get; }

        public int Signatures { get; private set; }

        public string PathOf(RecordEnvelope record) => Path.Combine(Estate.Root, record.ExpectedPath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>A second record carrying a record's identifier, written by hand in an earlier month.</summary>
        public RecordEnvelope Copy(RecordEnvelope original)
        {
            DateTimeOffset now = Estate.Clock;
            Estate.SetClock(EarlierMonth);
            RecordEnvelope copy = Estate.Add("decision", Estate.BuilderId, Decision(Estate.OperatorId), id: original.Id);
            Estate.SetClock(now);
            return copy;
        }

        public void Dispose()
        {
            Service.Dispose();
            var store = new Records.Signing.FileKeyStore(Records.Signing.VericairnHome.Path());
            if (store.Has(Device)) store.Remove(Device);
            Estate.Dispose();
        }
    }

    private static NewRecordRequest DecisionRequest(Fixture f) => new("decision", f.Estate.OperatorId, JsonSerializer.SerializeToElement(JsonNode.Parse(Decision(f.Estate.OperatorId))));

    [Fact]
    public void NoReleaseIsSubmittedAndNoRecordSignedWhileTheEstateLeavesARecordUnread()
    {
        using var f = new Fixture();
        RecordEnvelope copy = f.Copy(f.Product);
        int operations = f.Estate.Load().OfType("operation").Count();

        // Release submission: refused before any presence prompt, checkpoint, intent or provider call, naming the duplicate.
        ReleaseResponse submitted = f.Service.ReleaseSubmit(f.Request);
        Assert.False(submitted.Ok);
        Assert.Equal("refused", submitted.Problem!.Code);
        Assert.Contains(submitted.Problem.Findings, x => x.Code == "record.duplicate-id" && x.Severity == "error");
        Assert.Contains("record.duplicate-id", submitted.Message, StringComparison.Ordinal);
        Assert.Empty(f.Provider.Dispatches);
        Assert.Equal(operations, f.Estate.Load().OfType("operation").Count());

        // Signing: a record and a revocation are each refused before the device signs anything.
        WriteOutcome written = f.Service.NewRecord(DecisionRequest(f));
        Assert.False(written.Ok);
        Assert.Contains(written.Problem!.Findings, x => x.Code == "record.duplicate-id");
        WriteOutcome revoked = f.Service.DeviceRevoke(new RevokeRequest(f.Device, f.Estate.OperatorId, "Lost."));
        Assert.False(revoked.Ok);
        Assert.Contains(revoked.Problem!.Findings, x => x.Code == "record.duplicate-id");
        Assert.Equal(0, f.Signatures);

        // Removing the copy: the record is signed and the release goes, as before.
        File.Delete(f.PathOf(copy));
        RunEstate.Ok(f.Service.NewRecord(DecisionRequest(f)));
        Assert.Equal(1, f.Signatures);
        ReleaseResponse released = f.Service.ReleaseSubmit(f.Request);
        Assert.True(released.Ok, released.Message);
        Assert.Single(f.Provider.Dispatches);
    }

    [Fact]
    public void NoHistoryCheckpointIsSignedWhileTheEstateLeavesARecordUnread()
    {
        using var f = new Fixture(format: 3);
        RecordEnvelope copy = f.Copy(f.Product);
        (HistoryCheckpointResponse? checkpoint, Problem? refused) = f.Service.HistoryCheckpoint(new HistoryCheckpointRequest(f.Estate.OperatorId, Adopt: true));
        Assert.Null(checkpoint);
        Assert.Contains(refused!.Findings, x => x.Code == "record.duplicate-id");
        Assert.Equal(0, f.Signatures);
        Assert.Empty(f.Estate.Load().OfType("history-checkpoint"));

        File.Delete(f.PathOf(copy));
        string plan = f.Service.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!.PlanHash!;
        (checkpoint, refused) = f.Service.HistoryCheckpoint(new HistoryCheckpointRequest(f.Estate.OperatorId, Adopt: true, ExpectPlan: plan));
        Assert.True(checkpoint?.Written is not null, refused?.Message);
        Assert.Equal(1, f.Signatures);
    }

    [Fact]
    public void NoReleaseIsSubmittedWhileTheEstateReportsAnyOtherErrorAndARecordIsJudgedAsBefore()
    {
        using var f = new Fixture();
        // A decision naming a subject that does not exist, written by hand: an error that is no duplicate.
        RecordEnvelope broken = f.Estate.Add("decision", f.Estate.OperatorId, Decision(f.Estate.OperatorId), new RecordReference("decision", Records.Identity.Ulid.NewUlid().Value, null));
        Assert.Contains(f.Estate.Load().Findings, x => x.Code == "record.subject" && x.Severity == Severity.Error);

        ReleaseResponse submitted = f.Service.ReleaseSubmit(f.Request);
        Assert.False(submitted.Ok);
        Assert.Equal("refused", submitted.Problem!.Code);
        Assert.Contains(submitted.Problem.Findings, x => x.Code == "record.subject");
        Assert.Empty(f.Provider.Dispatches);

        // A record is judged by the write boundary with the estate as it would be, as before: signed, then refused while the error stands.
        WriteOutcome written = f.Service.NewRecord(DecisionRequest(f));
        Assert.False(written.Ok);
        Assert.Equal("rejected", written.Problem!.Code);
        Assert.Equal(1, f.Signatures);

        File.Delete(f.PathOf(broken));
        ReleaseResponse released = f.Service.ReleaseSubmit(f.Request);
        Assert.True(released.Ok, released.Message);
        Assert.Single(f.Provider.Dispatches);
    }

    [Fact]
    public void NoRunIsAdmittedOrLaunchedWhileTheEstateLeavesARecordUnread()
    {
        using var run = new RunEstate();
        RecordEnvelope order = Assert.Single(run.Estate.Load().Current("work-order"));
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        run.Estate.SetClock(now.AddMonths(-1));
        RecordEnvelope copy = run.Estate.Add("decision", run.Estate.BuilderId, Decision(run.Estate.OperatorId), id: order.Id);
        run.Estate.SetClock(now);

        // Admission: refused before any workspace, setup, probe or record.
        int operations = run.Estate.Load().OfType("operation").Count();
        RunResponse refused = run.Start("generic", "generic", run.Report("refused"));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, x => x.Code == "record.duplicate-id");
        Assert.Equal(operations, run.Estate.Load().OfType("operation").Count());
        Assert.False(File.Exists(run.Report("refused")));
        File.Delete(Path.Combine(run.Estate.Root, copy.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));

        // Launch: an intent admitted before the copy was written launches nothing once it stands.
        var gate = new object();
        RunAdmissionService admission = RunAdmissionServiceTests.Admission(run, gate);
        RunAdmissionService.AdmittedRun admitted = RunAdmissionServiceTests.Prepare(run, admission);
        run.Estate.SetClock(now.AddMonths(-1));
        copy = run.Estate.Add("decision", run.Estate.BuilderId, Decision(run.Estate.OperatorId), id: order.Id);
        run.Estate.SetClock(now);
        bool launched = false;
        ProviderRefusedException stopped = Assert.Throws<ProviderRefusedException>(() => admission.Launch(admitted, _ =>
        {
            launched = true;
            throw new InvalidOperationException("The launch boundary must not be reached.");
        }, out _, out _));
        Assert.Contains("record.duplicate-id", stopped.Message, StringComparison.Ordinal);
        Assert.False(launched);
        Assert.False(File.Exists(run.Report("admission")));
    }
}
