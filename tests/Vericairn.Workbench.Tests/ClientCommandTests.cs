// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class ClientCommandTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task ARefusedEffectLeavesTheEditorAvailableAndDoesNotAddUncertainty()
    {
        string root = Seed.Temp("command-refusal");
        string person = Seed.Init(root);
        using var connection = new ControlledConnection(root);
        (ShellViewModel shell, AssignmentComposer page) = await CreateComposerAsync(connection, person);
        connection.Control.Refused = true;
        await fixture.UiAsync(() => shell.SubmitAssignmentAsync(page, TestContext.Current.CancellationToken));
        Assert.Equal("refused", shell.RequestState);
        Assert.Equal("The runtime refuses this write and writes nothing.", shell.Status);
        Assert.False(shell.IsBusy);
        Assert.False(shell.HasUncertainOutcomes);
        Assert.Null(page.UncertainEffect);
        Assert.Equal(0, connection.Control.AssignCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowRefreshLeavesInputAndCancellationResponsiveAndIgnoresALateResponse(bool timeout)
    {
        string root = Seed.Temp("command-refresh");
        Seed.Init(root);
        using var connection = new ControlledConnection(root);
        var shell = new ShellViewModel(_ => connection, new FakeRuntimeManager(), new Preferences(Seed.Temp("command-preferences")));
        MainWindow window = await fixture.Ui(() => { var value = new MainWindow(shell); value.Show(); return value; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, TestContext.Current.CancellationToken));
            var uiChanges = new List<bool>();
            await fixture.Ui(() => shell.PropertyChanged += (_, _) => uiChanges.Add(Dispatcher.UIThread.CheckAccess()));
            connection.Control.StallStatus = true;
            connection.Control.Timeout = timeout ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
            await fixture.UiAsync(async () =>
            {
                Task request = shell.RefreshAsync(TestContext.Current.CancellationToken);
                Assert.False(request.IsCompleted, shell.Status + "/" + shell.RequestState);
                await connection.Control.Entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                Assert.False(request.IsCompleted);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Assert.True(shell.IsBusy);
                    Assert.Equal("busy", shell.RequestState);
                    Assert.False(shell.RefreshCommand.CanExecute(null));
                    Assert.True(shell.CancelCommand.CanExecute(null));
                    window.Focus();
                    window.KeyPress(Key.L, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.L, null);
                    Assert.False(shell.LeftVisible);
                    if (!timeout) shell.CancelCurrentRequest();
                    else connection.Control.Clock.Elapse(connection.Control.Timeout);
                });
                await request.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                Assert.Equal(timeout ? "timed-out" : "cancelled", shell.RequestState);
                Assert.Equal(timeout ? "The request timed out." : "The request was cancelled.", shell.Status);
                Assert.False(shell.IsBusy);
                Assert.False(shell.HasUncertainOutcomes);
                Assert.Equal("cached", shell.Freshness);
                string interrupted = shell.Status;
                connection.Control.Release.TrySetResult();
                await connection.Control.Finished.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                Assert.Equal(interrupted, shell.Status);
                connection.Control.StallStatus = false;
                await shell.RefreshAsync(TestContext.Current.CancellationToken);
                Assert.Equal("idle", shell.RequestState);
                Assert.Null(shell.LastFailure);
                Assert.All(uiChanges, onUi => Assert.True(onUi));
            });
        }
        finally
        {
            connection.Control.Release.TrySetResult();
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task AnUnavailableRuntimeIsAnIntendedCachedOutcome()
    {
        string root = Seed.Temp("command-unavailable");
        Seed.Init(root);
        using var connection = new ControlledConnection(root);
        var shell = new ShellViewModel(_ => connection, new FakeRuntimeManager(), new Preferences(Seed.Temp("command-preferences")));
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, TestContext.Current.CancellationToken));
        connection.Control.Unavailable = true;
        await fixture.UiAsync(() => shell.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal("unavailable", shell.RequestState);
        Assert.Equal("The runtime is unavailable.", shell.Status);
        Assert.Equal("Example Estate", shell.EstateName);
        Assert.Equal("cached", shell.Freshness);
        Assert.False(shell.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Journey", "D19-E")]
    public async Task AFailedEstateRefreshDiscardsCompanionContentAndCatalogBasisUntilAccessIsRechecked(bool invalidStatus)
    {
        string root = Seed.Temp("command-companion-cache");
        string person = Seed.Init(root);
        using var connection = new ControlledConnection(root);
        var shell = new ShellViewModel(_ => connection, new FakeRuntimeManager(), new Preferences(Seed.Temp("command-preferences")));
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, TestContext.Current.CancellationToken));
        await fixture.UiAsync(() => shell.OpenCompanionCatalogAsync(TestContext.Current.CancellationToken));
        CompanionCatalogDocument catalog = Assert.IsType<CompanionCatalogDocument>(shell.SelectedDocument);
        await fixture.UiAsync(() => shell.OpenCompanionAsync(person, TestContext.Current.CancellationToken));
        CompanionDocument item = Assert.IsType<CompanionDocument>(shell.SelectedDocument);
        Assert.NotEmpty(catalog.Rows);
        Assert.NotNull(catalog.Coverage.Snapshot);
        Assert.NotEmpty(item.Text);
        Assert.NotEmpty(item.SourceText);

        connection.Control.InvalidStatus = invalidStatus;
        connection.Control.Unavailable = !invalidStatus;
        await fixture.UiAsync(() => shell.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal("unavailable", shell.RequestState);
        Assert.Equal("unavailable", item.Currentness);
        Assert.Empty(item.Text);
        Assert.Empty(item.SourceText);
        Assert.Empty(item.PreviousText);
        Assert.Empty(item.Sources);
        Assert.Equal("unavailable", catalog.Coverage.Status);
        Assert.Null(catalog.Coverage.Snapshot);
        Assert.Null(catalog.Coverage.ManifestSnapshot);
        Assert.Empty(catalog.Coverage.Destinations);
        Assert.Empty(catalog.Rows);
        Assert.Null(catalog.Selected);

        connection.Control.InvalidStatus = connection.Control.Unavailable = false;
        await fixture.UiAsync(() => shell.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal("current", item.Currentness);
        Assert.Equal(person, item.Request.Revision);
        Assert.NotEmpty(item.SourceText);
        Assert.Equal("success", catalog.Coverage.Status);
        Assert.NotEmpty(catalog.Rows);
        Assert.NotNull(catalog.Coverage.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedEffectStaysUncertainAfterRefreshAndIsNotRetried(bool timeout)
    {
        string root = Seed.Temp("command-effect");
        string person = Seed.Init(root);
        using var connection = new ControlledConnection(root);
        IEstateApi api = connection.Api;
        (ShellViewModel shell, AssignmentComposer page) = await CreateComposerAsync(connection, person);
        connection.Control.StallAssign = true;
        connection.Control.Timeout = timeout ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMinutes(1);
        await fixture.UiAsync(async () =>
        {
            Task request = shell.SubmitAssignmentAsync(page, TestContext.Current.CancellationToken);
            Assert.False(request.IsCompleted, page.Status + "/" + shell.Status);
            try
            {
                await connection.Control.Entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                if (!timeout) shell.CancelCurrentRequest();
                else connection.Control.Clock.Elapse(connection.Control.Timeout);
                await request.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                Assert.True(shell.LastFailure?.OutcomeUnknown);
                Assert.True(shell.HasUncertainOutcomes);
                Assert.Null(page.WrittenId);
                Assert.False(page.CanSubmit);
                Assert.Contains("outcome is unknown", page.Status, StringComparison.Ordinal);
                Assert.Contains("outcome is unknown", shell.UncertaintyNotice, StringComparison.Ordinal);
                connection.Control.Release.TrySetResult();
                await connection.Control.Finished.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                await shell.RefreshAsync(TestContext.Current.CancellationToken);
                Assert.True(shell.HasUncertainOutcomes);
                await shell.SubmitAssignmentAsync(page, TestContext.Current.CancellationToken);
                Assert.Single(api.Records("assignment"));
                Assert.Equal(1, connection.Control.AssignCalls);
            }
            finally { connection.Control.Release.TrySetResult(); }
        });
    }

    [Fact]
    public async Task AConfirmedWriteKeepsItsReceiptWhenTheFollowingRefreshTimesOut()
    {
        string root = Seed.Temp("command-confirmed-effect");
        string person = Seed.Init(root);
        using var connection = new ControlledConnection(root);
        (ShellViewModel shell, AssignmentComposer page) = await CreateComposerAsync(connection, person);
        connection.Control.StallStatus = true;
        connection.Control.Timeout = TimeSpan.FromMilliseconds(100);
        try
        {
            await fixture.UiAsync(async () =>
            {
                Task request = shell.SubmitAssignmentAsync(page, TestContext.Current.CancellationToken);
                await connection.Control.Entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                Assert.False(request.IsCompleted);
                Assert.NotNull(page.WrittenId);
                connection.Control.Clock.Elapse(connection.Control.Timeout);
                await request.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            });
            Assert.Equal("timed-out", shell.RequestState);
            Assert.False(shell.LastFailure?.OutcomeUnknown);
            Assert.False(shell.HasUncertainOutcomes);
            Assert.NotNull(page.WrittenId);
            Assert.Null(page.UncertainEffect);
            Assert.StartsWith("Written: assignment " + page.WrittenId, page.Status, StringComparison.Ordinal);
            Assert.Equal(page.WrittenId, Assert.Single(connection.Api.Records("assignment")).Id);
        }
        finally { connection.Control.Release.TrySetResult(); }
    }

    private async Task<(ShellViewModel Shell, AssignmentComposer Page)> CreateComposerAsync(ControlledConnection connection, string person)
    {
        Seed.Admit(connection.Root, person, Seed.Temp("command-effect-inputs"), "Contributor", "agent", "contributor");
        IEstateApi api = connection.Api;
        Assert.True(api.NewRecord(new("work-order", person, Seed.Json("""{"key":"ASYNC","kind":"ordinary","title":"Observe an effect","intent":"Test.","scope":["test"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}"""))).Ok);
        var shell = new ShellViewModel(_ => connection, new FakeRuntimeManager(), new Preferences(Seed.Temp("command-preferences"))) { ActingOverride = person };
        await fixture.UiAsync(() => shell.OpenEstateAsync(connection.Root, TestContext.Current.CancellationToken));
        AssignmentComposer page = Assert.IsType<AssignmentComposer>(await fixture.UiAsync(() => shell.OpenComposerAsync("ASYNC", TestContext.Current.CancellationToken)));
        await fixture.Ui(() =>
        {
            page.Outcome = "Observe an uncertain response.";
            page.PermittedChanges = "test";
            page.ProhibitedEffects = "publication";
            page.Workspace = "test workspace";
            page.ExpectedOutputs = "result";
            page.ReviewCriteria = "observed";
            page.Recovery = "Reconcile the original assignment.";
        });
        return (shell, page);
    }

    private sealed class ControlledConnection : IEstateConnection
    {
        private readonly EstateService service;
        public ControlledConnection(string root)
        {
            Root = root;
            service = EstateService.Open(root);
            Api = DispatchProxy.Create<IEstateApi, ControlledApi>();
            Control.Target = service;
        }
        public ControlledApi Control => (ControlledApi)Api;
        public string Root { get; }
        public IEstateApi Api { get; }
        public string Mode => "daemon";
        public string Where => "test service";
        public void Refresh() { }
        public void Dispose() => service.Dispose();
    }

    public class ControlledApi : DispatchProxy
    {
        public IEstateApi Target { get; set; } = null!;
        public bool StallStatus { get; set; }
        public bool StallAssign { get; set; }
        public bool Unavailable { get; set; }
        public bool InvalidStatus { get; set; }
        public bool Refused { get; set; }
        public int AssignCalls { get; private set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        internal ClientClock Clock { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Refused && targetMethod!.Name == nameof(IEstateApi.AssignAsync))
                throw new ClientRefusedException("The runtime refuses this write and writes nothing.");
            if (targetMethod!.Name == nameof(IEstateApi.StatusAsync))
            {
                if (InvalidStatus) throw new InvalidOperationException("fixture status is unavailable");
                if (Unavailable) throw new HttpRequestException("fixture unavailable");
                if (StallStatus) return ClientOperation.RunAsync("Status", async token =>
                {
                    StatusResponse response = await Target.StatusAsync(token);
                    Entered.TrySetResult();
                    await Release.Task; // Deliberately non-cooperative: a late result must never mutate the shell.
                    Finished.TrySetResult();
                    return response;
                }, () => false, Timeout, Clock, (CancellationToken)args![^1]!);
            }
            if (targetMethod.Name == nameof(IEstateApi.AssignAsync) && StallAssign)
            {
                AssignCalls++;
                return ClientOperation.RunAsync("Assign", async token =>
                {
                    WriteOutcome result = await Target.AssignAsync((AssignRequest)args![0]!, token);
                    Assert.True(result.Ok, result.Problem?.Message);
                    Entered.TrySetResult();
                    await Release.Task;
                    Finished.TrySetResult();
                    return result;
                }, () => true, Timeout, Clock, (CancellationToken)args![^1]!);
            }
            try { return targetMethod.Invoke(Target, args); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
