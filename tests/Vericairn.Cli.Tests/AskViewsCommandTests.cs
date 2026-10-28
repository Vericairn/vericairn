// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class AskViewsCommandTests
{
    public AskViewsCommandTests() => CliHarness.Run("--help");

    [Fact]
    public void EveryOpenAskCommandFollowsCurrentAnswersWithoutAnAskRevision()
    {
        using var fixture = new AnsweredAskFixture();
        foreach (bool answered in new[] { false, true, false, true })
        {
            fixture.Decide(answered);
            Dictionary<string, string> before = fixture.Records();
            foreach (string[] command in new[] { new[] { "ask" }, ["operator", "list"], ["status"], ["progress"] })
            {
                CliHarness.Outcome json = CliHarness.RunWithApi(fixture.Service, null, ["--estate", fixture.Estate.Root, "--json", .. command]);
                Assert.Equal(0, json.Code);
                JsonElement expected = command[0] switch
                {
                    "ask" => fixture.Service.Asks().Json,
                    "operator" => fixture.Service.OperatorActions().Json,
                    "status" => fixture.Service.Status().Json,
                    _ => fixture.Service.Progress().Json,
                };
                Assert.True(JsonElement.DeepEquals(expected, json.Json));
                CliHarness.Outcome text = CliHarness.RunWithApi(fixture.Service, null, ["--estate", fixture.Estate.Root, .. command]);
                Assert.Equal(0, text.Code);
                Assert.Contains(fixture.Pending.Id, text.Out, StringComparison.Ordinal);
                string displayed = command[0] == "ask" ? fixture.Original.Id : fixture.Notified.Id;
                Assert.Equal(!answered, text.Out.Contains(displayed, StringComparison.Ordinal));
            }
            Assert.Equal(before.OrderBy(p => p.Key), fixture.Records().OrderBy(p => p.Key));
            fixture.AssertAskUnchanged(answered);
        }
    }
}
