// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Guides;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class CatalogRequestCommandsTests
{
    [Fact]
    public void ValidateAcceptsEmptyOptionalFieldsAndRefusesUnknownRetiredAndUnconfirmedRequests()
    {
        string root = CatalogFixture.CreateTemporaryDirectory("catalog-request-command-");
        try
        {
            string inventory = Path.Combine(root, "inventory.json"), request = Path.Combine(root, "request.json");
            File.WriteAllBytes(inventory, CatalogFixture.Json(CatalogFixture.Inventory));
            foreach ((string key, bool deliberate, bool accepted) in new[]
            {
                ("fixture.active", true, true), ("fixture.remainder", true, true), ("fixture.retired", true, false),
                ("fixture.unknown", true, false), ("fixture.active", false, false),
            })
            {
                File.WriteAllText(request, "{\"entryKey\":\"" + key + "\",\"request\":" + (deliberate ? "true" : "false") + "}");
                CliHarness.Outcome result = CliHarness.Run("guides", "request-validate", "--inventory", inventory, "--request", request);
                Assert.Equal(accepted ? 0 : 1, result.Code);
                Assert.Equal(accepted, result.Json.GetProperty("accepted").GetBoolean());
                Assert.NotEmpty(result.Json.GetProperty("reference").GetString()!);
            }
            File.WriteAllText(request, "{\"entryKey\":\"fixture.active\",\"request\":true,\"accountId\":\"claimed\"}");
            Assert.NotEqual(0, CliHarness.Run("guides", "request-validate", "--inventory", inventory, "--request", request).Code);
            File.WriteAllText(request, "{\"entryKey\":\"fixture.active\",\"entryKey\":\"fixture.remainder\",\"request\":true}");
            Assert.NotEqual(0, CliHarness.Run("guides", "request-validate", "--inventory", inventory, "--request", request).Code);
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void ReconciliationOutputsPrivateSuppliedResultsAndIncompleteInputNeverReturnsZero()
    {
        string root = CatalogFixture.CreateTemporaryDirectory("catalog-reconcile-command-");
        try
        {
            string inventory = Path.Combine(root, "inventory.json"), observations = Path.Combine(root, "observations.json");
            File.WriteAllBytes(inventory, CatalogFixture.Json(CatalogFixture.Inventory));
            CatalogRequestObservation first = CatalogFixture.Issue("first", "account");
            CatalogRequestSnapshot snapshot = CatalogFixture.Snapshot(first, CatalogFixture.Issue("duplicate", "account", 2), first);
            File.WriteAllBytes(observations, CatalogFixture.Json(snapshot));
            CliHarness.Outcome result = CliHarness.Ok("guides", "requests-reconcile", "--inventory", inventory, "--observations", observations);
            Assert.Equal("private-local-result", result.Json.GetProperty("publicationState").GetString());
            Assert.Equal("supplied", result.Json.GetProperty("observationMode").GetString());
            Assert.Single(result.Json.GetProperty("result").GetProperty("votes").EnumerateArray());
            Assert.Equal("first", Assert.Single(result.Json.GetProperty("result").GetProperty("issues").EnumerateArray()).GetProperty("issueId").GetString());
            Assert.Equal(CatalogFixture.Json(snapshot), File.ReadAllBytes(observations));
            File.WriteAllBytes(observations, CatalogFixture.Json(snapshot with { Complete = false }));
            result = CliHarness.Run("guides", "requests-reconcile", "--inventory", inventory, "--observations", observations);
            Assert.Equal(1, result.Code);
            Assert.False(result.Json.GetProperty("result").GetProperty("complete").GetBoolean());
            Assert.Empty(result.Json.GetProperty("result").GetProperty("totals").EnumerateArray());
        }
        finally { CliHarness.Delete(root); }
    }
}
