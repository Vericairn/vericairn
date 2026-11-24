// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Vericairn.Adapters.Git;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-168: gitleaks' Sourcegraph rule reads any 40 hexadecimal digits standing alone as a token wherever a fragment names Sourcegraph,
/// so the scan refused 0.9.4's candidate records with 33 findings in six evidence objects, each a git commit id that a release basis
/// records, and the basis listed a file named DocumentSourceGraph.cs. The scan now admits exactly the revisions a release basis records,
/// in the objects that hold that basis's bytes, as it admits a history checkpoint's inventory lines (FIX-150); every other byte and path
/// is scanned as before. Each test scans real commits with the installed gitleaks; skipped without one.
/// </summary>
public sealed partial class ReleaseBasisScanTests : IDisposable
{
    private readonly SecretScanConfigurationTests scans = new();

    // A basis held as one object, and a basis past the object limit held as a split object, its parts and its root, as 0.9.4's were.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARealShapedBasisPasses(bool split)
    {
        byte[] content = ReleaseBasisEvidenceFixture.Basis(split ? Split : Whole);
        (string Path, byte[] Content)[] objects = ReleaseBasisEvidenceFixture.Objects(content);
        Assert.Equal(split ? 3 : 1, objects.Length);
        if (split) AssertEveryPartIsRead(objects);
        SecretScan scan = ScanAdditions([.. objects, Unrelated()]);
        Assert.Equal("passed", scan.Verdict);
        Assert.Equal(0, scan.FindingCount);
        // The scan applied a configuration of its own, recorded by its hash: neither of the configurations without the basis's allowance.
        Assert.Matches(@"; rules sha256:[0-9a-f]{64}$", scan.Version);
        Assert.DoesNotContain(GitleaksScanner.ConfigurationHash, scan.Version, StringComparison.Ordinal);
        Assert.DoesNotContain(GitleaksScanner.ConfigurationWithoutInventoriesHash, scan.Version, StringComparison.Ordinal);
    }

    // The same basis with one secret-shaped value beside its revisions, in its first repository between its identity and its revision:
    // the value is found, once, while the revisions themselves are still admitted. A 40-digit hexadecimal value the basis does not
    // record as a revision is found by the very rule the allowance targets.
    [Theory]
    [InlineData("github token", false)]
    [InlineData("hexadecimal", false)]
    [InlineData("hexadecimal", true)]
    [InlineData("upper-case revision", false)]
    public void TheSameBasisWithASecretShapedValueBesideItsRevisionsIsRefused(string kind, bool split)
    {
        string value = kind switch
        {
            "github token" => SecretScanConfigurationTests.Token(),
            "hexadecimal" => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20)),
            _ => ReleaseBasisEvidenceFixture.Revisions[0].ToUpperInvariant(),
        };
        Assert.DoesNotContain(value, ReleaseBasisEvidenceFixture.Recorded);
        (string Path, byte[] Content)[] objects = ReleaseBasisEvidenceFixture.Objects(ReleaseBasisEvidenceFixture.Basis(split ? Split : Whole, beside: value));
        if (split) AssertEveryPartIsRead(objects);
        SecretScan scan = ScanAdditions(objects);
        Assert.Equal("findings", scan.Verdict);
        Assert.Equal(1, scan.FindingCount);
    }

    // The allowance holds only for the revisions a release basis records, in the objects that hold its bytes: the same bytes anywhere else,
    // under a name that is not their hash, without the root that makes them a basis's parts, or of another format, are scanned as before;
    // and the same revisions in another object pushed beside a basis are found there.
    [Theory]
    [InlineData("docs/basis.json")]
    [InlineData("records/operation/2026-11/basis.json")]
    [InlineData("objects/sub/{0}")]
    [InlineData("objects/{0}.json")]
    [InlineData("estate/objects/{0}")]
    [InlineData("named by another hash")]
    [InlineData("another format")]
    [InlineData("parts without their root")]
    [InlineData("another object beside a basis")]
    public void TheAllowanceDoesNotApplyOutsideTheBasissEvidence(string where)
    {
        byte[] content = ReleaseBasisEvidenceFixture.Basis(Whole, format: where == "another format" ? "vericairn.release-plan/1" : "vericairn.release-basis/3");
        string name = Convert.ToHexStringLower(SHA256.HashData(content));
        SecretScan scan;
        switch (where)
        {
            case "named by another hash":
                scan = ScanAdditions([("objects/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("another"))), content)]);
                break;
            case "another format":
                scan = ScanAdditions([(ReleaseBasisEvidenceFixture.ObjectPath(content), content)]);
                break;
            case "parts without their root":
                (string Path, byte[] Content)[] objects = ReleaseBasisEvidenceFixture.Objects(ReleaseBasisEvidenceFixture.Basis(Split));
                AssertEveryPartIsRead(objects);
                scan = ScanAdditions(objects[..^1]);
                Assert.Equal("findings", scan.Verdict);
                Assert.True(scan.FindingCount > 0);
                return;
            case "another object beside a basis":
                byte[] other = Encoding.UTF8.GetBytes("{\"format\":\"vericairn.example/1\",\"path\":\"src/DocumentSourceGraph.cs\",\"revision\":\""
                    + ReleaseBasisEvidenceFixture.Revisions[0] + "\"}");
                scan = ScanAdditions([(ReleaseBasisEvidenceFixture.ObjectPath(content), content), (ReleaseBasisEvidenceFixture.ObjectPath(other), other)]);
                Assert.Equal("findings", scan.Verdict);
                Assert.Equal(1, scan.FindingCount);
                return;
            default:
                scan = ScanAdditions([(string.Format(System.Globalization.CultureInfo.InvariantCulture, where, name), content)]);
                break;
        }

        Assert.Equal("findings", scan.Verdict);
        Assert.Equal(ReleaseBasisEvidenceFixture.Readings, scan.FindingCount);
    }

    // Files per repository: a basis of a few kilobytes, and one past the object limit, split into two parts and a root.
    private const int Whole = 40;
    private const int Split = 6_000;

    // Each part of a split basis names Sourcegraph and holds a revision the rule reads, so each would be refused without the allowance,
    // as both parts of each of 0.9.4's bases were. The split falls outside every run of hexadecimal digits: a part edge that cut a hash
    // the basis records could leave 40 of its digits alone at the edge, which the allowance does not admit (see FIX-168's report).
    private static void AssertEveryPartIsRead((string Path, byte[] Content)[] objects)
    {
        Assert.True(objects.Length > 2);
        string[] parts = [.. objects[..^1].Select(o => Encoding.UTF8.GetString(o.Content))];
        foreach (string part in parts)
        {
            Assert.Contains("sourcegraph", part, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ReleaseBasisEvidenceFixture.Recorded, revision => part.Contains(revision + "\"", StringComparison.Ordinal));
        }

        for (int i = 1; i < parts.Length; i++)
            Assert.False(EndsWithDigitsAlone().IsMatch(parts[i - 1]) || StartsWithDigitsAlone().IsMatch(parts[i]), "The fixture's split leaves 40 digits alone at a part's edge.");
        Assert.Equal(EstateLayout.MaxObjectBytes, objects[0].Content.LongLength);
    }

    // An ordinary object of the same push, which the scan reads as before.
    private static (string Path, byte[] Content) Unrelated()
    {
        byte[] content = Encoding.UTF8.GetBytes("{\"format\":\"vericairn.example/1\",\"note\":\"A plain object.\"}");
        return (ReleaseBasisEvidenceFixture.ObjectPath(content), content);
    }

    private SecretScan ScanAdditions((string Path, byte[] Content)[] files)
    {
        string basis = scans.Start();
        foreach ((string path, byte[] content) in files)
        {
            string file = Path.Combine(scans.Repository, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, content);
        }

        scans.Git(["add", "--", .. files.Select(f => f.Path)]);
        scans.Git("commit", "-m", "Add objects");
        return scans.Scan(basis, scans.Git("rev-parse", "HEAD"));
    }

    [GeneratedRegex(@"(?<![0-9A-Za-z_])[0-9a-fA-F]{40}\z")]
    private static partial Regex EndsWithDigitsAlone();

    [GeneratedRegex(@"\A[0-9a-fA-F]{40}(?![0-9A-Za-z_])")]
    private static partial Regex StartsWithDigitsAlone();

    public void Dispose() => scans.Dispose();
}
