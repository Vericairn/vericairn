// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

/// <summary>
/// Every component the distributables bundle, packages and shared frameworks alike, is named in the notices with the license text its archive carries. Until 0.4.2
/// the notices named the MIT and Apache-2.0 components without their texts, and 0.4.0 shipped two embedded fonts without the
/// license their own terms require each copy to carry.
/// </summary>
public partial class LicenseTests
{
    private static readonly string[] NotDistributed = ["Avalonia.BuildServices"];

    [Fact]
    public void EveryShippedPackageIsInTheNoticesWithALicenseTextTheArchivesCarry()
    {
        string root = RepositoryRoot();
        string notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        var shipped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string project in new[] { "Vericairn.Cli", "Vericairn.Workbench" })
        {
            using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", project, "packages.lock.json")));
            foreach (JsonProperty framework in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (JsonProperty package in framework.Value.EnumerateObject())
                {
                    bool isProject = package.Value.TryGetProperty("type", out JsonElement type) && type.GetString() == "Project";
                    if (!isProject && !NotDistributed.Contains(package.Name) && !package.Name.EndsWith(".WebAssembly", StringComparison.Ordinal))
                    {
                        shipped.Add(package.Name);
                    }
                }
            }
        }

        // Shared frameworks are not in the lock files: a project that references one bundles it in a self-contained build.
        foreach (string project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (Match reference in FrameworkReference().Matches(File.ReadAllText(project)))
            {
                shipped.Add(reference.Groups["name"].Value);
            }
        }

        Assert.Contains("Microsoft.AspNetCore.App", shipped);
        Assert.Contains("System.CommandLine", shipped);
        Assert.Contains("Avalonia", shipped);
        string[] missing = shipped.Where(name => !Regex.IsMatch(notices, $@"(?<![\w.]){Regex.Escape(name)}(?![\w.])")).ToArray();
        Assert.True(missing.Length == 0, "Not named in THIRD-PARTY-NOTICES.md: " + string.Join(", ", missing));

        string licenses = Path.Combine(root, "licenses");
        string fonts = Path.Combine(root, "src", "Vericairn.Workbench", "Assets", "Fonts");
        var named = LicensePath().Matches(notices).Select(m => m.Groups["file"].Value).ToHashSet(StringComparer.Ordinal);
        foreach (string file in named)
        {
            bool present = File.Exists(Path.Combine(licenses, file)) || File.Exists(Path.Combine(licenses, "workbench", file)) || File.Exists(Path.Combine(fonts, file));
            Assert.True(present, $"THIRD-PARTY-NOTICES.md names licenses/{file}, which the repository does not hold.");
        }

        foreach (string file in Directory.EnumerateFiles(licenses, "*.txt", SearchOption.AllDirectories))
        {
            Assert.True(named.Contains(Path.GetFileName(file)), $"{Path.GetRelativePath(root, file)} is not named in THIRD-PARTY-NOTICES.md.");
            Assert.True(new FileInfo(file).Length > 0, file);
        }

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        Assert.Contains("cp licenses/*.txt \"out/$rid/licenses/\"", workflow, StringComparison.Ordinal);
        Assert.Contains("cp licenses/*.txt licenses/workbench/*.txt \"out-workbench/$rid/licenses/\"", workflow, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"`licenses/(?<file>[A-Za-z0-9._-]+\.txt)`")]
    private static partial Regex LicensePath();

    [GeneratedRegex("<FrameworkReference\\s+Include=\"(?<name>[^\"]+)\"")]
    private static partial Regex FrameworkReference();

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
