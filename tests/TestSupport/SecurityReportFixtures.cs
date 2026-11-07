// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Providers.GitHub;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.TestSupport;

/// <summary>
/// A stand-in for the gh executable: it answers the reader's argument lists with recorded GitHub responses and never reaches GitHub.
/// Each advisory carries distinctive outside text, so a test can prove none of it leaves the reader.
/// </summary>
internal sealed class FakeGh
{
    internal const string Repository = "Example/widget";
    internal const string Summary = "Unique-summary-7f3a remote takeover in the widget parser";
    internal const string Description = "Unique-description-91bc send a crafted header to reach the parser";
    internal const string Reporter = "unique-reporter-55d2";
    internal const string Diagnostics = "untrusted-diagnostics-3e8f";
    internal static readonly string[] OutsideText = [Summary, Description, Reporter, Diagnostics, "Unique-credit-4b1d"];

    internal const string Triaged = "GHSA-2c4m-9q7r-hx3v";
    internal const string Drafted = "GHSA-5f6g-8j9p-w2x3";
    internal const string Published = "GHSA-c3h4-m5q6-r7v8";

    private readonly object gate = new();

    internal List<string[]> Calls { get; } = [];
    internal List<JsonObject> Advisories { get; } = [Advisory(Triaged, "triage", "high"), Advisory(Drafted, "draft", "medium"), Advisory(Published, "published", "low")];
    internal long Remaining { get; set; } = 4990;
    internal bool Admin { get; set; } = true;
    internal string FullName { get; set; } = Repository;

    /// <summary>The token scopes the rate-limit answer names; null leaves the header out, as a fine-grained or app token does.</summary>
    internal string? Scopes { get; set; } = "gist, read:org, repo, workflow";

    /// <summary>How many advisories each page holds; the listing follows cursors across pages.</summary>
    internal int PageSize { get; set; } = 2;

    /// <summary>Exact page bodies to answer in place of the advisories, for malformed listings.</summary>
    internal List<string>? PageBodies { get; set; }

    /// <summary>The allowance left that each page's headers report.</summary>
    internal long PageRemaining { get; set; } = 4980;

    /// <summary>The outcome of every page read, as an output limit or another bounded failure would end it.</summary>
    internal ProcessOutcome? PageOutcome { get; set; }

    /// <summary>no-login, network, rate, not-started or timeout: every call fails that way.</summary>
    internal string? Failure { get; set; }

    internal static JsonObject Advisory(string id, string state, string? severity) => new()
    {
        ["ghsa_id"] = id, ["cve_id"] = null, ["url"] = "https://api.github.com/repos/Example/widget/security-advisories/" + id,
        ["html_url"] = "https://github.com/Example/widget/security/advisories/" + id, ["summary"] = Summary, ["description"] = Description,
        ["severity"] = severity, ["state"] = state, ["author"] = new JsonObject { ["login"] = Reporter, ["id"] = 7 },
        ["credits"] = new JsonArray(new JsonObject { ["login"] = Reporter, ["type"] = "reporter" }),
        ["credits_detailed"] = new JsonArray(new JsonObject { ["user"] = new JsonObject { ["login"] = "Unique-credit-4b1d" }, ["type"] = "finder", ["state"] = "accepted" }),
        ["cvss"] = new JsonObject { ["vector_string"] = "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H", ["score"] = 9.8 },
        ["cwe_ids"] = new JsonArray("CWE-787"), ["created_at"] = "2026-11-02T02:00:00Z", ["updated_at"] = "2026-11-02T02:00:00Z",
        ["vulnerabilities"] = new JsonArray(new JsonObject { ["package"] = new JsonObject { ["ecosystem"] = "nuget", ["name"] = "Example.Widget" }, ["vulnerable_version_range"] = "< 1.0.1" }),
    };

    internal ProcessResult Run(ProcessStartInfo info)
    {
        Assert.Equal("gh", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal("1", info.Environment["GH_PROMPT_DISABLED"]);
        string[] arguments = [.. info.ArgumentList];
        lock (gate) Calls.Add(arguments);
        Assert.Equal("api", arguments[0]);
        Assert.DoesNotContain(arguments, a => a.Contains("token", StringComparison.OrdinalIgnoreCase) || a is "-X" or "--method" or "-f" or "-F" or "--field" or "--raw-field" or "--input" or "--paginate");
        switch (Failure)
        {
            case "not-started": return new(ProcessOutcome.NotStarted, false, null, string.Empty, string.Empty);
            case "timeout": return new(ProcessOutcome.TimedOut, true, null, Diagnostics, Diagnostics);
            case "no-login": return new(ProcessOutcome.Exited, true, 4, string.Empty, Diagnostics + " To get started with GitHub CLI, please run: gh auth login");
            case "network": return new(ProcessOutcome.Exited, true, 1, string.Empty, Diagnostics + " error connecting to api.github.com");
            case "rate": return new(ProcessOutcome.Exited, true, 1, "{\"message\":\"API rate limit exceeded " + Diagnostics + "\"}", "gh: API rate limit exceeded (HTTP 403)");
        }

        lock (gate)
        {
            if (arguments is ["api", "-i", "rate_limit"])
            {
                var body = new JsonObject { ["resources"] = new JsonObject { ["core"] = new JsonObject { ["limit"] = 5000, ["remaining"] = Remaining, ["reset"] = 1_793_000_000 } } };
                return Ok(Headers(Scopes is null ? [] : [("X-Oauth-Scopes", Scopes)]) + body.ToJsonString());
            }
            if (arguments is ["api", var described] && described == "repos/" + Repository)
                return Ok(new JsonObject { ["full_name"] = FullName, ["private"] = false, ["description"] = Summary, ["permissions"] = new JsonObject { ["admin"] = Admin, ["push"] = true, ["pull"] = true } }.ToJsonString());
            const string listing = "repos/" + Repository + "/security-advisories?per_page=100";
            if (arguments is ["api", "-i", var route] && route.StartsWith(listing, StringComparison.Ordinal))
            {
                int page = route == listing ? 0 : int.Parse(route[(listing.Length + "&after=cursor".Length)..], System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(route == listing || route.StartsWith(listing + "&after=cursor", StringComparison.Ordinal), route);
                if (PageOutcome is { } outcome) return new(outcome, true, null, Diagnostics, Diagnostics);
                List<string> pages = PageBodies ?? [.. Advisories.Chunk(PageSize).Select(chunk => new JsonArray([.. chunk.Select(a => (JsonNode)a.DeepClone())]).ToJsonString())];
                if (pages.Count == 0) pages.Add("[]");
                var headers = new List<(string, string)> { ("X-RateLimit-Limit", "5000"), ("X-RateLimit-Remaining", PageRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
                if (page + 1 < pages.Count)
                    headers.Add(("Link", $"<https://api.github.com/repositories/1/security-advisories?per_page=100&after=cursor{page + 1}>; rel=\"next\""));
                return Ok(Headers(headers) + pages[page]);
            }
        }

        throw new InvalidOperationException("The reader made a call the stand-in does not answer: " + string.Join(' ', arguments));
    }

    private static string Headers(IEnumerable<(string Name, string Value)> headers) =>
        "HTTP/2.0 200 OK\r\nContent-Type: application/json; charset=utf-8\r\n" + string.Concat(headers.Select(h => h.Name + ": " + h.Value + "\r\n")) + "\r\n";

    private static ProcessResult Ok(string output) => new(ProcessOutcome.Exited, true, 0, output, Diagnostics);
}

/// <summary>A format-3 estate with a releasable product whose security-reports declaration names one repository read through a stand-in gh.</summary>
internal sealed class SecurityReportFixture : IDisposable
{
    internal const string Commit = "0123456789abcdef0123456789abcdef01234567";

    internal PolicyFixture Records { get; } = new();
    internal EstateService Api { get; }
    internal CountingReleases Releases { get; } = new();
    internal FakeGh Gh { get; } = new();
    internal RecordEnvelope Resource { get; }
    internal RecordEnvelope Product { get; private set; }
    internal HeldClock Clock { get; }
    internal string Operator => Records.Estate.OperatorId;
    internal string Builder => Records.Estate.BuilderId;
    internal ReleaseSubmitRequest Request => new(Product.Id, "1.0.0", FakeGh.Repository, Operator, Commit: Commit, Expected: Product.ComputedHash) { ClientBuild = EstateService.Version };

    internal SecurityReportFixture(bool declare = true, ISecurityAdvisoryReader? reader = null, string provider = GitHubSecurityAdvisoryReader.ProviderId)
    {
        Resource = Records.Add("resource", new JsonObject
        {
            ["kind"] = "repository", ["name"] = "Widget", ["role"] = "The product's source and its advisories.",
            ["location"] = new JsonObject { ["github"] = FakeGh.Repository }, ["visibility"] = "public",
        });
        Product = Records.Add("product", ProductBody(declare ? provider : null));
        Api = EstateService.Open(Records.Estate.Root);
        RecordWritten device = Api.DeviceCreate(new(Operator, "Fixture writer", "file")).Written!;
        RecordEnvelope held = Api.Session.Load().ById[device.Id];
        Records.Estate.SetClock(held.AtTime);
        Records.Add("admission", new JsonObject { ["principal"] = Operator, ["status"] = "admitted", ["note"] = "Admit the fixture writer." }, subject: held.Reference);
        Clock = new(Records.Estate.Clock.AddMinutes(1));
        Api.PolicyClock = Clock;
        Releases.Now = () => Clock.Now;
        var registry = new ProviderRegistry();
        registry.Register(Releases);
        registry.Register(reader ?? new GitHubSecurityAdvisoryReader(Gh.Run));
        Api.Providers = registry;
        ReleaseGateSource.Prepare(Api, FakeGh.Repository, Commit);
    }

    internal JsonObject ProductBody(string? provider)
    {
        var body = new JsonObject
        {
            ["name"] = "Widget", ["slug"] = "widget", ["summary"] = "A fixture product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture interface." }),
            ["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = "github-releases", ["settings"] = new JsonObject { ["repository"] = FakeGh.Repository } } },
        };
        if (provider is not null) body["securityReports"] = new JsonObject { ["provider"] = provider, ["repositories"] = new JsonArray(Resource.Id) };
        return body;
    }

    /// <summary>The operator's triage through the runtime, of the revision this machine observed.</summary>
    internal SecurityReportsResponse Triage(string advisory, string disposition = "security", string? by = null, string? reason = null)
    {
        SecurityReportsResponse poll = Api.SecurityReportsPoll(new(Operator));
        string hash = poll.Reports.Single(r => r.Advisory == advisory).ContentHash;
        return Api.SecurityReportsTriage(new(by ?? Operator, advisory, FakeGh.Repository, hash, disposition, reason));
    }

    internal void TriageAll()
    {
        foreach (SecurityReportRow report in Api.SecurityReportsPoll(new(Operator)).Reports.Where(r => r.Holds && r.Triage is null))
            Assert.True(Api.SecurityReportsTriage(new(Operator, report.Advisory, report.Repository, report.ContentHash, "no-action", "Handled before this estate read its reports.")).Ok);
    }

    public void Dispose() { Api.Dispose(); Records.Dispose(); }
}

internal sealed class HeldClock(DateTimeOffset at) : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = at;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class CountingReleases : IReleaseProvider
{
    internal int Calls { get; private set; }
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;
    public string Provider => "github-releases";
    public ProviderDescriptor Descriptor { get; } = new FakeReleaseProvider().Descriptor;
    public DispatchReceipt Dispatch(DispatchRequest request)
    {
        Calls++;
        return new("fixture-run", "https://example.invalid/fixture-run", "Fixture dispatch.", RecordParser.FormatTimestamp(Now()));
    }
    public RunObservation? ObserveRun(string destination, string runId) => null;
    public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) => null;
    public string ManualPublicationCommand(string destination, string tag) => "Manual fixture publication.";
}
