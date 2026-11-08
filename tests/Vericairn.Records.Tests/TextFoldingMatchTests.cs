// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Tests;

/// <summary>
/// Held work and blocked terms are matched across case and Unicode compatibility forms in the product's own globalization mode, which
/// this test assembly shares (invariant): full-width letters, ligatures and other compatibility characters read the same as the
/// text they stand for, so they never hide a match, and they never make unrelated text match.
/// </summary>
public sealed class TextFoldingMatchTests
{
    private const string Intent = "Fix the offline profile flow so staff files reach their final folder.";

    private static readonly string[] TwiceOffice = ["office", "ＯＦＦＩＣＥ"];

    private static readonly ImmutableArray<string> Terms =
        BlockedTerms.TermsOf(JsonSerializer.SerializeToElement(new { terms = new[] { "Tern", "orbit-7-lens", "office" } }));

    // The premise of every test here: the platform's own normalization does nothing in this mode, as in the shipped build.
    [Fact]
    public void ThisAssemblyRunsInTheProductsInvariantGlobalizationMode()
    {
        Assert.True(AppContext.TryGetSwitch("System.Globalization.Invariant", out bool invariant) && invariant);
        Assert.Equal("ﬁ", "ﬁ".Normalize(System.Text.NormalizationForm.FormKC));
    }

    [Theory]
    [InlineData("Implements FIX-90.")]
    [InlineData("Implements ＦＩＸ－９０.")]
    [InlineData("Implements 𝐅𝐈𝐗-𝟗𝟎.")]
    [InlineData("Implements ⒻⒾⓍ-⑨⓪.")]
    [InlineData("Implements ﬁx-90.")]
    [InlineData("Fix the oﬄine proﬁle ﬂow so staﬀ ﬁles reach their ﬁnal folder")]
    [InlineData("ＦＩＸ ＴＨＥ ＯＦＦＬＩＮＥ ＰＲＯＦＩＬＥ ＦＬＯＷ so staff files reach their final folder.")]
    [InlineData("fix the offline profile flow so　staff files reach their final folder")]
    public void HeldWorkIsFoundInEveryCompatibilityForm(string text)
    {
        IReadOnlyList<DisclosureHolds.Hold> holds = Holds();
        Assert.Equal("FIX-90", DisclosureHolds.Disclosed(holds, text));
    }

    [Theory]
    [InlineData("Implements FIX-901 and fix-9.")]
    [InlineData("Implements ＦＩＸ－９１.")]
    [InlineData("A ﬁle in the oﬃce, nothing held.")]
    [InlineData("Fix the offline profile.")]
    public void UnrelatedTextIsNotMatchedInAnyForm(string text) => Assert.Null(DisclosureHolds.Disclosed(Holds(), text));

    [Theory]
    [InlineData("A Tern session wrote this.", "tern")]
    [InlineData("Ｔｅｒｎ in full-width form", "tern")]
    [InlineData("𝐓𝐞𝐫𝐧 in mathematical bold", "tern")]
    [InlineData("Ⓣⓔⓡⓝ in circled letters", "tern")]
    [InlineData("ran on ｏｒｂｉｔ－７－ｌｅｎｓ.", "orbit-7-lens")]
    [InlineData("the back oﬃce", "office")]
    [InlineData("THE BACK OFFICE", "office")]
    [InlineData("Lanterns, terne plate, intern", null)]
    [InlineData("ｉｎｔｅｒｎ and ｌａｎｔｅｒｎ", null)]
    [InlineData("an oﬃcer", null)]
    public void BlockedTermsMatchWholeInEveryCompatibilityForm(string text, string? expected) =>
        Assert.Equal(expected, BlockedTerms.Introduced(Terms, "", text));

    [Fact]
    public void ATermAlreadyHeldInACompatibilityFormIsNotIntroducedAgain()
    {
        Assert.Null(BlockedTerms.Introduced(Terms, "kept Ｔｅｒｎ", "kept Tern, reworded"));
        Assert.Equal("tern", BlockedTerms.Introduced(Terms, "kept Ｔｅｒｎ", "kept Tern and 𝐓𝐞𝐫𝐧"));
    }

    [Fact]
    public void AListCannotDeclareOneTermTwiceInCompatibilityForms() =>
        Assert.Equal("Blocked terms are unique, ignoring case.", BlockedTerms.Validate(JsonSerializer.SerializeToElement(new { terms = TwiceOffice })));

    private static IReadOnlyList<DisclosureHolds.Hold> Holds()
    {
        using var estate = new TestEstate(3);
        estate.Add("work-order", estate.OperatorId, $$"""
            {"key":"FIX-90","kind":"ordinary","title":"Profile flow","intent":"{{Intent}}","scope":["The fix"],"exitQuestion":"Fixed?","inputs":[],"deliverables":["The fix"],"disclosure":"held"}
            """);
        estate.Add("work-order", estate.OperatorId, """
            {"key":"CODE-99","kind":"ordinary","title":"Another step","intent":"Something else entirely, for another reason.","scope":["The fix"],"exitQuestion":"Done?","inputs":[],"deliverables":["Done"]}
            """);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        DisclosureHolds.Hold hold = Assert.Single(DisclosureHolds.Active(model));
        Assert.Contains(hold.Phrases, p => p.StartsWith("fix the offline profile flow", StringComparison.Ordinal));
        return [hold];
    }
}
