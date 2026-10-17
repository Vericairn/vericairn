// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Vericairn.Presentation;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// Workbench's Goals workspace and the attention items of asks (the estate's design/goals.md § 8): a goal's objective, its conditions
/// with their current verdicts, its plan and steps with the principal each reaches, spend against its budget, its asks and its
/// evaluations, shown and never acted on; and each ask a delivery carries is a decision needed, in the state language, with its
/// recommendation first.
/// </summary>
[Collection("workbench")]
public class GoalsTests(HeadlessFixture fixture)
{
    static GoalsTests()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Seed.Temp("workbench-home"));
        }
    }

    [Fact]
    public async Task TheGoalsWorkspaceShowsAGoalAndItsAsksAreDecisionsNeededKeyboardOnly()
    {
        string root = Seed.Temp("wb-goals");
        string scratch = Seed.Temp("wb-goals-scratch");
        string op = Seed.Init(root);
        string builder = Seed.Id(Seed.Cli("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", Seed.Write(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}""")));
        string reviewer = Seed.Admit(root, op, scratch, "Reviewer", "agent", "reviewer");
        Seed.Cli("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", Seed.Write(scratch, "roster.json", $$"""{"roles":[{"role":"builder","principals":["{{builder}}"]},{"role":"publisher","principals":["{{op}}"]},{"role":"decider","principals":["{{op}}"]}]}"""));
        string goal = Seed.Id(Seed.Cli("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Release the guide", "--objective", "A published guide for the widget.", "--done", """{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}""", "--done", """{"evidence":"The guide describes the widget.","check":"Read it."}""", "--budget", "wallMinutes=60", "--budget", "attemptsPerStep=2"));
        Seed.Cli("--estate", root, "--json", "goal", "activate", goal, "--by", op);
        foreach ((string key, string role) in new[] { ("W-1", "builder"), ("W-2", "publisher") })
        {
            Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, key + ".json", $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["guide"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["guide"],"role":"{{role}}"}"""));
        }

        // Two steps: the builder has no device, so W-1 is asked of the sponsor; W-2, publication, is assigned to the operator and asked.
        Seed.Cli("--estate", root, "--json", "goal", "step", goal, "--by", op);
        Seed.Cli("--estate", root, "--json", "goal", "step", goal, "--by", op);
        Seed.Cli("--estate", root, "--json", "goal", "evaluate", goal, "--by", reviewer, "--summary", "Read the records.", "--verdict", "2:not-yet:The guide is not written.");

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-goals")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));

            // Each ask is a compact decision needed, first among them, with its exact source and state in the state language.
            List<AttentionItem> asks = shell.Attention.Where(a => a.SubjectType == "ask").ToList();
            Assert.Equal(2, asks.Count);
            Assert.Equal(asks, shell.Attention.Take(2));
            Assert.All(asks, a => Assert.Equal(("decision needed", "open, awaiting an answer"), (a.KindLabel, a.State)));
            Assert.Equal(asks[0].SubjectId, asks[0].Subject);
            Assert.StartsWith($"ask {asks[0].SubjectId} at sha256:", asks[0].Why, StringComparison.Ordinal);
            Assert.Contains("open the ask for its recommendation, alternatives and no-answer consequence", asks[0].Why, StringComparison.Ordinal);
            Assert.Equal($"vericairn records show {asks[0].SubjectId}", asks[0].NextAction);

            // Ctrl+Shift+G opens the Goals workspace on the open goal.
            await fixture.Ui(() => { window.Focus(); window.KeyPress(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.G, null); });
            GoalsWorkspace page = Assert.IsType<GoalsWorkspace>(shell.SelectedDocument);
            Assert.Equal(goal, page.Selected!.Id);
            Assert.Equal("Release the guide: active", page.Heading);
            Assert.Equal("A published guide for the widget.", page.Objective);
            Assert.StartsWith("Sponsored by Operator; accepted by its sponsor, Operator", page.Acceptance, StringComparison.Ordinal);
            Assert.Equal(["1. not met: ", "2. unknown here: "], page.Conditions.Select(c => c.Heading[..c.Heading.IndexOf(':', StringComparison.Ordinal)] + ": "));
            Assert.Contains("Evaluation: not yet, The guide is not written.", page.Conditions[1].Detail, StringComparison.Ordinal);
            Assert.Equal(["W-1  Order W-1 (builder): waits on an ask, Operator", "W-2  Order W-2 (publisher): with the person it is assigned to, Operator"], page.Steps.Select(s => s.Heading));
            Assert.Equal(("wallMinutes", "0 of 60 charged, 60 remain"), (page.Spend[0].Unit, page.Spend[0].Detail));
            Assert.Equal("0 of 1 in flight; 2 attempts per step", page.Spend[1].Detail);
            Assert.Equal(2, page.Asks.Count);
            Assert.All(page.Asks, a => Assert.StartsWith("Recommendation: ", a.Heading, StringComparison.Ordinal));
            Assert.StartsWith("open, awaiting an answer, to Operator:", page.Asks[1].Detail, StringComparison.Ordinal);
            EvaluationRow evaluation = Assert.Single(page.Evaluations);
            Assert.EndsWith("findings by Reviewer, the current evaluation", evaluation.Heading, StringComparison.Ordinal);
            Assert.StartsWith("Next: nothing to dispatch", page.Next, StringComparison.Ordinal);

            // The workspace shows and never acts: it offers no button that activates, accepts, funds, answers or takes a step (a scroll bar's are no action).
            Assert.Empty(await fixture.Ui(() => window.DocumentTabs.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button) && ReferenceEquals(b.DataContext, page)).ToList()));
            Assert.NotEmpty(await fixture.Ui(() => window.DocumentTabs.GetVisualDescendants().OfType<ListBox>().Where(l => ReferenceEquals(l.DataContext, page)).ToList()));
            IReadOnlyList<Target> targets = await fixture.Ui(() => Probe.Measure(window));
            Assert.All(targets.Where(t => !t.Exempt), t => Assert.True(t.MeetsMinimum, $"{t.Type} {t.Name}: {t.Width} x {t.Height}"));

            // Enter on an ask's attention item opens its goal's workspace and explains the ask in the inspector.
            await fixture.Ui(() => shell.Close(page));
            await fixture.Ui(() =>
            {
                window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, null);
                window.AttentionList.SelectedIndex = 0;
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            });
            Assert.Equal(goal, Assert.IsType<GoalsWorkspace>(shell.SelectedDocument).Selected!.Id);
            Assert.StartsWith("decision needed", shell.Inspector, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void AnAsksAttentionComesFromTheDeliveryAndTheRestWaitInOrder()
    {
        Assert.Empty(AskAttention.From(default, "now"));
        const string asks = """
        {"format":"vericairn.asks/1","perDelivery":3,"principals":[{"id":"P1","name":"Operator","waiting":4,
         "delivered":[{"id":"A1","state":"open","question":"Publish?","recommendation":"Publish it.","consequence":"It waits."},{"id":"A2","state":"notified","question":"Raise?","recommendation":"Raise it.","consequence":"It stops."},{"id":"A3","state":"open","question":"Stop?","recommendation":"Not yet.","consequence":"It goes on."}],
         "queued":[{"id":"A4","position":4}]}]}
        """;
        using JsonDocument document = JsonDocument.Parse(asks);
        IReadOnlyList<AttentionItem> items = AskAttention.From(document.RootElement, "2026-10-15T12:00:00Z");
        Assert.Equal(["decision needed", "decision needed", "decision needed", "information"], items.Select(i => i.Kind));
        Assert.Equal(("Publish?", "Recommendation: Publish it. If unanswered: It waits.", "open, awaiting an answer", "A1"), (items[0].Subject, items[0].Why, items[0].State, items[0].SubjectId));
        Assert.Equal("notified, delivery unknown", items[1].State);
        Assert.Equal("1 more ask waits for Operator", items[3].Subject);
    }
}
