// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The whole-estate passes one status read makes, counted rather than timed (FIX-136): a read is what one <c>vericairn status</c> does
/// without a daemon, the runtime's opening and then status. Before FIX-136 a read judged the estate three times, twice in the opening;
/// derived every living document three times for status's text, JSON and checks; read every work order once for each goal for each
/// assignment it judged; and read every operation once for each operation. Counted on generated estates of 40 and of 200 work orders,
/// that code judged the estate 3 times, derived the living documents 3 times, and read the work orders 846 and 4,046 times and the
/// operations 65 and 225 times. Now a read judges the estate twice and derives the living documents once, and the whole work orders
/// and operations are read as often whatever the estate's size (6 and 26 times on those estates). From 0.9.4 (FIX-148) the one-shot
/// runtime keeps its opening's judgment for status, so a read judges the estate once, counted in a context of the read's own.
/// </summary>
[Collection("status-performance")]
public sealed class StatusPassCountTests
{
    [Fact]
    public void OneStatusReadJudgesTheEstateOnceAndMakesNoRemovedPassThatGrowsWithTheEstate()
    {
        using var small = new GeneratedStatusEstate(40, goals: 20, operations: true, livingDocuments: true);
        using var large = new GeneratedStatusEstate(200, goals: 20, operations: true, livingDocuments: true);
        StatusPasses fewer = StatusPasses.Read(small.Root);
        StatusPasses more = StatusPasses.Read(large.Root);
        foreach (StatusPasses passes in new[] { fewer, more })
        {
            Assert.False(passes.Status.HasErrors);
            Assert.Equal((1, 1), (passes.OpeningJudgments, passes.Judgments));
            Assert.Equal(1, passes.LivingDocumentDerivations);
        }

        // Five times the work orders, assignments and operations, and the whole work orders and operations are read as often.
        Assert.True(more.Status.Json.GetProperty("records").GetInt32() > 4 * fewer.Status.Json.GetProperty("records").GetInt32());
        Assert.Equal(fewer.WorkOrderReads, more.WorkOrderReads);
        Assert.Equal(fewer.OperationReads, more.OperationReads);
    }
}

/// <summary>
/// What one status read counted: the opening's judgments of the estate and all of them, and, for the model status derived its views
/// from, how many times every living document was derived and every work order and every operation read whole.
/// </summary>
internal sealed record StatusPasses(long OpeningJudgments, long Judgments, long LivingDocumentDerivations, long WorkOrderReads, long OperationReads,
    StatusResponse Status, TimeSpan Opening, TimeSpan Elapsed)
{
    /// <summary>
    /// Opens a one-shot runtime on the estate, as a command without a daemon does, and reads status, counting its judgments in a context
    /// of its own with fresh signature tables, and timing it.
    /// </summary>
    internal static StatusPasses Read(string root, Action<string, TimeSpan>? observe = null)
    {
        var clock = Stopwatch.StartNew();
        var judgments = new JudgmentContext(new Vericairn.Records.Signing.SignatureTables());
        using EstateService service = EstateService.Open(root, null, null, oneShot: true, judgments: judgments);
        TimeSpan opened = clock.Elapsed;
        long opening = judgments.Judgments;
        EstateModel? judged = null;
        service.Session.Loaded = model => judged = model;
        StatusResponse status = service.Status(observe);
        clock.Stop();
        Assert.NotNull(judged);
        IReadOnlyDictionary<string, long> reads = judged.TypeReads;
        return new(opening, judgments.Judgments, judged.LivingDocumentDerivations, reads.GetValueOrDefault("work-order"), reads.GetValueOrDefault("operation"),
            status, opened, clock.Elapsed);
    }
}
