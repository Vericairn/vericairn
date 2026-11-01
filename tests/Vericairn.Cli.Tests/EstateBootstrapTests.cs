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
    internal const string WrittenJson = """[{"path":"bootstrap","status":"written"},{"path":"bootstrap.ps1","status":"written"},{"path":".gitattributes","status":"written"},{"path":".gitignore","status":"written"}]""";
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
        AssertReceipt(written, json, "written", "written", "written");
        AssertInstalled(estate.Root);
        Assert.Equal(before, EstateFiles(estate.Root));
        Assert.Empty(EstateChecks.Run(estate.Load()));

        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (string name in new[] { "bootstrap", "bootstrap.ps1" })
            File.SetLastWriteTimeUtc(Path.Combine(estate.Root, name), timestamp);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(estate.Root, "bootstrap"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AssertReceipt(Run(estate.Root, json), json, "unchanged", "unchanged", "unchanged");
        AssertReceipt(Run(estate.Root, json, force: true), json, "unchanged", "unchanged", "unchanged");
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

        AssertReceipt(Run(estate.Root, json, force: true), json, "written", "written", "written");
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
        AssertReceipt(Run(estate.Root, json: true), true, "written", "written", "written");
        File.Delete(Path.Combine(estate.Root, missing));
        AssertReceipt(Run(estate.Root, json: true), true,
            missing == "bootstrap" ? "written" : "unchanged", missing == "bootstrap.ps1" ? "written" : "unchanged", "unchanged");
        AssertInstalled(estate.Root);
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("bootstrap.ps1")]
    public void RefreshingADifferingCopyLeavesItsIdenticalCompanionUntouched(string changed)
    {
        using var estate = new TestEstate(3);
        AssertReceipt(Run(estate.Root, json: true), true, "written", "written", "written");
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
            changed == "bootstrap" ? "written" : "unchanged", changed == "bootstrap.ps1" ? "written" : "unchanged", "unchanged");
        AssertInstalled(estate.Root);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(companion));
    }

    [Fact]
    public void AnEstateClonedWithWindowsLineEndingConversionPassesItsCheck()
    {
        // Git for Windows checks text out with CRLF by default (core.autocrlf=true); the attributes estate init writes keep every
        // record, object and document LF, so a fresh clone passes the same check as the estate it came from.
        string root = CliHarness.TempDirectory("crlf-source");
        string parent = CliHarness.TempDirectory("crlf-parent");
        string clone = Path.Combine(parent, "clone");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Line endings.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567");
            ContextJourneyTests.Git(root, "init", "-q");
            ContextJourneyTests.Git(root, "add", ".");
            ContextJourneyTests.Git(root, "commit", "-qm", "Initialize");
            ContextJourneyTests.Git(parent, "-c", "core.autocrlf=true", "clone", "-q", root, clone);
            Assert.DoesNotContain((byte)'\r', File.ReadAllBytes(Path.Combine(clone, "estate", "manifest.json")));
            CliHarness.Outcome check = CliHarness.Run("--estate", clone, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
            Assert.DoesNotContain("formatting", check.Out, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(root, parent); }
    }

    [Fact]
    public void ExistingAttributesKeepTheirLinesAndGainOnlyTheMissingOnes()
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(".gitattributes", "* text=auto eol=lf\nbootstrap.ps1 text eol=lf");
        AssertReceipt(Run(estate.Root, json: true), true, "written", "written", "written");
        Assert.Equal("* text=auto eol=lf\nbootstrap.ps1 text eol=lf\n*.json text eol=lf\n*.md text eol=lf\nbootstrap text eol=lf\n", File.ReadAllText(Path.Combine(estate.Root, ".gitattributes")));
        Assert.Equal(".vericairn/\n", File.ReadAllText(Path.Combine(estate.Root, ".gitignore")));
        AssertReceipt(Run(estate.Root, json: true), true, "unchanged", "unchanged", "unchanged");
        AssertInstalled(estate.Root);
        Assert.Empty(EstateChecks.Run(estate.Load()));
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
        // Git for Windows converts text to CRLF on checkout by default; the estate's attributes keep both scripts LF.
        string[] attributes = File.ReadAllLines(Path.Combine(root, ".gitattributes"));
        Assert.Contains("bootstrap text eol=lf", attributes);
        Assert.Contains("bootstrap.ps1 text eol=lf", attributes);
        // ... and every record, object and document LF too, so their canonical bytes and hashes survive a Windows checkout.
        Assert.Contains("* text=auto eol=lf", attributes);
        Assert.Contains("*.json text eol=lf", attributes);
        Assert.Contains("*.md text eol=lf", attributes);
        Assert.Contains(".vericairn/", File.ReadAllLines(Path.Combine(root, ".gitignore")));
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            Assert.Equal(execute, File.GetUnixFileMode(Path.Combine(root, "bootstrap")) & execute);
        }
    }

    private static CliHarness.Outcome Run(string root, bool json, bool force = false) =>
        CliHarness.Run(["--estate", root, .. json ? JsonOption : [], "estate", "bootstrap", .. force ? ForceOption : []]);

    private static void AssertReceipt(CliHarness.Outcome outcome, bool json, string shellStatus, string powershellStatus, string attributesStatus, string? ignoreStatus = null)
    {
        ignoreStatus ??= attributesStatus;
        Assert.Equal(0, outcome.Code);
        Assert.Empty(outcome.Err);
        if (json)
        {
            Assert.Equal(["bootstrap"], outcome.Json.EnumerateObject().Select(p => p.Name));
            JsonElement[] files = outcome.Json.GetProperty("bootstrap").EnumerateArray().ToArray();
            Assert.Equal(["bootstrap", "bootstrap.ps1", ".gitattributes", ".gitignore"], files.Select(f => f.GetProperty("path").GetString()));
            Assert.Equal([shellStatus, powershellStatus, attributesStatus, ignoreStatus], files.Select(f => f.GetProperty("status").GetString()));
            Assert.All(files, file => Assert.Equal(["path", "status"], file.EnumerateObject().Select(p => p.Name)));
        }
        else Assert.Equal($"{shellStatus,-9} bootstrap\n{powershellStatus,-9} bootstrap.ps1\n{attributesStatus,-9} .gitattributes\n{ignoreStatus,-9} .gitignore\n", outcome.Out);
    }

    private static string[] EstateFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetRelativePath(root, path) is not ("bootstrap" or "bootstrap.ps1" or ".gitattributes" or ".gitignore"))
        .Select(path => Path.GetRelativePath(root, path) + " " + ContentHash.Sha256File(path)).Order(StringComparer.Ordinal).ToArray();
}
