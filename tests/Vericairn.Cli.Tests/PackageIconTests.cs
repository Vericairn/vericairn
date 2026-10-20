// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace Vericairn.Cli.Tests;

public class PackageIconTests
{
    [Fact]
    public async Task EveryPackedPackageNamesAndEmbedsTheCairnBesideItsReadme()
    {
        string root = RepositoryRoot();
        string output = Path.Combine(root, "artifacts", "package-icons", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            await Pack(root, output);
            byte[] expectedIcon = await File.ReadAllBytesAsync(Path.Combine(root, "brand", "package-icon.png"), TestContext.Current.CancellationToken);
            var ids = new List<string>();
            foreach (string path in Directory.EnumerateFiles(output, "*.nupkg"))
            {
                using ZipArchive package = ZipFile.OpenRead(path);
                ZipArchiveEntry nuspec = Assert.Single(package.Entries, entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
                using Stream nuspecStream = nuspec.Open();
                XDocument document = XDocument.Load(nuspecStream);
                Assert.NotNull(document.Root);
                XNamespace ns = document.Root.Name.Namespace;
                XElement? metadata = document.Root.Element(ns + "metadata");
                Assert.NotNull(metadata);
                ids.Add(Assert.Single(metadata.Elements(ns + "id")).Value);
                Assert.Equal("package-icon.png", Assert.Single(metadata.Elements(ns + "icon")).Value);
                Assert.Equal("README.md", Assert.Single(metadata.Elements(ns + "readme")).Value);
                Assert.NotNull(package.GetEntry("README.md"));
                ZipArchiveEntry icon = Assert.Single(package.Entries, entry => entry.FullName == "package-icon.png");
                using Stream iconStream = icon.Open();
                using var bytes = new MemoryStream();
                await iconStream.CopyToAsync(bytes, TestContext.Current.CancellationToken);
                Assert.Equal(expectedIcon, bytes.ToArray());
            }

            string[] expectedIds = ["dotnet-vc", "Vericairn", "Vericairn.Daemon", "Vericairn.Mcp", "Vericairn.Protocol", "Vericairn.Records", "Vericairn.Runtime", "Vericairn.Sdk"];
            Assert.Equal(expectedIds.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static async Task Pack(string root, string output)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        foreach (string argument in new[] { "pack", "Vericairn.slnx", "--no-build", "--configuration", "Release", "--output", output, "--disable-build-servers", "-m:1", "-warnaserror" })
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
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
