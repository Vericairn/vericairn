// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class WorkspaceMetadataTests
{
    [Theory]
    [InlineData("directory")]
    [InlineData("brief.md")]
    [InlineData(".gitignore")]
    [InlineData("dangling")]
    [InlineData("nested")]
    public void MetadataRefusesEveryLinkAndNeverOverwritesItsTarget(string kind)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-metadata-" + Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(scratch, "workspace");
        string outside = Path.Combine(scratch, "outside");
        string own = Path.Combine(workspace, ".vericairn");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "victim");
        File.WriteAllText(victim, "untouched");
        try
        {
            try
            {
                if (kind == "directory")
                {
                    Directory.CreateSymbolicLink(own, outside);
                }
                else
                {
                    Directory.CreateDirectory(own);
                    if (kind == "nested")
                    {
                        Directory.CreateDirectory(Path.Combine(own, "nested"));
                        Directory.CreateSymbolicLink(Path.Combine(own, "nested", "link"), outside);
                    }
                    else
                    {
                        File.CreateSymbolicLink(Path.Combine(own, kind == "dangling" ? "brief.md" : kind), kind == "dangling" ? Path.Combine(outside, "missing") : victim);
                    }
                }
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
            {
                Assert.Skip("Creating Windows symbolic links requires Developer Mode or the symlink privilege.");
            }

            Assert.ThrowsAny<IOException>(() => WorkspaceMetadata.Write(workspace, "must not reach outside"));
            Assert.Equal("untouched", File.ReadAllText(victim));
            Assert.False(File.Exists(Path.Combine(outside, "brief.md")));
            Assert.False(File.Exists(Path.Combine(outside, "missing")));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void RegularAndHiddenMetadataCanBeReplacedWithoutLeavingTemporaryFiles()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "vericairn-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            string brief = WorkspaceMetadata.Write(workspace, "first");
            Assert.Equal("first", File.ReadAllText(brief));
            if (OperatingSystem.IsWindows())
            {
                foreach (string path in Directory.EnumerateFiles(Path.GetDirectoryName(brief)!))
                {
                    File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.System);
                }
            }

            Assert.Equal(brief, WorkspaceMetadata.Write(workspace, "second"));
            Assert.Equal("second", File.ReadAllText(brief));
            Assert.Equal([".gitignore", "brief.md"], Directory.EnumerateFiles(Path.GetDirectoryName(brief)!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
