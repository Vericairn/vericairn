// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Workbench.Tests;

/// <summary>OPS-24 (0.9.4): Workbench offers the same reviewed list as vericairn operator act --ready, and signs only what the operator chose.</summary>
[Collection("workbench")]
public class OperatorActSurfaceTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task TheReviewedListSignsTheChosenItemsAndLeavesTheRestWaiting()
    {
        string root = Seed.Temp("wb-operator-act");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-operator-act-prefs"))) { ActingOverride = person };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
            // The operator's own admitted device on this machine signs the act; no record of an act is unsigned.
            WriteOutcome device = shell.Api!.DeviceCreate(new(person, "Operator test device", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            Assert.True(shell.Api.DeviceAdmit(new(device.Written!.Id, person, "Admit the test device.")).Ok);
            string first = Ask(shell, person, "Ship it?", "Ship it now.");
            string second = Ask(shell, person, "Wait on it?", "Wait.");
            OperatorActDocument page = Assert.IsType<OperatorActDocument>(await fixture.UiAsync(() => shell.OpenOperatorActAsync(TestContext.Current.CancellationToken)));
            Assert.Same(page, shell.SelectedDocument);
            Assert.Equal(person, page.By);
            Assert.Equal(["answer:" + first, "answer:" + second], page.Rows.Select(r => r.Item.Id));
            Assert.Equal(["sign", "wait"], page.Rows[0].Choices);
            Assert.Contains("Recommendation sha256:", page.Rows[0].Label, StringComparison.Ordinal);
            Assert.False(page.CanSign);
            await fixture.Ui(() =>
            {
                page.Rows[1].Choice = "wait";
                Assert.Contains("signs 1 and dismisses 0 of 2 ready items", page.Review, StringComparison.Ordinal);
                page.Reviewed = true;
                Assert.True(page.CanSign);
            });

            await fixture.UiAsync(() => shell.SignOperatorActAsync(page, TestContext.Current.CancellationToken));

            Assert.True(page.Note.StartsWith("Act ", StringComparison.Ordinal), page.Note);
            RecordResponse decision = Assert.Single(shell.Api!.Records("decision"));
            JsonElement act = decision.Envelope.GetProperty("body").GetProperty("act");
            Assert.Equal(first, act.GetProperty("ask").GetString());
            Assert.Equal(["answer:" + second], page.Rows.Select(r => r.Item.Id));
            Assert.False(page.Reviewed);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void AnAcceptanceStartsWaitingUntilTheOperatorSelectsItAndShowsItsReviews()
    {
        var review = new OperatorActReview("01M7ZZZZZZZZZZZZZZZZZZZZZ1", "sha256:" + new string('b', 64), "01M7ZZZZZZZZZZZZZZZZZZZZZ2", "pass", "independent-agent", "sha256:" + new string('a', 64));
        var acceptance = new OperatorActItem("close:STEP-1", "close", "op", "STEP-1 result", "an acceptance", "acceptance", "result",
            new("01M7ZZZZZZZZZZZZZZZZZZZZZ3", "sha256:" + new string('a', 64)), [new(review.Id, review.Hash)], false, false, Reviews: [review]);
        var answer = acceptance with { Id = "answer:x", Kind = "answer", RecordType = "decision", Reviews = null };
        var row = new OperatorActRow(acceptance, () => { });

        Assert.Equal("wait", row.Choice);
        Assert.Equal("sign", new OperatorActRow(answer, () => { }).Choice);
        Assert.Contains($"Review {review.Id} at {review.Hash} by {review.By}: pass, independent-agent, of {review.SubjectHash}", row.Label, StringComparison.Ordinal);
        row.Choice = "sign";
        Assert.Equal("sign", row.Choice);
    }

    private static string Ask(ShellViewModel shell, string person, string question, string recommendation)
    {
        WriteOutcome written = shell.Api!.NewRecord(new NewRecordRequest("ask", person,
            JsonSerializer.SerializeToElement(new { to = person, question, recommendation, consequence = "The work waits.", state = "open" })));
        Assert.True(written.Ok, written.Problem?.Message);
        return written.Written!.Id;
    }
}
