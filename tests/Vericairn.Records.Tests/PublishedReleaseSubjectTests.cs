// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Tests;

public sealed class PublishedReleaseSubjectTests
{
    [Fact]
    public async Task CharterRevisionsKeepThePublishedPinAndExistingRecordBytesUnchanged()
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        VerifiedRelease release = await fixture.VerifyAsync();
        string root = Path.Combine(scratch.Root, "fresh");
        EstateInitializer.Initialize(root, new EstateInitialization("Example", "example", "Use a published dependency.", "Operator", "Example", null,
            VerifiedRelease.Repository, PublishedReleaseFixture.Commit) { Release = release });
        EstateModel model = EstateLoader.Load(root);
        RecordEnvelope original = model.CurrentEstate!;
        RecordEnvelope observation = Assert.Single(model.OfType("observation"));
        byte[] manifest = File.ReadAllBytes(Path.Combine(root, EstateLayout.ManifestPath));
        Dictionary<string, string> records = EstateLoader.EnumerateRecordFiles(root).ToDictionary(file => file.Path, file => ContentHash.Sha256(file.Bytes));
        DateTimeOffset at = model.Records.Max(record => record.AtTime);

        for (int revision = 1; revision <= 2; revision++)
        {
            JsonObject body = JsonNode.Parse(model.CurrentEstate!.Body.GetRawText())!.AsObject();
            body["purpose"] = $"Revised purpose {revision}.";
            AuthoredRecord charter = RecordAuthor.Create(new("estate", original.By, JsonSerializer.SerializeToElement(body),
                Supersedes: model.CurrentEstate.Id, At: at.AddSeconds(revision), Format: model.Manifest!.RecordFormat));
            RecordAuthor.Write(root, charter);

            model = EstateLoader.Load(root);
            Assert.Empty(model.Findings);
            Assert.Empty(EstateChecks.Run(model));
            Assert.Equal(charter.Envelope.Id, model.CurrentEstate!.Id);
            Assert.Equal(3 + revision, model.Records.Count);
            Assert.Equal(observation.Reference, model.Manifest!.Release);
            Assert.Equal(original.Reference, model.ById[observation.Id].Subject);
            Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(root, EstateLayout.ManifestPath)));
            Assert.All(records, pair => Assert.Equal(pair.Value, ContentHash.Sha256File(Path.Combine(root, pair.Key))));
            Assert.All(model.Records, record => Assert.Equal(3, record.Format));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReleaseObservationsOfAnyCharterRevisionStayCurrentAtEveryRecordFormat(long format)
    {
        using var estate = new TestEstate(format);
        using var fixture = new PublishedReleaseFixture();
        string body = (await fixture.VerifyAsync()).ObservationBody().GetRawText();
        estate.Add("observation", estate.OperatorId, body, estate.Estate.Reference);
        RecordEnvelope revised = estate.Add("estate", estate.OperatorId, estate.Estate.Body.GetRawText(), supersedes: estate.Estate.Id);
        Assert.Empty(estate.Load().Findings);

        estate.Add("observation", estate.OperatorId, body, revised.Reference);
        estate.Add("estate", estate.OperatorId, revised.Body.GetRawText(), supersedes: revised.Id);
        Assert.Empty(estate.Load().Findings);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("release")]
    [InlineData("workflow-run")]
    [InlineData("estate")]
    public async Task OtherObservationsAndReviewsStillWarnAboutEarlierCharterRevisions(string kind)
    {
        using var estate = new TestEstate(3);
        using var fixture = new PublishedReleaseFixture();
        estate.Add("observation", estate.OperatorId, (await fixture.VerifyAsync()).ObservationBody().GetRawText(), estate.Estate.Reference);
        string hash = "sha256:" + new string('a', 64);
        RecordEnvelope observation = estate.Add("observation", estate.OperatorId, JsonSerializer.Serialize(new
        {
            kind, asOf = estate.Estate.At, summary = "Observed this charter revision.",
            facts = new { records = 3, recordSetHash = hash, pinHash = hash, statusHash = hash }, limitations = Array.Empty<string>(),
        }), estate.Estate.Reference);
        RecordEnvelope review = estate.Add("review", estate.OperatorId,
            """{"independence":"self","outcome":"pass","summary":"Reviewed this charter revision.","findings":[]}""", estate.Estate.Reference);
        RecordEnvelope current = estate.Estate;
        for (int revision = 0; revision < 2; revision++)
        {
            current = estate.Add("estate", estate.OperatorId, current.Body.GetRawText(), supersedes: current.Id);
            EstateModel model = estate.Load();
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.Equal(new[] { observation.ExpectedPath, review.ExpectedPath }.Order(StringComparer.Ordinal),
                model.Findings.Where(finding => finding.Code == "record.subject-stale" && finding.Severity == Severity.Warning)
                    .Select(finding => finding.Location).Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("estate")]
    public async Task ReleaseObservationsOfOtherSubjectsStillWarn(string type)
    {
        using var estate = new TestEstate(3);
        using var fixture = new PublishedReleaseFixture();
        RecordEnvelope subject = type == "principal" ? estate.Builder : estate.Add("estate", estate.OperatorId, estate.Estate.Body.GetRawText());
        RecordEnvelope observation = estate.Add("observation", estate.OperatorId, (await fixture.VerifyAsync()).ObservationBody().GetRawText(), subject.Reference);
        estate.Add(type, estate.OperatorId, subject.Body.GetRawText(), supersedes: subject.Id);
        EstateModel model = estate.Load();
        Assert.Equal(estate.Estate.Id, model.CurrentEstate!.Id);
        Assert.Contains(model.Findings, finding => finding.Code == "record.subject-stale" && finding.Severity == Severity.Warning && finding.Location == observation.ExpectedPath);
        Assert.Contains(model.Findings, finding => finding.Severity == Severity.Error && finding.Code ==
            (type == "principal" ? "observation.vericairn-release" : "estate.multiple"));
    }

    [Theory]
    [InlineData("hash", "record.subject-hash")]
    [InlineData("type", "record.subject")]
    public async Task CharterAnchorsStillValidateTheirExactSubject(string fault, string code)
    {
        using var estate = new TestEstate(3);
        using var fixture = new PublishedReleaseFixture();
        RecordReference subject = fault == "hash"
            ? estate.Estate.Reference with { Hash = "sha256:" + new string('a', 64) }
            : estate.Estate.Reference with { Type = "principal" };
        RecordEnvelope observation = estate.Add("observation", estate.OperatorId, (await fixture.VerifyAsync()).ObservationBody().GetRawText(), subject);
        estate.Add("estate", estate.OperatorId, estate.Estate.Body.GetRawText(), supersedes: estate.Estate.Id);
        Assert.Contains(estate.Load().Findings, finding => finding.Code == code && finding.Severity == Severity.Error && finding.Location == observation.ExpectedPath);
    }
}
