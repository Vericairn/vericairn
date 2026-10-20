// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>The checked-changes declaration and the repository it names decide where merges land, so only the operator or an administer grant changes them.</summary>
public sealed class ChangesDeclarationAuthorTests
{
    private const string Editions = """ "editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}]""";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OnlyTheOperatorDeclaresAProductsCheckedChanges(bool byOperator, bool refused)
    {
        using var estate = new TestEstate(recordFormat: 3);
        RecordEnvelope repository = Repository(estate, "Example/widget");
        estate.Add("product", byOperator ? estate.OperatorId : estate.BuilderId, Product(repository, ["tests"]));
        Assert.Equal(refused, Errors(estate).Contains("product.changes-author"));
    }

    [Theory]
    [InlineData("same", false)]
    [InlineData("weaker", true)]
    [InlineData("removed", true)]
    public void ABuilderRevisionKeepsTheDeclarationOrIsRefused(string change, bool refused)
    {
        using var estate = new TestEstate(recordFormat: 3);
        RecordEnvelope repository = Repository(estate, "Example/widget");
        RecordEnvelope product = estate.Add("product", estate.OperatorId, Product(repository, ["tests (linux)", "tests (windows)"]));
        string revised = change switch
        {
            "same" => Product(repository, ["tests (linux)", "tests (windows)"], "A revised summary."),
            "weaker" => Product(repository, ["tests (linux)"]),
            _ => $$"""{"name":"Widget","slug":"widget","summary":"A widget.",{{Editions}}}""",
        };
        estate.Add("product", estate.BuilderId, revised, supersedes: product.Id);
        Assert.Equal(refused, Errors(estate).Contains("product.changes-author"));
    }

    [Theory]
    [InlineData("builder", true)]
    [InlineData("operator", false)]
    [InlineData("administer", false)]
    public void OnlyTheOperatorOrAnAdministerGrantMovesARepositorysLocation(string author, bool refused)
    {
        using var estate = new TestEstate(recordFormat: 3);
        RecordEnvelope repository = Repository(estate, "Example/widget");
        if (author == "administer")
        {
            RecordEnvelope decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Administration","statement":"Grant administration.","summary":"Test grant.","synthesis":"A bounded grant.","effects":["Administer the estate."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["publication"]}""");
            estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["administer"],"reserved":["publication"],"conditions":[],"basis":"{{decision.Id}}"}""");
        }
        string by = author == "operator" ? estate.OperatorId : estate.BuilderId;
        estate.Add("resource", by, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/elsewhere"},"visibility":"private"}""", supersedes: repository.Id);
        Assert.Equal(refused, Errors(estate).Contains("resource.location-author"));
    }

    [Fact]
    public void ABuilderMayReviseARepositorysOtherFieldsAndCreateResources()
    {
        using var estate = new TestEstate(recordFormat: 3);
        RecordEnvelope repository = Repository(estate, "Example/widget");
        estate.Add("resource", estate.BuilderId, """{"kind":"repository","name":"Widget","role":"The widget's source.","location":{"github":"Example/widget"},"visibility":"private"}""", supersedes: repository.Id);
        estate.Add("resource", estate.BuilderId, """{"kind":"repository","name":"Gadget","role":"Source","location":{"github":"Example/gadget"},"visibility":"private"}""");
        Assert.DoesNotContain("resource.location-author", Errors(estate));
    }

    private static RecordEnvelope Repository(TestEstate estate, string github) =>
        estate.Add("resource", estate.OperatorId, $$"""{"kind":"repository","name":"Widget","role":"Source","location":{"github":"{{github}}"},"visibility":"private"}""");

    private static string Product(RecordEnvelope repository, string[] checks, string summary = "A widget.") =>
        $$$"""{"name":"Widget","slug":"widget","summary":"{{{summary}}}",{{{Editions}}},"changes":{"provider":"github-pull-requests","repository":"{{{repository.Id}}}","target":"main","requiredChecks":[{{{string.Join(",", checks.Select(c => "\"" + c + "\""))}}}]}}""";

    private static HashSet<string> Errors(TestEstate estate) =>
        EstateLoader.Load(estate.Root).Findings.Where(f => f.Severity == Severity.Error).Select(f => f.Code).ToHashSet(StringComparer.Ordinal);
}
