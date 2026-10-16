// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public class CommunicationAttentionTests
{
    [Fact]
    public void StructuredAttentionIncludesEveryCategoryAndEveryOriginalAtFrequencyNone()
    {
        IEstateApi api = DispatchProxy.Create<IEstateApi, AttentionApi>();
        var source = (AttentionApi)api;
        source.Records =
        [
            Record("verification", "failed", """{"outcome":"failed","evidence":"complete failed evidence"}"""),
            Record("verification", "unknown", """{"outcome":"unknown","evidence":"complete unknown evidence"}"""),
            Record("result", "blocked", """{"outcome":"blocked","summary":"complete blocker","checks":[],"unresolvedEffects":[]}"""),
            Record("result", "uncertain", """{"outcome":"partial","checks":[],"unresolvedEffects":["complete uncertain effect"]}"""),
            Record("result", "check-failed", """{"outcome":"partial","checks":[{"outcome":"failed","summary":"complete result check"}],"unresolvedEffects":[]}"""),
        ];
        IReadOnlyList<CommunicationAttention> attention = CommunicationPresentation.Attention(api, "person");
        Assert.All(CommunicationPresentation.MandatoryKinds, kind => Assert.Contains(attention, a => a.Kind == kind));
        Assert.Contains(attention, a => a.Kind == "failed-check" && a.Record == "check-failed");
        Assert.Contains(attention, a => a.Kind == "blocker" && a.Record == "held-work");
        Assert.Contains(attention, a => a.Kind == "blocker" && a.Record == "blocked-goal");
        Assert.True(CommunicationPresentation.ShouldDeliver("none", "progress", attention));
        string rendered = CommunicationPresentation.RenderAttention(attention);
        Assert.All(source.Records, record => Assert.Contains(record.Envelope.GetRawText(), rendered, StringComparison.Ordinal));
        Assert.Contains("Complete question and consequence.", rendered, StringComparison.Ordinal);
        Assert.Contains("Complete incident", rendered, StringComparison.Ordinal);
        Assert.Contains("budget exhausted", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableAttentionSourcesRemainVisibleAsUnknown()
    {
        IEstateApi api = DispatchProxy.Create<IEstateApi, AttentionApi>();
        ((AttentionApi)api).Unavailable = true;
        IReadOnlyList<CommunicationAttention> attention = CommunicationPresentation.Attention(api);
        Assert.NotEmpty(attention);
        Assert.All(attention, item => Assert.Equal("unknown-check", item.Kind));
        Assert.True(CommunicationPresentation.ShouldDeliver("none", "progress", attention));
    }

    private static RecordResponse Record(string type, string id, string body) => new("record.json", id, type, "sha256:exact", true, Json("{\"body\":" + body + "}"));
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    public class AttentionApi : DispatchProxy
    {
        public IReadOnlyList<RecordResponse> Records { get; set; } = [];
        public bool Unavailable { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Unavailable) { throw new IOException("The attention source is unavailable."); }
            return targetMethod?.Name switch
            {
                nameof(IEstateApi.Records) => Records.Where(r => r.Type == (string)args![0]!).ToList(),
                nameof(IEstateApi.Asks) => new AsksResponse("Complete question and consequence.", Json("""{"principals":[{"waiting":1}]}""")),
                nameof(IEstateApi.Progress) => new ProgressResponse("Complete progress.", Json("""{"eligible":{"held":[{"id":"held-work","relations":[{"id":"dependency","holds":true,"state":"planned"}]}]}}"""), false),
                nameof(IEstateApi.Goals) => new List<GoalRow> { new("blocked-goal", "sha256:goal", "A goal", "blocked", "person", null) },
                nameof(IEstateApi.Goal) => new GoalResponse("Complete blocker and next step.", Json("""{"id":"blocked-goal","status":"blocked"}""")),
                nameof(IEstateApi.Runs) => new RunsResponse(
                    [new RunRow("run", "identity", "generic", null, null, "goal", null, "known-failure", Json("{}"), null, Json("{}"), null, null, false, true,
                        Signals: [new("incident", "critical", "now", "Complete incident")])],
                    [new LedgerRow("goal", "Goal", "budget-limited", [new("tokens", 100, 100, 0)], 0, 1, 1, ["budget exhausted"])]),
                _ => throw new NotSupportedException(),
            };
        }
    }
}
