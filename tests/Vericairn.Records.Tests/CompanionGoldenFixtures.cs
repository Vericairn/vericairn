// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Tests;

/// <summary>Exact rendition bytes retained under their implementation version, never refreshed by a test run.</summary>
public static class CompanionGoldenFixtures
{
    public static void Check(DocumentGeneratorDescriptor descriptor, IReadOnlyDictionary<string, DocumentRendition> cases)
    {
        string root = FixtureRoot(descriptor);
        Assert.True(Directory.Exists(root), "The implementation version needs its own reviewed golden-byte fixtures: " + descriptor.Implementation);
        Assert.Equal(cases.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal));
        foreach ((string name, DocumentRendition rendition) in cases)
        {
            Validate(rendition);
            Assert.True(File.ReadAllBytes(Path.Combine(root, name)).AsSpan().SequenceEqual(rendition.Bytes.AsSpan()),
                "Golden bytes changed for " + descriptor.Implementation + "/" + name + ". Version changes to emitted bytes or meaning before generating new fixtures.");
        }
    }

    /// <summary>Test-fixture maintenance only: an existing implementation version cannot be overwritten.</summary>
    public static void WriteNewVersion(DocumentGeneratorDescriptor descriptor, IReadOnlyDictionary<string, DocumentRendition> cases)
    {
        string root = FixtureRoot(descriptor);
        if (Directory.Exists(root)) throw new InvalidOperationException("Retain existing golden bytes; bump the affected implementation version first.");
        Assert.NotEmpty(cases);
        foreach (DocumentRendition rendition in cases.Values) Validate(rendition);
        foreach ((string name, DocumentRendition rendition) in cases)
        {
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(rendition.Bytes.AsSpan());
        }
    }

    private static string FixtureRoot(DocumentGeneratorDescriptor descriptor)
    {
        Assert.Matches("^vericairn\\.[a-z-]+/[a-z-]+/[1-9][0-9]*$", descriptor.Implementation);
        return Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "readable-companions", descriptor.Implementation);
    }

    private static void Validate(DocumentRendition rendition)
    {
        Assert.Equal(DocumentGenerationStatus.Success, rendition.Status);
        Assert.Null(rendition.Reason);
        Assert.NotNull(rendition.InputDigest);
        Assert.Equal(ContentHash.Sha256(rendition.Bytes.AsSpan()), rendition.ContentHash);
    }
}
