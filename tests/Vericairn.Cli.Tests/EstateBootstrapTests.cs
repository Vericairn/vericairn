// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class EstateBootstrapTests
{
    internal const string WrittenJson = """[{"path":"bootstrap","status":"written"},{"path":"bootstrap.ps1","status":"written"}]""";
    private static readonly string[] JsonOption = ["--json"];
    private static readonly string[] ForceOption = ["--force"];

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("bootstrap.ps1")]
    public void ShippedScriptEqualsItsFirstPartySource(string name)
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        byte[] source = File.ReadAllBytes(Path.Combine(root, "src", "Vericairn.Cli", "Bootstrap", name));
        Assert.NotEmpty(source);
        Assert.DoesNotContain((byte)'\r', source);
        Assert.Equal(source, ShippedBytes(name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingEstateGetsBothScriptsAndAnIdenticalCopyIsUnchanged(bool json)
    {
        using var estate = new TestEstate(3);
        string[] before = EstateFiles(estate.Root);
        CliHarness.Outcome written = Run(estate.Root, json);
        AssertReceipt(written, json, "written", "written");
        AssertInstalled(estate.Root);
        Assert.Equal(before, EstateFiles(estate.Root));
        Assert.Empty(EstateChecks.Run(estate.Load()));

        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (string name in new[] { "bootstrap", "bootstrap.ps1" })
            File.SetLastWriteTimeUtc(Path.Combine(estate.Root, name), timestamp);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(estate.Root, "bootstrap"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AssertReceipt(Run(estate.Root, json), json, "unchanged", "unchanged");
        AssertReceipt(Run(estate.Root, json, force: true), json, "unchanged", "unchanged");
        AssertInstalled(estate.Root);
        foreach (string name in new[] { "bootstrap", "bootstrap.ps1" })
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(estate.Root, name)));
        Assert.Equal(before, EstateFiles(estate.Root));
    }

    [Theory]
    [InlineData("bootstrap", false)]
    [InlineData("bootstrap.ps1", false)]
    [InlineData("bootstrap", true)]
    [InlineData("bootstrap.ps1", true)]
    public void DifferingCopyRefusesBeforeEitherFileChangesAndForceReplacesIt(string name, bool json)
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(name, "# Estate customization.\n");
        string[] before = EstateFiles(estate.Root);
        string other = name == "bootstrap" ? "bootstrap.ps1" : "bootstrap";
        CliHarness.Outcome refused = Run(estate.Root, json);
        Assert.Equal(2, refused.Code);
        Assert.Empty(refused.Out);
        Assert.Contains(name + " differs", refused.Err, StringComparison.Ordinal);
        Assert.Contains("--force", refused.Err, StringComparison.Ordinal);
        Assert.Contains("Nothing changed.", refused.Err, StringComparison.Ordinal);
        Assert.Equal("# Estate customization.\n", File.ReadAllText(Path.Combine(estate.Root, name)));
        Assert.False(File.Exists(Path.Combine(estate.Root, other)));
        Assert.Equal(before, EstateFiles(estate.Root));

        AssertReceipt(Run(estate.Root, json, force: true), json, "written", "written");
        AssertInstalled(estate.Root);
        Assert.Equal(before, EstateFiles(estate.Root));
        Assert.Empty(EstateChecks.Run(estate.Load()));
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("bootstrap.ps1")]
    public void MissingCopyIsWrittenWhileItsIdenticalCompanionIsUnchanged(string missing)
    {
        using var estate = new TestEstate(3);
        AssertReceipt(Run(estate.Root, json: true), true, "written", "written");
        File.Delete(Path.Combine(estate.Root, missing));
        AssertReceipt(Run(estate.Root, json: true), true,
            missing == "bootstrap" ? "written" : "unchanged", missing == "bootstrap.ps1" ? "written" : "unchanged");
        AssertInstalled(estate.Root);
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("bootstrap.ps1")]
    public void RefreshingADifferingCopyLeavesItsIdenticalCompanionUntouched(string changed)
    {
        using var estate = new TestEstate(3);
        AssertReceipt(Run(estate.Root, json: true), true, "written", "written");
        string other = changed == "bootstrap" ? "bootstrap.ps1" : "bootstrap";
        string companion = Path.Combine(estate.Root, other);
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(companion, timestamp);
        estate.WriteFile(changed, "# Estate customization.\n");

        CliHarness.Outcome refused = Run(estate.Root, json: true);
        Assert.Equal(2, refused.Code);
        Assert.Empty(refused.Out);
        Assert.Equal("# Estate customization.\n", File.ReadAllText(Path.Combine(estate.Root, changed)));
        Assert.Equal(ShippedBytes(other), File.ReadAllBytes(companion));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(companion));

        AssertReceipt(Run(estate.Root, json: true, force: true), true,
            changed == "bootstrap" ? "written" : "unchanged", changed == "bootstrap.ps1" ? "written" : "unchanged");
        AssertInstalled(estate.Root);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(companion));
    }

    [Theory]
    [InlineData("bootstrap", false)]
    [InlineData("bootstrap.ps1", false)]
    [InlineData("bootstrap", true)]
    [InlineData("bootstrap.ps1", true)]
    public void DirectoryIsRefusedEvenWithForceBeforeWritingEitherScript(string name, bool force)
    {
        using var estate = new TestEstate(3);
        Directory.CreateDirectory(Path.Combine(estate.Root, name));
        estate.WriteFile(name + "/keep.txt", "Keep this file.\n");
        string[] before = EstateFiles(estate.Root);
        CliHarness.Outcome refused = Run(estate.Root, json: true, force);
        Assert.Equal(2, refused.Code);
        Assert.Empty(refused.Out);
        Assert.Contains(name + " is a link or directory", refused.Err, StringComparison.Ordinal);
        Assert.Equal(before, EstateFiles(estate.Root));
        Assert.False(File.Exists(Path.Combine(estate.Root, name == "bootstrap" ? "bootstrap.ps1" : "bootstrap")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingEstateRefusesWithoutCreatingFiles(bool force)
    {
        string root = CliHarness.TempDirectory("bootstrap-no-estate");
        try
        {
            CliHarness.Outcome refused = Run(root, json: true, force);
            Assert.Equal(2, refused.Code);
            Assert.Empty(refused.Out);
            Assert.Contains("No estate/manifest.json", refused.Err, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { CliHarness.Delete(root); }
    }

    internal static byte[] ShippedBytes(string name)
    {
        using Stream? resource = typeof(CliApp).Assembly.GetManifestResourceStream("Vericairn.Cli.Bootstrap." + name);
        Assert.NotNull(resource);
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return bytes.ToArray();
    }

    internal static void AssertInstalled(string root)
    {
        foreach (string name in new[] { "bootstrap", "bootstrap.ps1" })
        {
            byte[] written = File.ReadAllBytes(Path.Combine(root, name));
            Assert.Equal(ShippedBytes(name), written);
            Assert.DoesNotContain((byte)'\r', written);
        }
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            Assert.Equal(execute, File.GetUnixFileMode(Path.Combine(root, "bootstrap")) & execute);
        }
    }

    private static CliHarness.Outcome Run(string root, bool json, bool force = false) =>
        CliHarness.Run(["--estate", root, .. json ? JsonOption : [], "estate", "bootstrap", .. force ? ForceOption : []]);

    private static void AssertReceipt(CliHarness.Outcome outcome, bool json, string shellStatus, string powershellStatus)
    {
        Assert.Equal(0, outcome.Code);
        Assert.Empty(outcome.Err);
        if (json)
        {
            Assert.Equal(["bootstrap"], outcome.Json.EnumerateObject().Select(p => p.Name));
            JsonElement[] files = outcome.Json.GetProperty("bootstrap").EnumerateArray().ToArray();
            Assert.Equal(["bootstrap", "bootstrap.ps1"], files.Select(f => f.GetProperty("path").GetString()));
            Assert.Equal([shellStatus, powershellStatus], files.Select(f => f.GetProperty("status").GetString()));
            Assert.All(files, file => Assert.Equal(["path", "status"], file.EnumerateObject().Select(p => p.Name)));
        }
        else Assert.Equal($"{shellStatus,-9} bootstrap\n{powershellStatus,-9} bootstrap.ps1\n", outcome.Out);
    }

    private static string[] EstateFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetRelativePath(root, path) is not ("bootstrap" or "bootstrap.ps1"))
        .Select(path => Path.GetRelativePath(root, path) + " " + ContentHash.Sha256File(path)).Order(StringComparer.Ordinal).ToArray();
}
