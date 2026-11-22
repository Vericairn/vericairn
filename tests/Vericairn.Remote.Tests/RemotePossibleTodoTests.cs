// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>OPS-22 (0.9.4): Remote shows the optional operator requests status gives as possible todos, each with its guide, recommendation and commands.</summary>
public sealed class RemotePossibleTodoTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AnOptionalRequestIsAPossibleTodoWithItsGuideAndCommands()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope work = estate.Load().Current("work-order").Single();
        estate.Add("operator-request", estate.BuilderId, RemoteOperatorActTests.RequestBody(estate, 1).ToJsonString(), work.Reference);
        JsonObject body = RemoteOperatorActTests.RequestBody(estate, 2);
        body["optional"] = true;
        RecordEnvelope todo = estate.Add("operator-request", estate.BuilderId, body.ToJsonString(), work.Reference);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            Presentation.TodoRow row = Assert.Single(model.PossibleTodos);
            Assert.True(model.HasPossibleTodos);
            Assert.Equal(todo.Id, row.Revision);
            Assert.Equal("Possible todo: Perform action 2", row.Heading);
            Assert.Contains("Recommendation: Recommended action 2", row.Detail, StringComparison.Ordinal);
            Assert.Contains($"vericairn operator fetch {todo.Id} --to <folder>", row.Detail, StringComparison.Ordinal);
            Assert.Contains($"vericairn operator dismiss {todo.Id} --by {estate.OperatorId} --expected {todo.ComputedHash}", row.Detail, StringComparison.Ordinal);
            Assert.StartsWith("# Perform action 2\n", row.Guide, StringComparison.Ordinal);
            Assert.DoesNotContain(model.Attention, a => a.Notice?.SubjectId == todo.Id);
            return true;
        });
    }
}
