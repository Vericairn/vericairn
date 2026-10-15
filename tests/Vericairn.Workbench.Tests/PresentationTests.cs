// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Records.Types;

namespace Vericairn.Workbench.Tests;

/// <summary>The state language: one golden file, the presentation table and the runtime's own strings never disagree.</summary>
public class PresentationTests
{
    [Fact]
    public void TheGoldenFileAndThePresentationTableAreIdentical()
    {
        using JsonDocument golden = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs", "state-language.json")));
        JsonElement vocabularies = golden.RootElement.GetProperty("vocabularies");
        Assert.Equal(vocabularies.EnumerateObject().Count(), StateLanguage.Vocabularies.Count);
        foreach (StateLanguage.Vocabulary vocabulary in StateLanguage.Vocabularies)
        {
            Assert.True(vocabularies.TryGetProperty(vocabulary.Subject, out JsonElement states), $"golden file lacks {vocabulary.Subject}");
            Assert.Equal(states.EnumerateArray().Select(s => s.GetString()!).ToList(), vocabulary.States.Select(s => s.Value).ToList());
        }
    }

    [Fact]
    public void TheRuntimeStringsAreInTheTable()
    {
        List<string> operations = StateLanguage.Vocabularies.Single(v => v.Subject == "operation").States.Select(s => s.Value).ToList();
        Assert.Equal(OperationType.Statuses.Order(StringComparer.Ordinal), operations.Order(StringComparer.Ordinal));
        List<string> releases = StateLanguage.Vocabularies.Single(v => v.Subject == "release").States.Select(s => s.Value).ToList();
        Assert.Equal(ReleaseType.Statuses.Order(StringComparer.Ordinal), releases.Order(StringComparer.Ordinal));
        List<string> asks = StateLanguage.Vocabularies.Single(v => v.Subject == "ask").States.Select(s => s.Value).ToList();
        Assert.Equal(Vericairn.Records.Estate.GoalPredicates.States["ask"], asks);
        Assert.Equal(AskType.States.Append("answered"), asks);
        Assert.True(StateLanguage.NeedsPerson("ask", "notified"));
        Assert.Equal(["proposed", .. GoalStatusType.Statuses.Where(s => s is "active" or "paused" or "budget-limited" or "blocked"), "impossible", "abandoned", "achieved"], StateLanguage.Vocabularies.Single(v => v.Subject == "goal").States.Select(s => s.Value));
        Assert.Equal(GoalStatusType.Statuses.Append("proposed").Append("achieved").Order(StringComparer.Ordinal), StateLanguage.Vocabularies.Single(v => v.Subject == "goal").States.Select(s => s.Value).Order(StringComparer.Ordinal));
        Assert.Equal(Vericairn.Records.Estate.GoalPredicates.Verdicts, StateLanguage.Vocabularies.Single(v => v.Subject == "condition").States.Select(s => s.Value));
        Assert.Equal(Vericairn.Records.Estate.GoalRules.EvaluationVerdicts, StateLanguage.Vocabularies.Single(v => v.Subject == "evaluation").States.Select(s => s.Value));
        Assert.Equal(Vericairn.Records.Estate.NextEligible.RouteStates, StateLanguage.Vocabularies.Single(v => v.Subject == "step-route").States.Select(s => s.Value));
        Assert.True(StateLanguage.NeedsPerson("step-route", "unavailable"));
        Assert.False(StateLanguage.NeedsPerson("step-route", "running"));
        Assert.False(StateLanguage.NeedsPerson("ask", "answered"));
        Assert.Equal("submitted, awaiting acceptance", StateLanguage.Label("work-order", "submitted"));
        Assert.Equal("made-up", StateLanguage.Label("work-order", "made-up"));
        Assert.True(StateLanguage.NeedsPerson("operation", "uncertain"));
        Assert.False(StateLanguage.NeedsPerson("operation", "observed-success"));
    }

    [Fact]
    public void TheSdksConnectionStatesAreTheStateLanguages()
    {
        // A client says what its connection to a runtime is in exactly the words the cohesion contract gives, in their order.
        List<string> connection = StateLanguage.Vocabularies.Single(v => v.Subject == "connection").States.Select(s => s.Value).ToList();
        Assert.Equal(connection, Vericairn.Sdk.ConnectionStates.All);
        Assert.Equal("cached; freshness shown", StateLanguage.Label("connection", Vericairn.Sdk.ConnectionStates.Cached));
    }

    [Fact]
    public void AttentionIsDerivedFromTheStatusDocumentOnly()
    {
        const string status = """
        {"estate":{"id":"01","name":"Example","slug":"example"},"records":12,"vericairn":{"mode":"release","version":"0.2.0"},
         "workOrders":[{"key":"M9","kind":"milestone","title":"A milestone","status":"submitted","id":"o1"},{"key":"W-1","kind":"ordinary","title":"Work","status":"active","id":"o2"}],
         "assignments":[{"key":"W-1","id":"a1","contributor":"p1","contributorName":"Builder","attempts":1,"latestResult":"r1","outcome":"completed","state":"awaiting review"},{"key":"W-1","id":"a2","contributor":"p2","contributorName":null,"attempts":1,"latestResult":"r2","outcome":"blocked","state":"awaiting review"}],
         "releases":[{"version":"0.3.0","status":"candidate","commit":"abc","id":"rel1"}],
         "operations":[{"identity":"github-releases:x/y:v0.3.0","provider":"github-releases","action":"release","status":"uncertain","id":"op1"}],
         "findings":[{"severity":"error","code":"records.hash","location":"records/x.json","message":"Hash mismatch."},{"severity":"warning","code":"layout.residue","location":".DS_Store","message":"Residue."}],
         "reconcile":[]}
        """;
        using JsonDocument document = JsonDocument.Parse(status);
        EstateView view = EstateView.From(document.RootElement, "2026-10-14T00:00:00Z");
        Assert.Equal("Example", view.Name);
        Assert.Equal("release 0.2.0", view.Pin);
        Assert.Equal(2, view.WorkOrders.Count);
        List<string> kinds = view.Attention.Select(a => a.Kind).ToList();
        Assert.Equal(["decision needed", "decision needed", "decision needed", "blocked", "decision needed", "uncertain effect", "failed check", "information"], kinds);
        AttentionItem milestone = view.Attention[0];
        Assert.Equal("M9 A milestone", milestone.Subject);
        Assert.Contains("operator", milestone.Why, StringComparison.Ordinal);
        Assert.Equal("work-order", milestone.SubjectType);
        Assert.All(view.Attention, a => Assert.False(string.IsNullOrWhiteSpace(a.NextAction)));
        Assert.Equal("uncertain: reconcile before any retry", view.Operations[0].StatusLabel);
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        return root ?? throw new InvalidOperationException("repository root not found");
    }
}
