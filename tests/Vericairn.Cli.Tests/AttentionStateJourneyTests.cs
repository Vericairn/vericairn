// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class AttentionStateJourneyTests
{
    [Fact]
    public void HumanStatusShowsAcknowledgmentWhileCanonicalStatusAndItsSubjectStayUnchanged()
    {
        using var estate = new TestEstate(3);
        var repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""");
        var check = estate.Add("verification", estate.BuilderId, """{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"unknown","summary":"Awaiting a check.","evidence":[],"limitations":[]}""", subject: repository.Reference);
        string canonical = CliHarness.Ok("--estate", estate.Root, "--json", "status").Out;
        using EstateService api = EstateService.Open(estate.Root);
        AttentionNotice notice = Assert.Single(api.AttentionGet(estate.OperatorId).Items, i => i.SubjectId == check.Id);
        int records = estate.Load().Records.Count;
        Assert.True(api.AttentionAcknowledge(new(estate.OperatorId, notice.Kind, notice.SubjectType, notice.SubjectId, notice.Revision)).Ok);
        foreach (string depth in new[] { "short", "standard", "comprehensive" })
        {
            string human = CliHarness.Ok("--estate", estate.Root, "--depth", depth, "status").Out;
            Assert.Contains("acknowledged at ", human, StringComparison.Ordinal);
            Assert.Contains(check.ComputedHash, human, StringComparison.Ordinal);
            Assert.Contains("unknown-check: verification " + check.Id, human, StringComparison.Ordinal);
            Assert.Contains("subject state: unknown", human, StringComparison.Ordinal);
        }
        Assert.Equal(canonical, CliHarness.Ok("--estate", estate.Root, "--json", "status").Out);
        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal("unknown", api.Record(check.Id)!.Envelope.GetProperty("body").GetProperty("outcome").GetString());
        Assert.Equal("unacknowledged", Assert.Single(api.AttentionGet(estate.BuilderId).Items, i => i.SubjectId == check.Id).AttentionState);
    }
}
