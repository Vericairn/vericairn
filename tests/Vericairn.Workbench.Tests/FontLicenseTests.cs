// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Workbench.Tests;

/// <summary>
/// The fonts Workbench embeds travel with their licenses. The SIL Open Font License lets the fonts be bundled with software
/// provided each copy contains their copyright notice and the license where the user can read them; 0.4.0 shipped them only
/// inside the executable, which the independent review of its candidate found.
/// </summary>
public class FontLicenseTests
{
    [Fact]
    public void EveryEmbeddedFontHasItsLicenseInTheNoticesAndInTheWorkbenchArchive()
    {
        string root = RepositoryRoot();
        string fonts = Path.Combine(root, "src", "Vericairn.Workbench", "Assets", "Fonts");
        string notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        string[] families = Directory.EnumerateFiles(fonts, "*.ttf").Select(f => Path.GetFileNameWithoutExtension(f).Split('-')[0]).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Inter", "JetBrainsMono"], families);
        foreach (string family in families)
        {
            string license = Path.Combine(fonts, $"OFL-{family}.txt");
            Assert.True(File.Exists(license), license);
            string copyright = File.ReadLines(license).First();
            Assert.StartsWith("Copyright", copyright, StringComparison.Ordinal);
            Assert.Contains(copyright.Replace("Copyright (c) ", string.Empty, StringComparison.Ordinal).Replace("Copyright ", string.Empty, StringComparison.Ordinal), notices, StringComparison.Ordinal);
            Assert.Contains($"`licenses/OFL-{family}.txt` in every Workbench archive", notices, StringComparison.Ordinal);
            Assert.Contains($"\"out-workbench/$rid/licenses/OFL-{family}.txt\"", workflow, StringComparison.Ordinal);
        }

        Assert.Contains("cp src/Vericairn.Workbench/Assets/Fonts/OFL-*.txt \"out-workbench/$rid/licenses/\"", workflow, StringComparison.Ordinal);
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
