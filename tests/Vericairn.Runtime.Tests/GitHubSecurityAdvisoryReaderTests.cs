// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Providers.GitHub;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class GitHubSecurityAdvisoryReaderTests
{
    [Fact]
    public void ReadsEveryStateAndKeepsOnlyIdentityStateSeverityAndAHash()
    {
        var gh = new FakeGh();
        gh.Advisories.Add(FakeGh.Advisory("GHSA-g2h3-j4m5-p6q7", "closed", null));
        gh.Advisories.Add(FakeGh.Advisory("GHSA-w2x3-c4f5-g6h7", "a-new-state", "extreme"));
        var reader = new GitHubSecurityAdvisoryReader(gh.Run);

        SecurityAdvisoryListing listing = reader.Read(FakeGh.Repository);

        Assert.Equal(ISecurityAdvisoryReader.Contract, reader.Descriptor.Contract);
        Assert.Equal(GitHubSecurityAdvisoryReader.ProviderId, reader.Provider);
        Assert.Equal(FakeGh.Repository, listing.Repository);
        Assert.Equal(
            [(FakeGh.Triaged, "triage", "high"), (FakeGh.Drafted, "draft", "medium"), (FakeGh.Published, "published", "low"),
             ("GHSA-g2h3-j4m5-p6q7", "closed", "unknown"), ("GHSA-w2x3-c4f5-g6h7", "unknown", "unknown")],
            listing.Advisories.OrderBy(a => gh.Advisories.FindIndex(j => j["ghsa_id"]!.GetValue<string>() == a.Identifier)).Select(a => (a.Identifier, a.State, a.Severity)));
        Assert.All(listing.Advisories, a => Assert.Matches("^sha256:[0-9a-f]{64}$", a.ContentHash));
        Assert.Equal(listing.Advisories.Count, listing.Advisories.Select(a => a.ContentHash).Distinct().Count());
        string kept = JsonSerializer.Serialize(listing);
        Assert.All(FakeGh.OutsideText, text => Assert.DoesNotContain(text, kept, StringComparison.Ordinal));
        // The login's scopes and allowance are read first, then administrator permission, then each page by its cursor; nothing else is called.
        Assert.Equal([["api", "-i", "rate_limit"], ["api", "repos/Example/widget"], ["api", "-i", "repos/Example/widget/security-advisories?per_page=100"],
            ["api", "-i", "repos/Example/widget/security-advisories?per_page=100&after=cursor1"], ["api", "-i", "repos/Example/widget/security-advisories?per_page=100&after=cursor2"]], gh.Calls);
    }

    [Fact]
    public void TheHashIgnoresFormattingAndMemberOrderButSeesAnyChange()
    {
        JsonObject advisory = FakeGh.Advisory(FakeGh.Triaged, "triage", "high");
        using JsonDocument compact = JsonDocument.Parse(advisory.ToJsonString());
        var reordered = new JsonObject(advisory.Reverse().Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
        using JsonDocument pretty = JsonDocument.Parse(reordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(GitHubSecurityAdvisoryReader.Hash(compact.RootElement), GitHubSecurityAdvisoryReader.Hash(pretty.RootElement));
        advisory["description"] = FakeGh.Description + " edited";
        using JsonDocument edited = JsonDocument.Parse(advisory.ToJsonString());
        Assert.NotEqual(GitHubSecurityAdvisoryReader.Hash(compact.RootElement), GitHubSecurityAdvisoryReader.Hash(edited.RootElement));
    }

    [Theory]
    [InlineData("not-started", "could not be started")]
    [InlineData("timeout", "could not be read completely")]
    [InlineData("no-login", "not logged in")]
    [InlineData("network", "could not be read completely")]
    [InlineData("rate", "rate limit")]
    public void EveryFailureRefusesWithoutRepeatingProviderText(string failure, string reason)
    {
        var gh = new FakeGh { Failure = failure };
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository));
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
        Assert.All(FakeGh.OutsideText, text => Assert.DoesNotContain(text, refused.Message, StringComparison.Ordinal));
        Assert.Single(gh.Calls);
    }

    [Fact]
    public void ALoginWithoutAdministratorPermissionIsRefusedRatherThanReportedComplete()
    {
        var gh = new FakeGh { Admin = false };
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository));
        Assert.Contains("not an administrator", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(gh.Calls, c => c.Any(a => a.Contains("security-advisories", StringComparison.Ordinal)));
        gh = new FakeGh { FullName = "Example/renamed" };
        Assert.Contains("another name", Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1100, true)]
    [InlineData(1000, false)]
    [InlineData(50, false)]
    public void AReadStartsOnlyAboveTheShareKeptForThePersonsOwnUse(long remaining, bool reads)
    {
        var gh = new FakeGh { Remaining = remaining };
        var reader = new GitHubSecurityAdvisoryReader(gh.Run);
        if (reads)
        {
            Assert.Equal(3, reader.Read(FakeGh.Repository).Advisories.Count);
            return;
        }
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => reader.Read(FakeGh.Repository));
        Assert.Contains("share kept for the person's own use", refused.Message, StringComparison.Ordinal);
        Assert.Contains("2026-10-26T", refused.Message, StringComparison.Ordinal);
        Assert.Equal([["api", "-i", "rate_limit"]], gh.Calls);
    }

    [Theory]
    [InlineData(null, "cannot be confirmed")]
    [InlineData("", "neither the repo nor")]
    [InlineData("gist, public_repo, read:org", "neither the repo nor")]
    [InlineData("security_events", "neither the repo nor")]
    [InlineData("repository_advisories:read", null)]
    [InlineData("read:org,repo", null)]
    public void OnlyATokenWhoseScopesShowDraftsAndPrivateReportsReads(string? scopes, string? refusal)
    {
        var gh = new FakeGh { Scopes = scopes };
        var reader = new GitHubSecurityAdvisoryReader(gh.Run);
        if (refusal is null)
        {
            Assert.Equal(3, reader.Read(FakeGh.Repository).Advisories.Count);
            return;
        }
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => reader.Read(FakeGh.Repository));
        Assert.Contains(refusal, refused.Message, StringComparison.Ordinal);
        Assert.Contains("gh auth", refused.Message, StringComparison.Ordinal);
        Assert.Equal([["api", "-i", "rate_limit"]], gh.Calls);
    }

    [Theory]
    [InlineData("output-limit", "larger than one page may be")]
    [InlineData("malformed", "unreadable response")]
    [InlineData("object", "was not a list")]
    [InlineData("changed", "changed while the listing was read")]
    [InlineData("too-many-pages", "more than 3000 advisories")]
    [InlineData("allowance", "before the listing ended")]
    public void EveryPageIsBoundedAndAnIncompleteListingFailsClosed(string defect, string reason)
    {
        var gh = new FakeGh();
        JsonObject changed = FakeGh.Advisory(FakeGh.Triaged, "triage", "critical");
        switch (defect)
        {
            case "output-limit": gh.PageOutcome = ProcessOutcome.OutputLimitExceeded; break;
            case "malformed": gh.PageBodies = ["[", "[]"]; break;
            case "object": gh.PageBodies = ["[]", "{\"ghsa_id\":\"" + FakeGh.Triaged + "\"}"]; break;
            case "changed": gh.PageBodies = [new JsonArray(FakeGh.Advisory(FakeGh.Triaged, "triage", "high")).ToJsonString(), new JsonArray(changed).ToJsonString()]; break;
            case "too-many-pages": gh.PageBodies = [.. Enumerable.Repeat("[]", GitHubSecurityAdvisoryReader.MaxPages + 1)]; break;
            case "allowance": gh.PageRemaining = 1000; break;
        }
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository));
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
        Assert.All(FakeGh.OutsideText, text => Assert.DoesNotContain(text, refused.Message, StringComparison.Ordinal));
        Assert.True(gh.Calls.Count(c => c.Any(a => a.Contains("security-advisories", StringComparison.Ordinal))) <= GitHubSecurityAdvisoryReader.MaxPages);
    }

    [Theory]
    [InlineData("after=abc def", "cursor this reader does not follow")]
    [InlineData("after=--paginate", "cursor this reader does not follow")]
    [InlineData("after=%7Bowner%7D", "cursor this reader does not follow")]
    [InlineData("after={owner}", "cursor this reader does not follow")]
    [InlineData("after=abc%26state%3Dpublished", "cursor this reader does not follow")]
    [InlineData("after=abc%20def", "cursor this reader does not follow")]
    [InlineData("after=a&after=b", "more than one cursor")]
    [InlineData("state=published", "no cursor")]
    public void OnlyOneBase64CursorOfGitHubsOwnListingIsFollowed(string query, string reason)
    {
        var gh = new FakeGh { FirstLink = "<https://api.github.com/repositories/1/security-advisories?per_page=100&" + query + ">; rel=\"next\"" };
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository));
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
        // The crafted page is never requested: the route the reader builds carries nothing from it.
        Assert.Single(gh.Calls, c => c.Any(a => a.Contains("security-advisories", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("<https://evil.example/repositories/1/security-advisories?after=abc>; rel=\"next\"")]
    [InlineData("<http://api.github.com/repositories/1/security-advisories?after=abc>; rel=\"next\"")]
    [InlineData("<https://api.github.com/user/repos?after=abc>; rel=\"next\"")]
    [InlineData("<not a url>; rel=\"next\"")]
    [InlineData("<https://api.github.com/repositories/1/security-advisories?after=a>; rel=\"next\", <https://api.github.com/repositories/1/security-advisories?after=b>; rel=\"next\"")]
    public void ANextPageAnywhereButGitHubsOwnListingFailsClosed(string link)
    {
        var gh = new FakeGh { FirstLink = link };
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository));
        Assert.Contains("next page", refused.Message, StringComparison.Ordinal);
        Assert.Single(gh.Calls, c => c.Any(a => a.Contains("security-advisories", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnEncodedBase64CursorIsFollowedExactlyInTheRouteItself()
    {
        var gh = new FakeGh { FirstLink = "<https://api.github.com/repositories/1/security-advisories?per_page=100&after=Y3Vyc29yOnYy%3D%3D>; rel=\"next\"" };
        new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository);
        Assert.Equal(["api", "-i", "repos/Example/widget/security-advisories?per_page=100&after=Y3Vyc29yOnYy%3D%3D"], gh.Calls[^1]);
    }

    [Fact]
    public void AListingLargerThanOneReadHoldsIsRefusedAndTheBoundIsNinetySixMebicharacters()
    {
        Assert.Equal(96L * 1024 * 1024, GitHubSecurityAdvisoryReader.ListingCharacters);
        Assert.Equal(16 * 1024 * 1024, GitHubSecurityAdvisoryReader.PageCharacters);
        var gh = new FakeGh();
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run, listingCharacters: 1000).Read(FakeGh.Repository));
        Assert.Contains("larger than one read holds", refused.Message, StringComparison.Ordinal);
        Assert.Equal(3, new GitHubSecurityAdvisoryReader(gh.Run, listingCharacters: 1_000_000).Read(FakeGh.Repository).Advisories.Count);
    }

    [Fact]
    public void AnEmptyListingAndALastPageAtTheReserveAreComplete()
    {
        var gh = new FakeGh();
        gh.Advisories.Clear();
        Assert.Empty(new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository).Advisories);
        // A last page has no next page to stop before, so the allowance it leaves does not make the listing incomplete.
        gh = new FakeGh { PageSize = 100, PageRemaining = 10 };
        Assert.Equal(3, new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository).Advisories.Count);
    }

    [Theory]
    [InlineData("GHSA-0000-0000-0000")]
    [InlineData("GHSA-2c4m-9q7r-hx3v; rm -rf /")]
    [InlineData("")]
    public void AnAdvisoryWithoutAValidIdentifierMakesTheWholeListingIncomplete(string identifier)
    {
        var gh = new FakeGh();
        gh.Advisories[1]["ghsa_id"] = identifier;
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(FakeGh.Repository));
        Assert.Contains("without a valid identifier", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(identifier.Length == 0 ? "\u0000" : identifier, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Example/widget; gh api -X DELETE repos/Example/widget")]
    [InlineData("../widget")]
    [InlineData("Example/..")]
    [InlineData("Example/.")]
    [InlineData("Example")]
    [InlineData("-X/DELETE")]
    public void AnInvalidRepositoryIsRefusedBeforeAnyProcessStarts(string repository)
    {
        var gh = new FakeGh();
        Assert.Throws<ProviderRefusedException>(() => new GitHubSecurityAdvisoryReader(gh.Run).Read(repository));
        Assert.Empty(gh.Calls);
    }

    [Fact]
    public void TheMemoryReaderServesTheSameContract()
    {
        var reader = new MemorySecurityAdvisoryReader();
        reader.Set(FakeGh.Repository, new(FakeGh.Triaged, "triage", "high", "sha256:" + new string('a', 64)));
        Assert.Equal(ISecurityAdvisoryReader.Contract, reader.Descriptor.Contract);
        Assert.Single(reader.Read(FakeGh.Repository).Advisories);
        Assert.Throws<ProviderRefusedException>(() => reader.Set(FakeGh.Repository, new("GHSA-bad", "triage", "high", "sha256:" + new string('a', 64))));
        reader.Refusal = "Stopped.";
        Assert.Equal("Stopped.", Assert.Throws<ProviderRefusedException>(() => reader.Read(FakeGh.Repository)).Message);
    }
}
