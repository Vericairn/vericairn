// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Progress;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (WATCH-06): a progress target's default comment trigger, a step that waits on the operator, read from a run's stops: a
/// stop that reaches the operator after the target last looked adds one comment, and one that already waited adds none.
/// </summary>
[Collection("home")]
public sealed class ProgressTargetStepTests
{
    [Fact]
    public void AStepThatComesToWaitOnTheOperatorAddsOneComment()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string goal = FlowStepRoutingTests.Run(run, change: FlowStopRoutingTests.Options);
        string directory = Directory.CreateTempSubdirectory("progress-target-steps-").FullName;
        try
        {
            var adapter = new ProgressTargetTests.FakeTarget();
            var registry = new ProviderRegistry();
            registry.Register(adapter);
            var store = new ProgressTargetStore(directory);
            store.Save(new ProgressTargetConfiguration("status", "fake-target", ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("place", "the shelf")]),
                "private", null, [], ProgressTrigger.DefaultComment));
            var runner = new ProgressTargetRunner(() => run.Estate.Load(), registry, store);
            ProgressTargetRunner.Target target = runner.Resolve("status").Target!;

            // The first look: nothing waits on the operator yet, and nothing that already waited would be commented on.
            Assert.Equal(["created"], runner.Take(target, [], look: true).Select(o => o.Action));
            Assert.NotNull(store.State("status").Waiting);
            Assert.Empty(adapter.Comments);

            // The song reaches its listening review, which the operator holds: the step's event adds one comment naming it.
            FlowStopRoutingTests.ToAcceptance(run, goal);
            RecordEnvelope goalRecord = run.Estate.Load().ById[goal];
            var step = new EventEnvelope(EventVocabulary.Format, "sha256:step", "cursor", "step", "changed", "2026-10-13T12:00:00Z",
                new EventReference("step", "hmac-sha256:listen"), [new EventReference("goal", goalRecord.Id, goalRecord.ComputedHash)], run.Estate.OperatorId);
            Assert.Equal(["commented"], runner.Take(target, [step], look: false).Select(o => o.Action));
            string comment = Assert.Single(adapter.Comments);
            Assert.StartsWith("Waiting on Test Operator: ", comment, StringComparison.Ordinal);
            Assert.Contains("step listen", comment, StringComparison.Ordinal);

            // Heard again while it still waits, it adds nothing.
            Assert.Empty(runner.Take(target, [step with { Key = "sha256:step-again" }], look: false));
            Assert.Single(adapter.Comments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
