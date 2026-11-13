// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-107 (decision 01M6WVBC5DJQNEN5XPEK09W2XP): OPS-07's cost test found the work-order key check quadratic, each current work order
/// scanning every other. The check now reads one map of the current keys, with the same findings.
/// </summary>
public sealed class WorkOrderKeyTests(ITestOutputHelper output)
{
    private const string Code = "work.duplicate-key";

    /// <summary>
    /// A hang guard, not a measurement: the map judges 100,000 work orders in a few seconds, and the scan it replaced compared every
    /// pair of them, some five billion comparisons, which takes many minutes.
    /// </summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Fact]
    public void OneHundredThousandWorkOrdersAreJudgedInSecondsWithTheirDuplicateKeysFound()
    {
        var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var files = new List<RecordFile>();
        RecordEnvelope Author(string type, string by, JsonNode body, string? supersedes = null, string? id = null)
        {
            clock = clock.AddSeconds(1);
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, null, supersedes, clock, id ?? Ulid.NewUlid(clock).Value, 3), []);
            files.Add(new RecordFile(record.RelativePath, record.Bytes));
            return record.Envelope;
        }

        string op = Ulid.NewUlid(clock).Value;
        Author("principal", op, new JsonObject { ["kind"] = "human", ["name"] = "Operator", ["roles"] = new JsonArray("operator") }, id: op);
        RecordEnvelope charter = Author("estate", op, new JsonObject
        {
            ["name"] = "Keys", ["slug"] = "keys", ["purpose"] = "Judge many work orders.", ["operator"] = op, ["recordFormat"] = 3,
            ["policies"] = new JsonObject { ["signatures"] = "optional", ["releaseAcceptance"] = new JsonArray("operator") },
        });

        // 100,000 work orders with their own keys, then those that share one: two more with W-7, and a revision of W-10 that takes
        // W-11. A revision that keeps its key, of W-20, and the W-10 it superseded are no longer current and share nothing.
        var orders = new List<RecordEnvelope>();
        for (int number = 0; number < 100_000; number++) orders.Add(Author("work-order", op, Order("W-" + number.ToString(CultureInfo.InvariantCulture))));
        RecordEnvelope secondSeven = Author("work-order", op, Order("W-7"));
        RecordEnvelope thirdSeven = Author("work-order", op, Order("W-7"));
        RecordEnvelope movedToEleven = Author("work-order", op, Order("W-11"), supersedes: orders[10].Id);
        Author("work-order", op, Order("W-20"), supersedes: orders[20].Id);
        string[] expected = [.. new[] { orders[7], secondSeven, thirdSeven, orders[11], movedToEleven }.Select(r => r.ExpectedPath).Order(StringComparer.Ordinal)];

        var watch = Stopwatch.StartNew();
        EstateModel model = EstateLoader.LoadPrepared(Path.Combine(Path.GetTempPath(), "vericairn-work-order-keys-" + Ulid.NewUlid().Value),
            [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)], Manifest(charter.Id));
        watch.Stop();
        output.WriteLine($"{orders.Count + 4:N0} work orders judged in {watch.Elapsed.TotalSeconds:N1} s");

        Assert.True(watch.Elapsed < Bound, $"Judging {orders.Count + 4:N0} work orders took {watch.Elapsed}.");
        Assert.Equal(expected, model.Findings.Where(f => f.Code == Code).Select(f => f.Location).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error && f.Code != Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheFindingsAreThoseTheScanOfEveryCurrentWorkOrderGave(int seed)
    {
        // The scan the map replaced, as the oracle: a current work order's key is a duplicate when another current work order, with
        // another id, has the same key. Keys come from a small pool, and revisions keep or change theirs.
        var random = new Random(seed);
        using var estate = new TestEstate(3);
        var latest = new List<RecordEnvelope>();
        for (int step = 0; step < 150; step++)
        {
            string key = "K-" + random.Next(40).ToString(CultureInfo.InvariantCulture);
            if (latest.Count > 0 && random.Next(3) == 0)
            {
                int revised = random.Next(latest.Count);
                string kept = random.Next(2) == 0 ? EstateModel.BodyString(latest[revised], "key")! : key;
                latest[revised] = estate.Add("work-order", estate.OperatorId, Order(kept).ToJsonString(), supersedes: latest[revised].Id);
            }
            else
            {
                latest.Add(estate.Add("work-order", estate.OperatorId, Order(key).ToJsonString()));
            }
        }

        EstateModel model = estate.Load();
        RecordEnvelope[] current = [.. model.Current("work-order")];
        string[] scanned = [.. current.Where(order => current.Any(other => other.Id != order.Id && other.Body.GetProperty("key").GetString() == EstateModel.BodyString(order, "key")))
            .Select(order => order.ExpectedPath).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(scanned);
        Assert.Equal(scanned, model.Findings.Where(f => f.Code == Code).Select(f => f.Location).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TwoFilesWithOneIdAreNotDuplicateKeysOfEachOther()
    {
        // The scan compared ids, so two copies of one record under different paths shared no key with each other; record.duplicate-id
        // reports them instead. A third work order with the key is a duplicate of both.
        using var estate = new TestEstate(3);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, Order("W-1").ToJsonString());
        string original = Path.Combine(estate.Root, order.ExpectedPath);
        string copy = Path.Combine(Path.GetDirectoryName(original)!, "..", "2026-09", Path.GetFileName(original));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(original, copy);
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);

        RecordEnvelope other = estate.Add("work-order", estate.OperatorId, Order("W-1").ToJsonString());
        Assert.Contains(estate.Load().Findings, f => f.Code == Code && f.Location == other.ExpectedPath);
    }

    private static JsonObject Order(string key) => new()
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Work " + key, ["intent"] = "Stand for one of many work orders.", ["scope"] = new JsonArray("x"),
        ["exitQuestion"] = "Done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("y"),
    };

    private static byte[] Manifest(string estate)
    {
        var manifest = new JsonObject
        {
            ["estate"] = estate, ["format"] = EstateManifest.Format1, ["recordFormat"] = 3, ["profile"] = "repository-synced",
            ["vericairn"] = new JsonObject { ["commit"] = new string('0', 40), ["mode"] = "source", ["repository"] = "Vericairn/vericairn" },
        };
        using JsonDocument document = JsonDocument.Parse(manifest.ToJsonString());
        return CanonicalJson.SerializePrettyUtf8(document.RootElement);
    }
}
