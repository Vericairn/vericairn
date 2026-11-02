// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class CatalogRequestTests
{
    [Fact]
    public void OneSharedFormAndStructuredCommentsUseTheSameDeliberateFields()
    {
        string form = CatalogRequests.Form(CatalogFixture.Inventory);
        foreach (string field in new[] { "entry-key", "request", "intended-use", "offer-to-test", "offer-to-contribute", "notice-opt-in" })
            Assert.Contains("    id: " + field + "\n", form, StringComparison.Ordinal);
        Assert.DoesNotContain("type: dropdown", form, StringComparison.Ordinal);
        Assert.DoesNotContain("checked: true", form, StringComparison.Ordinal);
        Assert.Contains("one vote per hosting account and entry", form, StringComparison.Ordinal);
        Assert.Contains(CatalogRequests.Explanation, form, StringComparison.Ordinal);
        Assert.Contains("No subscription is enabled", form, StringComparison.Ordinal);
        CatalogRequestParse parsed = CatalogRequests.Parse("### Entry key\nfixture.active\n\n### Request\n- [x] I request implementation of this entry.\n");
        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Value);
        Assert.True(CatalogRequests.Validate(CatalogFixture.Inventory, parsed.Value).Accepted);
        Assert.False(parsed.Value.OfferToTest);
        Assert.False(parsed.Value.OfferToContribute);
        Assert.False(parsed.Value.NoticeOptIn);
        Assert.Equal("", parsed.Value.IntendedUse);
    }

    [Theory]
    [InlineData("fixture.active", true, "accepted")]
    [InlineData("fixture.remainder", true, "accepted")]
    [InlineData("fixture.retired", false, "retired-entry")]
    [InlineData("fixture.available", false, "not-requestable")]
    [InlineData("fixture.unknown", false, "unknown-entry")]
    [InlineData("Fixture.active", false, "unknown-entry")]
    [InlineData("fixture.withheld", false, "unknown-entry")]
    public void OnlyExactRequestableKeysAreAdmittedWithUsefulPointers(string key, bool accepted, string code)
    {
        CatalogRequestValidation result = CatalogRequests.Validate(CatalogFixture.Inventory, new(key, true));
        Assert.Equal(accepted, result.Accepted);
        Assert.Equal(code, result.Code);
        Assert.Equal(code == "unknown-entry" ? "indexes/entries.md" : "guides/adapter/" + key + ".md", result.Reference);
    }

    [Fact]
    public void OptionalFieldsNeverBecomeRequirementsAndUseTextIsBounded()
    {
        foreach (bool offered in new[] { false, true })
        {
            Assert.True(CatalogRequests.Validate(CatalogFixture.Inventory, new("fixture.active", true, null, offered, offered, offered)).Accepted);
            Assert.True(CatalogRequests.Validate(CatalogFixture.Inventory, new("fixture.active", true, new string('x', 500), offered, offered, offered)).Accepted);
        }
        Assert.Equal("invalid-intended-use", CatalogRequests.Validate(CatalogFixture.Inventory, new("fixture.active", true, new string('x', 501))).Code);
        Assert.False(CatalogRequests.Validate(CatalogFixture.Inventory, new("fixture.active", false, "Use", true, true, true)).Accepted);
        Assert.False(CatalogRequests.Validate(CatalogFixture.Inventory, new("fixture.active", true, "Use" + (char)0)).Accepted);
    }

    [Theory]
    [InlineData("Ordinary discussion is not a vote.")]
    [InlineData("### Entry key\nfixture.active\n### Request\nI agree")]
    [InlineData("### Entry key\nfixture.active\n### Request\n- [x] I request implementation of this entry.\n### Account\nsomeone")]
    [InlineData("### Entry key\nfixture.active\n### Entry key\nfixture.remainder\n### Request\n- [x] I request implementation of this entry.")]
    [InlineData("### Entry key\nfixture.active\n### Offer to test\n- [x] I offer to test.")]
    public void DiscussionUnknownFieldsAndAmbiguousPayloadsDoNotParseAsRequests(string body)
    {
        CatalogRequestParse result = CatalogRequests.Parse(body);
        Assert.Null(result.Value);
        Assert.Equal("invalid-form", result.Error);
    }

    [Fact]
    public void ParsedEditsAndWithdrawalsKeepOptionalConsentExplicit()
    {
        const string body = "### Entry key\r\nfixture.active\r\n### Request\r\n- [ ] I request implementation of this entry.\r\n### Intended use\r\n_No response_\r\n### Offer to test\r\n- [X] I offer to test.\r\n### Offer to contribute\r\n- [ ] I offer to contribute.\r\n### Status notices\r\n- [x] I would like status notices.\r\n";
        CatalogRequest request = Assert.IsType<CatalogRequest>(CatalogRequests.Parse(body).Value);
        Assert.False(request.Request);
        Assert.True(request.OfferToTest);
        Assert.False(request.OfferToContribute);
        Assert.True(request.NoticeOptIn);
        Assert.Equal("", request.IntendedUse);
        Assert.Equal("request-not-affirmed", CatalogRequests.Validate(CatalogFixture.Inventory, request).Code);
    }

    [Fact]
    public void ParsedMultilineUseHasPlatformIndependentNewlines()
    {
        const string body = "### Entry key\nfixture.active\n### Request\n- [x] I request implementation of this entry.\n### Intended use\nFirst line\nSecond line\n";
        Assert.Equal(CatalogRequests.Parse(body), CatalogRequests.Parse(body.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Assert.Equal("First line\nSecond line", CatalogRequests.Parse(body).Value!.IntendedUse);
    }

    [Fact]
    public void InconsistentOrDuplicatedInventoryCannotMakeRetiredScopeRequestable()
    {
        CatalogInventory inventory = CatalogFixture.Inventory;
        foreach (CatalogInventory invalid in new[]
        {
            inventory with { Format = "unknown" },
            inventory with { Entries = [inventory.Entries[0], inventory.Entries[0]] },
            inventory with { Entries = [inventory.Entries[2] with { Requestable = true }] },
            inventory with { Entries = [inventory.Entries[0] with { Guide = "unrelated.md" }] },
        })
        {
            Assert.Equal("invalid-inventory", CatalogRequests.Validate(invalid, new("fixture.active", true)).Code);
            Assert.Throws<ArgumentException>(() => CatalogRequests.Form(invalid));
        }
    }
}
