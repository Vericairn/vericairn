// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class PolicyGoldenFixtureTests
{
    [Fact]
    public void TheGoldenEstateValidatesAndEveryNewRecordAndFieldRoundTripsWithoutLosingBytes()
    {
        string root = FixtureRoot();
        EstateModel model = EstateLoader.Load(root);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(3, model.Manifest!.RecordFormat);
        Assert.Contains(model.OfType("estate-policy"), r => r.Body.GetProperty("rules").EnumerateArray().Any(rule => rule.GetProperty("kind").GetString() == "example.policy/age"));
        foreach (IRecordType type in RecordTypeRegistry.All)
        {
            foreach (FieldDescription field in type.Fields.Where(f => f.SinceFormat == 3))
                Assert.Contains(model.OfType(type.Name), r => r.Body.TryGetProperty(field.Name, out _));
        }
        foreach (RecordEnvelope record in model.Records)
        {
            byte[] original = File.ReadAllBytes(Path.Combine(root, record.ExpectedPath));
            Assert.Equal(original, RecordAuthor.Format(original));
            var findings = new FindingList();
            RecordEnvelope parsed = Assert.IsType<RecordEnvelope>(RecordParser.Parse(original, record.ExpectedPath, findings));
            Assert.False(findings.HasErrors);
            AuthoredRecord rebuilt = RecordAuthor.Create(new RecordDraft(parsed.Type, parsed.By, parsed.Body, parsed.Subject, parsed.Supersedes, parsed.AtTime, parsed.Id, parsed.Format));
            Assert.Equal(record.ComputedHash, rebuilt.Envelope.ComputedHash);
            Assert.Equal(CanonicalJson.Serialize(record.Body), CanonicalJson.Serialize(rebuilt.Envelope.Body));
            Assert.Equal(record.Signatures, parsed.Signatures);
        }
        foreach (RecordEnvelope record in model.Records.Where(r => r.Type is "estate-policy" or "policy-exception" or "writing-exception" or "product" or "operation" or "living-document"))
            Assert.True(SignatureChecks.IsAuthentic(model, record));
    }

    [Theory]
    [InlineData("estate-policy")]
    [InlineData("policy-exception")]
    [InlineData("writing-exception")]
    public void NewRecordTypesRejectUnknownFieldsAndRetainUnknownRuleImplementations(string type)
    {
        EstateModel model = EstateLoader.Load(FixtureRoot());
        RecordEnvelope record = model.OfType(type).First();
        JsonObject body = PolicyFixture.Body(record);
        body["unknownField"] = "must not disappear";
        AuthoredRecord changed = RecordAuthor.Create(new RecordDraft(type, record.By, PolicyFixture.Element(body), record.Subject, record.Supersedes, record.AtTime, record.Id, record.Format));
        var findings = new FindingList();
        RecordTypeRegistry.Find(type)!.Validate(new ValidationContext(model, changed.Envelope, findings));
        Assert.Contains(findings, f => f.Code == "body.unknown-key");
        Assert.NotEqual(record.ComputedHash, changed.Envelope.ComputedHash);
    }

    [Fact]
    public void TheGoldenInventoryPinsEveryFixtureByte()
    {
        string root = FixtureRoot();
        string[] inventory = File.ReadAllLines(Path.Combine(root, "inventory.sha256"));
        Dictionary<string, string> expected = inventory.Where(l => l.Length > 0).Select(l => l.Split("  ", 2, StringSplitOptions.None)).ToDictionary(p => p[1], p => p[0], StringComparer.Ordinal);
        string[] actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Where(p => p != "inventory.sha256").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual);
        Assert.All(expected, item => Assert.Equal(item.Value, ContentHash.Sha256File(Path.Combine(root, item.Key))));
    }

    private static string FixtureRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return Path.Combine(root, "tests", "fixtures", "format-3-policy-estate");
    }
}
