// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public sealed class AttentionStateTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AcknowledgmentFromAnotherDeviceIsShownWithoutHidingItsSubject()
    {
        using var estate = new TestEstate(3);
        var subject = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""");
        var failure = estate.Add("verification", estate.BuilderId, """{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"failed","summary":"Inspect the check.","evidence":[],"limitations":[]}""", subject.Reference);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("attention-state-prefs"))) { ActingOverride = estate.OperatorId };
        await fixture.UiAsync(() => shell.OpenEstateAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken));
        AttentionNotice notice = Assert.Single(shell.Api!.AttentionGet(estate.OperatorId).Items, i => i.SubjectId == failure.Id);
        int records = estate.Load().Records.Count;
        Assert.True(shell.Api.AttentionAcknowledge(new(estate.OperatorId, notice.Kind, notice.SubjectType, notice.SubjectId, notice.Revision)).Ok);
        await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
        var item = Assert.Single(shell.Attention, a => a.SubjectId == failure.Id);
        Assert.Equal("acknowledged", item.AttentionState);
        Assert.Equal("failed", item.State);
        Assert.Contains(failure.ComputedHash, item.Why, StringComparison.Ordinal);
        Assert.Equal(records, estate.Load().Records.Count);
        shell.ActingOverride = estate.BuilderId;
        await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("unacknowledged", Assert.Single(shell.Attention, a => a.SubjectId == failure.Id).AttentionState);
    }
}
