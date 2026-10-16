// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public class CommunicationResolutionTests
{
    [Fact]
    public void DefaultsAreExplicitAndDoNotInventAPersonalProfile()
    {
        CommunicationProfile profile = CommunicationResolution.Resolve("person", null, []);
        Assert.Equal("completion-and-need", profile.Fields["progress"].Value);
        Assert.Equal("short", profile.Fields["status"].Value);
        foreach (string kind in new[] { "decision", "result", "handoff" })
        {
            Assert.Equal("comprehensive", profile.Fields[kind].Value);
            Assert.Equal("shipped", profile.Fields[kind].Source);
        }

        Assert.Empty(profile.Revisions);
    }

    [Fact]
    public void PrecedenceIsPerFieldAndAViewDoesNotChangeTheStoredLayers()
    {
        CommunicationLayer[] layers =
        [
            new("estate", new(Status: "standard", Decision: "short"), "estate-record", "h1", "administrator"),
            new("person", new(Status: "comprehensive"), "person-record", "h2", "person"),
            new("assignment", new(Result: "short"), "assignment-record", "h3", "person"),
        ];
        CommunicationProfile profile = CommunicationResolution.Resolve("person", "assignment", layers, new(Status: "short"));
        Assert.Equal("view", profile.Fields["status"].Source);
        Assert.Equal(4, profile.Fields["status"].Contributions.Count);
        Assert.Equal("estate", profile.Fields["decision"].Source);
        Assert.Equal("assignment", profile.Fields["result"].Source);
        Assert.Equal("comprehensive", CommunicationResolution.Resolve("person", null, layers[..2]).Fields["status"].Value);
    }

    [Fact]
    public void DivergentRevisionsAreVisibleAndNeverLastArrivalWins()
    {
        CommunicationLayer[] layers =
        [
            new("person", new(Status: "short"), "one", "h1", "person"),
            new("person", new(Status: "standard"), "two", "h2", "delegate"),
            new("person", new(Status: "comprehensive"), "old", "h0", "person", Current: false),
        ];
        CommunicationProfile profile = CommunicationResolution.Resolve("person", null, layers);
        Assert.Null(profile.Fields["status"].Value);
        Assert.True(profile.Fields["status"].Conflict);
        Assert.Equal(["one", "two"], profile.Conflicts);
        Assert.Equal(3, profile.Revisions.Count);
        Assert.Equal("short", CommunicationResolution.Resolve("person", null, layers, new(Status: "short")).Fields["status"].Value);
        Assert.NotEmpty(CommunicationResolution.Resolve("person", null, layers, new(Status: "short")).Conflicts);
    }

    [Fact]
    public void NoneIsAValueAndResetInherits()
    {
        Assert.Equal("none", CommunicationResolution.Resolve("p", null, [new("person", new(Progress: "none"))]).Fields["progress"].Value);
        Assert.Equal("completion-and-need", CommunicationResolution.Resolve("p", null, [new("person", new())]).Fields["progress"].Value);
        Assert.Throws<ArgumentException>(() => CommunicationResolution.Resolve("p", null, [], new(Status: "terse")));
    }
}
