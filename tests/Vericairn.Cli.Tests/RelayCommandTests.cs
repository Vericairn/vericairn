// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class RelayCommandTests
{
    [Fact]
    public void RelayUrlPersistsAndAnInvalidUrlDoesNotReplaceIt()
    {
        string root = CliHarness.TempDirectory("relay-estate");
        string home = CliHarness.TempDirectory("relay-home");
        string? previous = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            new Vericairn.Records.Updates.ReleaseUpdateStore().Configure(false);
            CliHarness.Ok("--estate", root, "estate", "init", "--name", "Relay Test", "--slug", "relay-test", "--purpose", "Relay settings.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", new string('a', 40));
            Assert.Equal(JsonValueKind.Null, Configure().GetProperty("relay").ValueKind);
            Assert.Equal("https://relay.invalid/", Configure("--relay", "https://RELAY.invalid:443").GetProperty("relay").GetString());
            Assert.Contains("HTTPS origin", CliHarness.Run("--estate", root, "runtime", "configure", "--relay", "http://relay.invalid").Err, StringComparison.Ordinal);
            Assert.Equal("https://relay.invalid/", Configure().GetProperty("relay").GetString());
            Assert.Equal(JsonValueKind.Null, Configure("--relay", "off").GetProperty("relay").ValueKind);

            JsonElement Configure(params string[] args) =>
                CliHarness.Ok(["--estate", root, "--json", "runtime", "configure", .. args]).Json;
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previous);
            Directory.Delete(root, recursive: true);
            Directory.Delete(home, recursive: true);
        }
    }
}
