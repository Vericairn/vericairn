// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class SecurityReportsApiTests
{
    private const string Advisory = "GHSA-2c4m-9q7r-hx3v";

    public SecurityReportsApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task TheDaemonReadsDeclaredReportsOnItsOwnScheduleAndTheOperatorSeesTheIncident()
    {
        (TestEstate estate, MemorySecurityAdvisoryReader reader) = Seed();
        using (estate)
        {
            await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false,
                options: new DaemonOptions { SecurityReportsFirstRead = TimeSpan.Zero }, cancellationToken: TestContext.Current.CancellationToken);
            DateTime deadline = DateTime.UtcNow + TestTiming.HangTimeout;
            AttentionNotice? notice = null;
            while (notice is null && DateTime.UtcNow < deadline)
            {
                notice = host.Service.AttentionGet(estate.OperatorId).Items.FirstOrDefault(n => n.SubjectType == "security-report");
                if (notice is null) await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.NotNull(notice);
            Assert.Equal("incident", notice.Kind);
            Assert.Contains(Advisory + " on Example/widget, state triage, severity high", notice.Why, StringComparison.Ordinal);
            Assert.True(reader.Reads >= 1);
            Assert.DoesNotContain(host.Service.AttentionGet(estate.BuilderId).Items, n => n.SubjectType is "security-report" or "security-reports");
        }
    }

    [Fact]
    public async Task ThePollRouteReadsAndTheEventStreamCarriesTheIncidentToTheOperatorAloneWithAnOpaqueSubject()
    {
        (TestEstate estate, MemorySecurityAdvisoryReader reader) = Seed();
        using (estate)
        {
            await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false,
                options: new DaemonOptions { SecurityReportsFirstRead = TimeSpan.FromHours(1) }, cancellationToken: TestContext.Current.CancellationToken);
            using var client = new Sdk.VericairnClient(host.Endpoint);
            var attention = new EventFilter(Kinds: ["attention"]);
            string operatorCursor = client.AttentionGet(estate.OperatorId).EventsCursor!;
            string builderCursor = client.AttentionGet(estate.BuilderId).EventsCursor!;

            SecurityReportsResponse poll = await client.SecurityReportsPollAsync(new(estate.OperatorId), TestContext.Current.CancellationToken);
            Assert.True(poll.Ok, poll.Message);
            Assert.True(poll.Held);
            Assert.Equal(1, reader.Reads);
            Assert.False(client.SecurityReportsPoll(new(estate.BuilderId)).Ok);

            EventEnvelope? incident = null;
            DateTime deadline = DateTime.UtcNow + TestTiming.HangTimeout;
            while (incident is null && DateTime.UtcNow < deadline)
            {
                EventsResponse events = await client.EventsAsync(after: operatorCursor, filter: attention, principal: estate.OperatorId, wait: EventVocabulary.MaxWaitSeconds,
                    cancellationToken: TestContext.Current.CancellationToken);
                incident = events.Events.FirstOrDefault(e => e.Subject.Type == "security-report");
                operatorCursor = events.Cursor;
            }

            Assert.NotNull(incident);
            Assert.Equal(("raised", estate.OperatorId, "incident"), (incident.Change, incident.To, incident.Category));
            Assert.StartsWith("hmac-sha256:", incident.Subject.Id, StringComparison.Ordinal);
            string json = System.Text.Json.JsonSerializer.Serialize(incident, ProtocolJson.Options);
            foreach (string text in new[] { Advisory, "Example/widget", "sha256:" + new string('c', 64) })
                Assert.DoesNotContain(text, json, StringComparison.Ordinal);
            Assert.DoesNotContain(client.Events(after: builderCursor, filter: attention, principal: estate.BuilderId, wait: 2).Events,
                e => e.Subject.Type is "security-report" or "security-reports");
        }
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("triage")]
    public async Task EveryRouteBindsItsPrincipalToTheAuthenticatedDevice(string operation)
    {
        (TestEstate estate, MemorySecurityAdvisoryReader reader) = Seed();
        using (estate)
        {
            using EstateService service = EstateService.Open(estate.Root);
            string route = "/v1/security-reports/" + operation;
            object request = operation == "poll" ? new SecurityReportsPollRequest(estate.OperatorId)
                : new SecurityReportTriageRequest(estate.OperatorId, Advisory, "Example/widget", "sha256:" + new string('c', 64), "security");
            Assert.True(RequestScope.Covers("POST " + route));
            Assert.True(RequestScope.Covers(request.GetType()));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = route;
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", true);
            var scope = new RequestScope(service, new LocalNetworkLedger(estate.Root));
            bool reached = false;
            object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Equal(0, reader.Reads);
        }
    }

    // Each test names its own reader id, so the process's registry never shares one between tests.
    private static (TestEstate, MemorySecurityAdvisoryReader) Seed()
    {
        var estate = new TestEstate(3);
        var reader = new MemorySecurityAdvisoryReader("daemon-test-advisories-" + Ulid.NewUlid().Value.ToLowerInvariant());
        ProviderRegistry.Current.Register(reader);
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId,
            """{"kind":"repository","name":"Widget","role":"The product's source.","location":{"github":"Example/widget"},"visibility":"public"}""");
        estate.Add("product", estate.OperatorId, $$$"""
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],
             "surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],"securityReports":{"provider":"{{{reader.Provider}}}","repositories":["{{{resource.Id}}}"]}}
            """);
        reader.Set("Example/widget", new(Advisory, "triage", "high", "sha256:" + new string('c', 64)));
        return (estate, reader);
    }
}
