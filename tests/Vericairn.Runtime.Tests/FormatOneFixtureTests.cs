// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>A format-1 estate written by the released 0.4.2 binary, kept byte for byte, read and moved to format 2 by this build.</summary>
public class FormatOneFixtureTests
{
    [Fact]
    public void TheReleasedFormatOneEstateIsReadUnchangedAndMovesToFormat2WithoutARewrite()
    {
        string copy = Path.Combine(Path.GetTempPath(), "vericairn-format-1-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(Path.Combine(RepositoryRoot(), "tests", "fixtures", "format-1-estate"), copy);
            string records = Path.Combine(copy, "records");
            Dictionary<string, byte[]> before = Directory.EnumerateFiles(records, "*.json", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
            Assert.Equal(10, before.Count);

            // Read as it was written: every record is format 1, canonical, and valid under format 1's rules.
            EstateModel model = EstateLoader.Load(copy);
            Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
            Assert.All(model.Records, r => Assert.Equal(1, r.Format));
            Assert.All(before, file => Assert.Equal(file.Value, RecordAuthor.Format(file.Value)));
            Assert.Equal(1, model.Manifest!.RecordFormat);
            string op = model.CurrentEstate!.Body.GetProperty("operator").GetString()!;

            using var service = EstateService.Open(copy);
            (UpgradeResponse? check, Problem? refused) = service.Upgrade(new UpgradeRequest("0.9.0", null, true));
            Assert.Null(refused);
            Assert.Equal(["record-format-2"], check!.Migrations);
            (UpgradeResponse? applied, Problem? problem) = service.Upgrade(new UpgradeRequest("0.9.0", op, false));
            Assert.Null(problem);
            Assert.True(applied!.Applied);

            // Moved without a rewrite: the 0.4.2 records are byte for byte what they were, and the one new record is format 2.
            Dictionary<string, byte[]> after = Directory.EnumerateFiles(records, "*.json", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
            Assert.All(before, file => Assert.Equal(file.Value, after[file.Key]));
            Assert.Equal(before.Count + 1, after.Count);
            EstateModel moved = service.Session.Load();
            Assert.Equal(2, moved.Manifest!.RecordFormat);
            Assert.DoesNotContain(moved.Findings, f => f.Severity == Severity.Error);
            Assert.Equal(2, moved.Current("migration").Single().Format);
            Assert.Contains("record format 2", service.Status().Text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(copy))
            {
                Directory.Delete(copy, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal));
        }
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        return root;
    }
}
