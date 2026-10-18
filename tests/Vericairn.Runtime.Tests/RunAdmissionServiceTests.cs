// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class RunAdmissionServiceTests
{
    [Fact]
    public void APreparedIntentDoesNotAuthorizeDispatchAfterConfigurationChanges()
    {
        using var run = new RunEstate();
        var gate = new object();
        RunAdmissionService admission = Admission(run, gate);
        RunAdmissionService.AdmittedRun admitted = Prepare(run, admission);
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal)
            {
                [admitted.Launcher.Launcher] = admitted.Configuration with { Name = "changed-configuration" },
            },
        };

        bool launched = false;
        ProviderRefusedException refusal = Assert.Throws<ProviderRefusedException>(() => admission.Launch(admitted, _ =>
        {
            launched = true;
            throw new InvalidOperationException("The launch boundary must not be reached.");
        }, out _, out _));

        Assert.Contains("changed after the intent", refusal.Message, StringComparison.Ordinal);
        Assert.False(launched);
        Assert.False(Monitor.IsEntered(gate));
        Assert.Equal("not-started", run.Row(admitted.Run).Status);
        Assert.False(File.Exists(run.Report("admission")));
    }

    [Fact]
    public void TheLaunchCallbackRunsUnderTheSameGateThatAdmittedTheIntent()
    {
        using var run = new RunEstate();
        var gate = new object();
        RunAdmissionService admission = Admission(run, gate);
        RunAdmissionService.AdmittedRun admitted = Prepare(run, admission);

        InvalidOperationException inspected = Assert.Throws<InvalidOperationException>(() => admission.Launch(admitted, command =>
        {
            Assert.True(Monitor.IsEntered(gate));
            Assert.NotEmpty(command);
            Assert.Equal(admitted.Intent.Id, run.Row(admitted.Run).Run);
            throw new InvalidOperationException("Launch boundary inspected.");
        }, out _, out _));

        Assert.Equal("Launch boundary inspected.", inspected.Message);
        Assert.False(Monitor.IsEntered(gate));
        Assert.Equal("not-started", run.Row(admitted.Run).Status);
        Assert.False(File.Exists(run.Report("admission")));
    }

    private static RunAdmissionService.AdmittedRun Prepare(RunEstate run, RunAdmissionService admission)
    {
        var request = new RunStartRequest(run.Assignment, "generic", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("generic", run.Report("admission")) });
        RunResponse? refusal = admission.Prepare(request, null, out RunAdmissionService.AdmittedRun? admitted, out RecordWritten? assigned);
        Assert.Null(refusal);
        Assert.Null(assigned);
        Assert.NotNull(admitted);
        Assert.Equal("not-started", run.Row(admitted.Run).Status);
        Assert.False(File.Exists(run.Report("admission")));
        return admitted;
    }

    // Compose admission on its own. Only generic records and context assembly use the fixture's facade; no run API is used
    // to prepare or launch. The supplied record boundary checks that intent publication retains the admission gate.
    private static RunAdmissionService Admission(RunEstate run, object gate)
    {
        var records = new EstateRecordAccess(gate, run.Service.Session,
            (draft, _) =>
            {
                Assert.True(Monitor.IsEntered(gate));
                string? subject = draft.Subject is null ? null : $"{draft.Subject.Type}:{draft.Subject.Id}:{draft.Subject.Hash}";
                return run.Service.NewRecord(new NewRecordRequest(draft.Type, draft.By, draft.Body, subject, draft.Supersedes,
                    draft.At is DateTimeOffset at ? RecordParser.FormatTimestamp(at) : null, draft.Id));
            },
            (_, _) => throw new InvalidOperationException("No pending assignment is carried by this test."),
            (_, _, _) => throw new InvalidOperationException("No batch is carried by this test."),
            carried =>
            {
                Assert.True(Monitor.IsEntered(gate));
                Assert.Empty(carried);
                return run.Service.Session.Load();
            },
            _ => null);
        return new RunAdmissionService(records, () => run.Service.Providers, () => run.Service.WorkerOptions,
            (_, _, order, maxBytes, recipient, assignment) => run.Service.BriefContext(order.Id, maxBytes, recipient, assignment)!);
    }
}
