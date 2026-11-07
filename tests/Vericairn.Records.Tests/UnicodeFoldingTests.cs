// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Vericairn.Records.Text;

namespace Vericairn.Records.Tests;

/// <summary>
/// The managed Unicode normalization and case folding, checked in the product's invariant-globalization mode, where the platform's
/// own <see cref="string.Normalize()"/> does nothing, against the Unicode conformance test of the pinned version.
/// </summary>
public class UnicodeFoldingTests
{
    [Fact]
    public void TheseTestsRunInTheProductsInvariantModeWhereThePlatformDoesNotNormalize()
    {
        Assert.True(AppContext.TryGetSwitch("System.Globalization.Invariant", out bool invariant) && invariant);
        Assert.Equal("ﬁ", "ﬁ".Normalize(NormalizationForm.FormKC));
        Assert.Equal("file", UnicodeFolding.Nfkc("ﬁle"));
    }

    [Fact]
    public void NormalizationPassesTheUnicodeConformanceTestOfThePinnedVersion()
    {
        string path = Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "unicode", $"NormalizationTest-{UnicodeFolding.UnicodeVersion}.txt.gz");
        using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress), Encoding.UTF8);
        var listed = new HashSet<int>();
        bool partOne = false;
        int lines = 0;
        var failures = new List<string>();
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.StartsWith('@'))
            {
                partOne = line.StartsWith("@Part1", StringComparison.Ordinal);
                continue;
            }

            string data = line.Split('#')[0].Trim();
            if (data.Length == 0) continue;
            string[] c = data.Split(';').Take(5).Select(Decode).ToArray();
            lines++;
            if (partOne) listed.Add(char.ConvertToUtf32(c[0], 0));
            void Check(string form, string expected, Func<string, string> normalize, params int[] sources)
            {
                foreach (int source in sources)
                {
                    string actual = normalize(c[source]);
                    if (actual != expected && failures.Count < 20) failures.Add($"{form}(c{source + 1}) of {Hex(c[0])}: {Hex(actual)}, expected {Hex(expected)}");
                }
            }

            Check("NFC", c[1], UnicodeFolding.Nfc, 0, 1, 2);
            Check("NFC", c[3], UnicodeFolding.Nfc, 3, 4);
            Check("NFD", c[2], UnicodeFolding.Nfd, 0, 1, 2);
            Check("NFD", c[4], UnicodeFolding.Nfd, 3, 4);
            Check("NFKC", c[3], UnicodeFolding.Nfkc, 0, 1, 2, 3, 4);
            Check("NFKD", c[4], UnicodeFolding.Nfkd, 0, 1, 2, 3, 4);
        }

        Assert.True(lines > 19_000, "The conformance test is incomplete: " + lines.ToString(CultureInfo.InvariantCulture) + " lines.");
        // Every code point the test does not list is unchanged by all four forms.
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF || listed.Contains(cp)) continue;
            string single = char.ConvertFromUtf32(cp);
            if ((UnicodeFolding.Nfc(single) != single || UnicodeFolding.Nfd(single) != single || UnicodeFolding.Nfkc(single) != single || UnicodeFolding.Nfkd(single) != single)
                && failures.Count < 20) failures.Add("unlisted " + Hex(single) + " changed");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData("Straße", "strasse")]
    [InlineData("STRASSE", "strasse")]
    [InlineData("ẞ", "ss")]
    [InlineData("Σσς", "σσσ")]
    [InlineData("İ", "i̇")]
    [InlineData("ﬁ", "fi")]
    [InlineData("Ǆ", "ǆ")]
    [InlineData("ꭰ", "Ꭰ")]
    [InlineData("\U00010400", "\U00010428")]
    [InlineData("ͅ", "ι")]
    public void FullCaseFoldingFoldsMultiCharacterAndSupplementaryMappings(string text, string folded) => Assert.Equal(folded, UnicodeFolding.CaseFold(text));

    [Theory]
    [InlineData("ﬁle Ｆｕｌｌ Café x²", "file full café x2")]
    [InlineData("CAFÉ", "café")]
    [InlineData("ǰ", "ǰ")]
    [InlineData("Å", "å")]
    [InlineData("① ㎡", "1 m2")]
    [InlineData("한국어", "한국어")]
    [InlineData("한", "한")]
    public void NfkcCaseFoldFoldsCaseAndCompatibilityFormsAndRecomposes(string text, string folded) => Assert.Equal(folded, UnicodeFolding.NfkcCaseFold(text));

    [Fact]
    public void NfkcCaseFoldIsStableForEveryCodePoint()
    {
        var unstable = new List<string>();
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF) continue;
            string once = UnicodeFolding.NfkcCaseFold(char.ConvertFromUtf32(cp));
            if (UnicodeFolding.NfkcCaseFold(once) != once && unstable.Count < 20) unstable.Add(Hex(char.ConvertFromUtf32(cp)));
        }

        Assert.True(unstable.Count == 0, "Not stable: " + string.Join(", ", unstable));
    }

    [Fact]
    public void AsciiIsReturnedAsItIsAndLoneSurrogatesPassThrough()
    {
        const string lower = "already lower-case 0.9.2";
        Assert.Same(lower, UnicodeFolding.NfkcCaseFold(lower));
        Assert.Same(lower, UnicodeFolding.Nfkc(lower));
        Assert.Equal("context-02", UnicodeFolding.NfkcCaseFold("CONTEXT-02"));
        Assert.Equal("\uD800xé", UnicodeFolding.NfkcCaseFold("\uD800XÉ"));
        Assert.Equal("a\uDC00", UnicodeFolding.Nfkc("a\uDC00"));
    }

    [Fact]
    public void GeneralCategoriesComeFromThePinnedTables()
    {
        Assert.Equal(UnicodeCategory.UppercaseLetter, UnicodeFolding.GetCategory('A'));
        Assert.Equal(UnicodeCategory.LowercaseLetter, UnicodeFolding.GetCategory('é'));
        Assert.Equal(UnicodeCategory.NonSpacingMark, UnicodeFolding.GetCategory(0x0301));
        Assert.Equal(UnicodeCategory.OtherLetter, UnicodeFolding.GetCategory(0x4E00));
        Assert.Equal(UnicodeCategory.OtherLetter, UnicodeFolding.GetCategory(0xAC00));
        Assert.Equal(UnicodeCategory.PrivateUse, UnicodeFolding.GetCategory(0xE000));
        Assert.Equal(UnicodeCategory.Surrogate, UnicodeFolding.GetCategory(0xD800));
        Assert.Equal(UnicodeCategory.OtherNotAssigned, UnicodeFolding.GetCategory(0x10FFFF));
        Assert.Equal(UnicodeCategory.DecimalDigitNumber, UnicodeFolding.GetCategory(0x1D7CE));
        // Assigned in Unicode 16.0: CYRILLIC CAPITAL LETTER TJE.
        Assert.Equal(UnicodeCategory.UppercaseLetter, UnicodeFolding.GetCategory(0x1C89));
        Assert.Equal(UnicodeCategory.OtherLetter, UnicodeFolding.GetCategory("a\U00020000", 1));
        Assert.Equal(230, UnicodeFolding.CombiningClass(0x0301));
        Assert.Equal(0, UnicodeFolding.CombiningClass('a'));
    }

    private static string Decode(string field) =>
        string.Concat(field.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => char.ConvertFromUtf32(int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));

    private static string Hex(string text)
    {
        var parts = new List<string>();
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            parts.Add(char.ConvertToUtf32(text, i).ToString("X4", CultureInfo.InvariantCulture));
        return string.Join(' ', parts);
    }
}
