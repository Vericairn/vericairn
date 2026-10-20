// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PolicyHashCommandsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneCommandHashesAPreparedBodyWithoutAnEstateOrAnyWrites(bool duty)
    {
        CliHarness.Ok("policies", "hash", "--help");
        using var fixture = new PolicyFixture();
        JsonObject body = fixture.PolicyBody(duty: duty);
        string directory = CliHarness.TempDirectory("policy-hash");
        try
        {
            string path = CliHarness.WriteJson(directory, "policy.json", body.ToJsonString());
            string absentEstate = Path.Combine(directory, "not-an-estate");
            Dictionary<string, string> before = Snapshot(directory);
            var json = CliHarness.Ok("--estate", absentEstate, "policies", "hash", "--body", path, "--json");
            var response = JsonSerializer.Deserialize<PolicyHashResponse>(json.Out, ProtocolJson.Options)!;
            Assert.Null(response.Problem);
            Assert.True(ContentHash.IsValid(response.DefinitionHash));
            Assert.Equal(response.DefinitionHash + "\n", CliHarness.Ok("--estate", absentEstate, "policies", "hash", "--body", path).Out);
            Assert.Equal(before, Snapshot(directory));
            Assert.False(Directory.Exists(absentEstate));
            using EstateService api = EstateService.Open(fixture.Estate.Root);
            Assert.Equal(api.PolicyHash(new(PolicyFixture.Element(body))), response);
            var approval = fixture.Approve(body);
            Assert.Equal(response.DefinitionHash, approval.Body.GetProperty("policies")[0].GetProperty("definitionHash").GetString());
            body["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
            File.WriteAllText(path, body.ToJsonString());
            Assert.Equal(response.DefinitionHash, CliHarness.Ok("policies", "hash", "--body", path, "--json").Json.GetProperty("definitionHash").GetString());
            body["title"] = "A changed definition";
            File.WriteAllText(path, body.ToJsonString());
            Assert.NotEqual(response.DefinitionHash, CliHarness.Ok("policies", "hash", "--body", path, "--json").Json.GetProperty("definitionHash").GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("{", "not valid JSON")]
    [InlineData("null", "prepared policy or duty body")]
    [InlineData("[]", "prepared policy or duty body")]
    [InlineData("{}", "prepared policy or duty body")]
    [InlineData("{\"type\":\"estate-policy\",\"body\":{}}", "prepared policy or duty body")]
    [InlineData("{\"token\":\"Open311\",\"state\":\"active\"}", "prepared policy or duty body")]
    public void InvalidInputFailsClearlyWithoutPrintingAHashOrWriting(string input, string message)
    {
        string directory = CliHarness.TempDirectory("invalid-policy-hash");
        try
        {
            string path = CliHarness.WriteJson(directory, "body.json", input);
            Dictionary<string, string> before = Snapshot(directory);
            CliHarness.Outcome result = CliHarness.Run("policies", "hash", "--body", path, "--json");
            Assert.Equal(2, result.Code);
            Assert.Empty(result.Out);
            Assert.Contains(message, result.Err, StringComparison.Ordinal);
            Assert.Equal(before, Snapshot(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void BodyIsRequiredAndAMissingFileIsReported()
    {
        Assert.NotEqual(0, CliHarness.Run("policies", "hash").Code);
        string directory = CliHarness.TempDirectory("missing-policy-body");
        try
        {
            CliHarness.Outcome missing = CliHarness.Run("policies", "hash", "--body", Path.Combine(directory, "missing.json"));
            Assert.Equal(2, missing.Code);
            Assert.Empty(missing.Out);
            Assert.Contains("error:", missing.Err, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public void HashingDoesNotCreateRuntimeStorage()
    {
        CliHarness.Ok("policies", "hash", "--help");
        using var fixture = new PolicyFixture();
        string directory = CliHarness.TempDirectory("policy-hash-storage");
        string unusedHome = Path.Combine(directory, "unused-home");
        string path = CliHarness.WriteJson(directory, "body.json", fixture.PolicyBody().ToJsonString());
        // Install a fresh test destination without inspecting or copying any existing home.
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, unusedHome);
        try
        {
            CliHarness.Ok("policies", "hash", "--body", path);
            CliHarness.Ok("policies", "hash", "--body", path, "--json");
            Assert.False(Directory.Exists(unusedHome));
            Assert.Single(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, CliHarness.TempDirectory("cli-tests-home"));
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(root, path), path => ContentHash.Sha256(File.ReadAllBytes(path)));
}
