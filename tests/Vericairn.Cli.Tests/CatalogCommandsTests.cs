// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class CatalogCommandsTests
{
    [Fact]
    public void ExportAndPublicationCheckRequireExplicitLocalInputsAndOfferNoPublishingCommand()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command guides = root.Subcommands.Single(c => c.Name == "guides");
        foreach (string action in new[] { "export", "publication-check" })
        {
            string help = CliHarness.Ok("guides", action, "--help").Out;
            string destination = action == "export" ? "--out" : "--destination";
            Command command = guides.Subcommands.Single(c => c.Name == action);
            foreach (string option in new[] { "--catalog", "--contracts", "--index", "--plan", destination })
            {
                Assert.Contains(option, help, StringComparison.Ordinal);
                Assert.True(command.Options.Single(o => o.Name == option).Required);
            }
            Assert.Contains("--issues", help, StringComparison.Ordinal);
            Assert.NotEqual(0, CliHarness.Run("guides", action).Code);
        }
        Assert.DoesNotContain(guides.Subcommands, c => c.Name is "publish" or "push" or "create-repository");
    }

    [Fact]
    public void ExportTwiceAndCompareReportExactBytesWithoutOpeningAnEstate()
    {
        string root = CatalogFixture.CreateTemporaryDirectory("catalog-commands-");
        try
        {
            new CatalogFixture().WriteInputs(root);
            string absentEstate = Path.Combine(root, "absent-estate");
            string first = Path.Combine(root, "first"), second = Path.Combine(root, "second");
            foreach (string output in new[] { first, second })
            {
                CliHarness.Outcome written = CliHarness.Ok(["--estate", absentEstate, "--json", .. Arguments("export", root, output)]);
                Assert.True(written.Json.GetProperty("ok").GetBoolean());
                Assert.Equal("local-preparation", written.Json.GetProperty("publicationState").GetString());
                Assert.Empty(written.Json.GetProperty("findings").EnumerateArray());
                Assert.False(Directory.Exists(Path.Combine(output, ".git")));
            }
            string[] files = Directory.GetFiles(first, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(first, p)).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(files, Directory.GetFiles(second, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(second, p)).Order(StringComparer.Ordinal));
            foreach (string file in files) Assert.Equal(File.ReadAllBytes(Path.Combine(first, file)), File.ReadAllBytes(Path.Combine(second, file)));
            CliHarness.Outcome compared = CliHarness.Ok(["--json", .. Arguments("publication-check", root, first)]);
            Assert.Empty(compared.Json.GetProperty("changes").EnumerateArray());
            Assert.Empty(compared.Json.GetProperty("findings").EnumerateArray());
            Assert.NotEqual(0, CliHarness.Run(Arguments("export", root, first)).Code);
            File.WriteAllText(Path.Combine(first, "README.md"), "Edited proposal");
            compared = CliHarness.Ok(["--json", .. Arguments("publication-check", root, first)]);
            Assert.Contains(compared.Json.GetProperty("changes").EnumerateArray(), c => c.GetProperty("path").GetString() == "README.md" && c.GetProperty("action").GetString() == "change");
            Assert.Equal("Edited proposal", File.ReadAllText(Path.Combine(first, "README.md")));
            Assert.False(Directory.Exists(absentEstate));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "home")));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void InvalidAllowlistProducesFindingsAndCreatesNoOutput()
    {
        string root = CatalogFixture.CreateTemporaryDirectory("catalog-refusal-");
        try
        {
            var fixture = new CatalogFixture();
            fixture.Plan = fixture.Plan with { Allowlist = [] };
            fixture.WriteInputs(root);
            string output = Path.Combine(root, "bundle");
            CliHarness.Outcome refused = CliHarness.Run(["--json", .. Arguments("export", root, output)]);
            Assert.Equal(1, refused.Code);
            Assert.False(refused.Json.GetProperty("ok").GetBoolean());
            Assert.Equal(0, refused.Json.GetProperty("files").GetInt32());
            Assert.False(Directory.Exists(output));
            Assert.NotEqual(0, CliHarness.Run(Arguments("export", root, Path.Combine(root, "catalog"))).Code);
        }
        finally { CliHarness.Delete(root); }
    }

    private static string[] Arguments(string action, string root, string output) =>
        ["guides", action, "--catalog", Path.Combine(root, "catalog"), "--contracts", Path.Combine(root, "contracts"),
            "--index", Path.Combine(root, "index.json"), "--plan", Path.Combine(root, "plan.json"), action == "export" ? "--out" : "--destination", output];
}
