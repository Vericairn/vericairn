// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class ResultOutputSchemeTests
{
    public const string Code = "result.output-scheme";
    public const string GitHubLocation = "https://github.com/example/widget/blob/0123456789abcdef0123456789abcdef01234567/widget.txt";
    public const string OrderBody = """{"key":"W-1","kind":"ordinary","title":"Small task","intent":"Exercise result outputs.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";
    private const string Brief = """{"outcome":"A validated widget.","sources":[],"permittedChanges":["the widget sources"],"prohibitedEffects":["no publication"],"workspace":["example repository, main branch"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["output hash verified"],"recovery":"Resume from the last recorded result."}""";
    private const string AttemptBody = """{"startedAt":"2026-10-13T12:00:05Z","workspace":"a clone of example","note":"Build the widget."}""";

    [Theory]
    [InlineData(1, GitHubLocation, "https:")]
    [InlineData(3, GitHubLocation, "https:")]
    [InlineData(3, "file:widget.txt", "file:")]
    [InlineData(3, "s3://bucket/widget.txt", "s3:")]
    [InlineData(3, "git+ssh://example.invalid/widget.txt", "git+ssh:")]
    [InlineData(3, "C:/widget.txt", "C:")]
    public void AnExistingOutputLocationWithASchemeIsAWarningAndKeepsItsBytes(long format, string location, string scheme)
    {
        using var estate = new TestEstate(format);
        RecordEnvelope attempt = StartAttempt(estate);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, Body(location), attempt.Reference);
        string path = Path.Combine(estate.Root, result.ExpectedPath);
        byte[] before = File.ReadAllBytes(path);

        EstateModel model = estate.Load();

        if (Path.IsPathRooted(location))
        {
            // A drive-rooted location was refused as a path before 0.9.2 on Windows, and it still is.
            Assert.Contains(model.Findings, f => f.Code == "result.output-path" && f.Severity == Severity.Error);
            return;
        }
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(result.ExpectedPath + "#body.outputs", finding.Location);
        Assert.Contains($"outputs[0] ({scheme})", finding.Message, StringComparison.Ordinal);
        Assert.Contains("evidence", finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(result.ComputedHash, model.ById[result.Id].ComputedHash);
    }

    [Theory]
    [InlineData("widget.txt")]
    [InlineData("docs/widget.txt")]
    [InlineData("docs/v1:2/widget.txt")]
    [InlineData("1x:widget.txt")]
    [InlineData(":widget.txt")]
    public void RepositoryPathsHaveNoSchemeFinding(string location)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        estate.Add("result", estate.BuilderId, Body(location), attempt.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);
    }

    [Theory]
    [InlineData("/widget.txt")]
    [InlineData("docs/../widget.txt")]
    [InlineData("docs\\widget.txt")]
    [InlineData("https://example.invalid/../widget.txt")]
    public void UnsafePathsKeepTheirPathError(string location)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        estate.Add("result", estate.BuilderId, Body(location), attempt.Reference);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "result.output-path" && f.Severity == Severity.Error);
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);
    }

    [Fact]
    public void EvidenceAndOutputsWithoutALocationMayNameExternalReferences()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        JsonObject body = JsonNode.Parse(Body("widget.txt"))!.AsObject();
        body["outputs"]!.AsArray().Add(new JsonObject { ["name"] = "stored-original", ["hash"] = "sha256:" + new string('b', 64), ["bytes"] = 12 });
        body["evidence"] = new JsonArray(new JsonObject { ["summary"] = "The stored original.", ["location"] = GitHubLocation });
        estate.Add("result", estate.BuilderId, body.ToJsonString(), attempt.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code == Code);
    }

    [Fact]
    public void SeveralSchemeLocationsInOneResultAreOneWarningNamingEach()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        JsonObject body = JsonNode.Parse(Body(GitHubLocation))!.AsObject();
        JsonArray outputs = body["outputs"]!.AsArray();
        outputs.Add(new JsonObject { ["name"] = "notes.md", ["hash"] = "sha256:" + new string('c', 64), ["location"] = "docs/notes.md" });
        outputs.Add(new JsonObject { ["name"] = "guide.md", ["hash"] = "sha256:" + new string('d', 64), ["location"] = "vericairn:docs/guide.md" });
        RecordEnvelope result = estate.Add("result", estate.BuilderId, body.ToJsonString(), attempt.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.Equal(result.ExpectedPath + "#body.outputs", finding.Location);
        Assert.Contains("outputs[0] (https:), outputs[2] (vericairn:)", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("outputs[1]", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASupersededResultStillReportsItsOwnScheme()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        RecordEnvelope earlier = estate.Add("result", estate.BuilderId, Body(GitHubLocation), attempt.Reference);
        estate.Add("result", estate.BuilderId, Body("widget.txt"), attempt.Reference, supersedes: earlier.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == Code);
        Assert.StartsWith(earlier.ExpectedPath + "#", finding.Location, StringComparison.Ordinal);
    }

    /// <summary>An ordinary work order assigned to the builder and the builder's attempt at it.</summary>
    public static RecordEnvelope StartAttempt(TestEstate estate)
    {
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrderBody);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{Brief}}}""", order.Reference);
        return estate.Add("attempt", estate.BuilderId, AttemptBody, assignment.Reference);
    }

    /// <summary>A completed result with one output at a location; the bytes are absent here, so only the location is judged.</summary>
    public static string Body(string location) => JsonSerializer.Serialize(new
    {
        outcome = "completed", summary = "Built the widget.",
        outputs = new[] { new { name = "widget.txt", hash = "sha256:" + new string('a', 64), bytes = 7, location } },
        evidence = Array.Empty<object>(), checks = Array.Empty<object>(), limitations = Array.Empty<string>(),
        unresolvedEffects = Array.Empty<string>(), nextStep = "none",
    });
}
