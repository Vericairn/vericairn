// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

/// <summary>vericairn events watch follows the daemon's stream over the local socket, resumes from a cursor and reports a reset.</summary>
[Collection("cli")]
public sealed class EventsWatchCommandTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Resource = """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"public"}""";

    [Fact]
    public async Task WatchResumesFromACursorPrintsEachEventAndReportsAReset()
    {
        string root = CliHarness.TempDirectory("events-watch");
        string scratch = CliHarness.TempDirectory("events-watch-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Event journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;

            // Only the daemon keeps the journal.
            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "events", "watch", "--count", "1");
            Assert.Equal(2, refused.Code);
            Assert.Contains("daemon keeps the event journal", refused.Err, StringComparison.Ordinal);
            Assert.Equal(2, CliHarness.Run("--estate", root, "events", "watch", "--count", "0").Code);

            CliHarness.RequireDaemonTransport(tls: false);
            await using DaemonHost host = await DaemonHost.StartAsync(root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
            using var client = new VericairnClient(host.Endpoint);
            string cursor = client.Status().EventsCursor!;
            string written = CliHarness.Ok("--estate", root, "--json", "records", "new", "resource", "--by", op, "--body", CliHarness.WriteJson(scratch, "resource.json", Resource)).Id;

            // From the cursor a status read gave, the record written after it, as JSON lines: the opening cursor, then the envelope.
            CliHarness.Outcome watched = CliHarness.Run("--estate", root, "--json", "events", "watch", "--after", cursor, "--kind", "record", "--count", "1");
            Assert.True(watched.Code == 0, watched.Err);
            string[] lines = watched.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(JsonDocument.Parse(lines[0]).RootElement.TryGetProperty("cursor", out _), watched.Out);
            EventEnvelope envelope = JsonSerializer.Deserialize<EventEnvelope>(lines[^1], ProtocolJson.Options)!;
            Assert.Equal((EventVocabulary.Format, "record", "written", written), (envelope.Format, envelope.Kind, envelope.Change, envelope.Subject.Id));

            CliHarness.Outcome text = CliHarness.Run("--estate", root, "events", "watch", "--after", cursor, "--kind", "record", "--type", "resource", "--count", "1");
            Assert.True(text.Code == 0, text.Err);
            Assert.Contains("record written  resource " + written, text.Out, StringComparison.Ordinal);

            // A cursor that cannot be honored: a reset line, then the stream follows from the head.
            Task<CliHarness.Outcome> following = Task.Run(() => CliHarness.Run("--estate", root, "events", "watch", "--after", "not-a-cursor", "--kind", "record", "--count", "1"), TestContext.Current.CancellationToken);
            // Records are written until the watch, which stops after one event, hears one: any record written once its stream is
            // open is delivered, so nothing here depends on when the stream opened.
            var writes = new List<string>();
            using (var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
            {
                guard.CancelAfter(TestTiming.HangTimeout);
                while (!following.IsCompleted)
                {
                    WriteOutcome next = client.NewRecord(new NewRecordRequest("resource", op, JsonDocument.Parse(Resource).RootElement.Clone()));
                    Assert.True(next.Ok, next.Problem?.Message);
                    writes.Add(next.Written!.Id);
                    await Task.WhenAny(following, Task.Delay(TimeSpan.FromSeconds(2), guard.Token));
                }
            }

            CliHarness.Outcome reset = await following;
            Assert.True(reset.Code == 0, reset.Err);
            Assert.StartsWith("reset: ", reset.Out, StringComparison.Ordinal);
            Assert.Contains(writes, id => reset.Out.Contains("record written  resource " + id, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
