// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class PolicyHashTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbeddedHashNeedsNoWriteAuthorityAndChangesNoFiles(bool duty)
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        // Fixture signers belong to their own key store; no runtime signing device is created.
        Assert.Empty(service.PolicyAccess(fixture.Estate.BuilderId));
        var request = new PolicyHashRequest(PolicyFixture.Element(fixture.PolicyBody(duty: duty)));
        Dictionary<string, string> before = Snapshot(fixture.Estate.Root);
        PolicyHashResponse response = service.PolicyHash(request);
        Assert.Null(response.Problem);
        Assert.True(ContentHash.IsValid(response.DefinitionHash));
        Assert.Equal(response, await ((IEstateApi)service).PolicyHashAsync(request, TestContext.Current.CancellationToken));
        var bound = new CancellationBoundApi(service, TestContext.Current.CancellationToken);
        Assert.Equal(response, bound.PolicyHash(request));
        Assert.Equal(response, await bound.PolicyHashAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(before, Snapshot(fixture.Estate.Root));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"body\":{},\"type\":\"estate-policy\"}")]
    public void InvalidBodiesReturnAProblemWithoutAHash(string json)
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        Dictionary<string, string> before = Snapshot(fixture.Estate.Root);
        using JsonDocument body = JsonDocument.Parse(json);
        PolicyHashResponse response = service.PolicyHash(new(body.RootElement));
        Assert.Null(response.DefinitionHash);
        Assert.Equal("invalid", response.Problem!.Code);
        Assert.Contains("prepared policy or duty body", response.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(fixture.Estate.Root));
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(root, path), path => ContentHash.Sha256(File.ReadAllBytes(path)));
}
