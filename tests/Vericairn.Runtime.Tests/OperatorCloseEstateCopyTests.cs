// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Operator close on a copy of a real estate (FIX-141): VERICAIRN_ESTATE_COPY names an estate, which the test copies, without its Git
/// directory and machine-local state, to a temporary directory and reads there; nothing is written to the estate named. Every work
/// order whose newest acceptance names its current revision and was recorded after its current result is no longer listed, and its
/// result reads accepted in status; every one whose current result was recorded after such an acceptance is still listed. Run on a
/// copy of Vericairn's own estate, it shows the work orders the operator accepted in October before acceptances named a result's exact
/// hash gone from the listing, and the one whose result came after that acceptance still there. Skipped unless the variable is set.
/// </summary>
[Collection("home")]
public sealed class OperatorCloseEstateCopyTests(ITestOutputHelper output)
{
    private const string EnvironmentVariable = "VERICAIRN_ESTATE_COPY";

    [Fact]
    public void OperatorCloseListsNoResultAnAcceptanceOfItsWorkOrdersCurrentRevisionFollowedAndEveryResultRecordedAfterOne()
    {
        string? source = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(source), $"{EnvironmentVariable} names an estate whose copy this test reads; unset, nothing is read.");
        Assert.True(File.Exists(Path.Combine(source!, EstateLayout.ManifestPath)), $"Missing prerequisite: {EnvironmentVariable} must name an estate root.");
        string root = Path.Combine(Path.GetTempPath(), "vericairn-close-copy-" + Ulid.NewUlid().Value);
        try
        {
            Copy(Path.GetFullPath(source!), root);
            using EstateService service = EstateService.Open(root);
            OperatorCloseCandidatesResponse listed = service.OperatorCloseCandidates();
            var keys = listed.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
            EstateModel model = service.Session.Load();

            // The steps operator close listed before 0.9.4, read again: the latest completed result with a passing review of it and
            // no acceptance of it, whose work order's newest acceptance names its current revision.
            var accepted = new List<(string Key, RecordEnvelope Result)>();
            var later = new List<string>();
            foreach (RecordEnvelope order in model.Current("work-order"))
            {
                IReadOnlyList<string> chain = model.Chain(order);
                RecordEnvelope? result = Newest(model.Current("result").Where(r => WorkView.OrderOfResult(model, r) is { } o && model.Latest(o).Id == order.Id));
                if (result is null || EstateModel.BodyString(result, "outcome") != "completed" || model.OfType("acceptance").Any(a => a.Subject == result.Reference)
                    || !model.About(result.Id, "review").Any(r => model.IsCurrent(r) && r.Subject == result.Reference && EstateModel.BodyString(r, "outcome") == "pass")) continue;
                RecordEnvelope? acceptance = Newest(chain.SelectMany(id => model.About(id, "acceptance")));
                if (acceptance is null || !WorkOrderRevisions.IsCurrentSubject(model, acceptance.Subject, order)) continue;
                string key = EstateModel.BodyString(order, "key")!;
                if (GoalRules.Before(result, acceptance)) accepted.Add((key, result));
                else later.Add(key);
            }

            output.WriteLine($"listed: {keys.Count}: {string.Join(' ', keys.Order(StringComparer.Ordinal))}");
            output.WriteLine($"accepted at the current revision after the current result, no longer listed: {accepted.Count}: {string.Join(' ', accepted.Select(a => a.Key).Order(StringComparer.Ordinal))}");
            output.WriteLine($"current result recorded after the acceptance, still listed: {later.Count}: {string.Join(' ', later.Order(StringComparer.Ordinal))}");
            Assert.NotEmpty(accepted);
            Assert.NotEmpty(later);
            Assert.All(accepted, a => Assert.DoesNotContain(a.Key, keys));
            Assert.All(later, key => Assert.Contains(key, keys));

            // Status reads each such result accepted, and the assignment of each later result as still awaiting review.
            JsonElement assignments = service.Status().Json.GetProperty("assignments");
            foreach ((_, RecordEnvelope result) in accepted)
                foreach (JsonElement row in assignments.EnumerateArray().Where(a => a.GetProperty("latestResult").ValueKind == JsonValueKind.String && a.GetProperty("latestResult").GetString() == result.Id))
                    Assert.Equal("accepted", row.GetProperty("state").GetString());
            foreach (string key in later)
                Assert.Contains(assignments.EnumerateArray(), a => a.GetProperty("key").GetString() == key && a.GetProperty("state").GetString() == "awaiting review");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RecordEnvelope? Newest(IEnumerable<RecordEnvelope> records) =>
        records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();

    // Every entry but the Git directory and machine-local state, so the copy is an estate of the same records and files.
    private static void Copy(string from, string to)
    {
        foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).Where(d => !Skipped(from, d)))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Where(f => !Skipped(from, f)))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    private static bool Skipped(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)[0] is ".git" or ".vericairn";
}
