// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class ReleaseLineTests
{
    private static readonly string[] NewFields = ["line", "channel", "endOfSupport", "backportOf"];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingRecordsReadUnchanged(long format)
    {
        using var f = new ReleaseLineFixture(format);
        RecordEnvelope release = f.Publish(ReleaseLineFixture.Body());
        string path = Path.Combine(f.Estate.Root, release.ExpectedPath);
        byte[] bytes = File.ReadAllBytes(path);
        string hash = release.ComputedHash;
        RecordEnvelope loaded = f.Estate.Load().ById[release.Id];
        Assert.Equal("current", ReleaseLines.Line(loaded.Body));
        Assert.Equal("stable", ReleaseLines.Channel(loaded.Body));
        Assert.Equal(hash, loaded.ComputedHash);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.All(NewFields, key => Assert.False(loaded.Body.TryGetProperty(key, out _)));
        f.AssertValid();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1.x")]
    [InlineData("a-b.c")]
    [InlineData("1234567890123456789012345678901234567890")]
    public void LineNamesAdmitTheExactAlphabetAndBound(string line)
    {
        using var f = new ReleaseLineFixture();
        f.Candidate(ReleaseLineFixture.Body(line: line));
        f.AssertValid();
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData(".x")]
    [InlineData("-x")]
    [InlineData("a_b")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("a\n")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void InvalidLineNamesAreRefusedOnReleasesAndProducts(string line)
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope release = f.Candidate(ReleaseLineFixture.Body(line: line));
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(new JsonObject { ["name"] = line, ["channel"] = "stable" });
        RecordEnvelope declaration = f.ReviseProduct(product);
        AssertError(f, release, "#body.line", "body.length", "body.pattern");
        AssertError(f, declaration, "#body.lines[0].name", "body.length", "body.pattern");
    }

    [Theory]
    [InlineData("Stable")]
    [InlineData("preview")]
    [InlineData("")]
    public void UnknownChannelsAreRefused(string channel)
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope release = f.Candidate(ReleaseLineFixture.Body(channel: channel));
        JsonObject body = ReleaseLineFixture.ProductBody();
        body["lines"] = new JsonArray(new JsonObject { ["name"] = "current", ["channel"] = channel });
        RecordEnvelope product = f.ReviseProduct(body);
        AssertError(f, release, "#body.channel", "body.enum", "body.length");
        AssertError(f, product, "#body.lines[0].channel", "body.enum", "body.length");
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("lts")]
    [InlineData("prerelease")]
    [InlineData("nightly")]
    public void EveryChannelAcceptsBackports(string channel)
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope source = f.Publish(ReleaseLineFixture.Body("2.1.0"));
        f.ReviseProduct(ReleaseLineFixture.ProductBody());
        f.Publish(ReleaseLineFixture.Body("1.9.1", "1.x", channel, source));
        f.AssertValid();
    }

    [Theory]
    [InlineData("stable", "lts")]
    [InlineData("lts", "stable")]
    [InlineData("nightly", "stable")]
    [InlineData("nightly", "prerelease")]
    [InlineData("prerelease", "nightly")]
    [InlineData("stable", "nightly")]
    public void ALineCannotChangeItsChannel(string first, string second)
    {
        using var f = new ReleaseLineFixture();
        f.Publish(ReleaseLineFixture.Body("1.0.0", "one", first));
        RecordEnvelope conflict = f.Candidate(ReleaseLineFixture.Body("1.1.0-rc.1", "one", second));
        AssertError(f, conflict, "#body.channel", "release.line-channel");
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("lts")]
    public void StableAndLtsLinesCanPreviewTheirNextVersionAndThenPublishIt(string channel)
    {
        using var f = new ReleaseLineFixture();
        f.Publish(ReleaseLineFixture.Body("1.0.0", "one", channel));
        f.Publish(ReleaseLineFixture.Body("1.1.0-rc.1", "one", "prerelease"));
        f.Publish(ReleaseLineFixture.Body("1.1.0", "one", channel));
        f.AssertValid();
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("lts")]
    public void ADeclaredLineCanStartWithAPrereleaseOfItsFirstVersion(string channel)
    {
        using var f = new ReleaseLineFixture();
        JsonObject product = ReleaseLineFixture.ProductBody();
        JsonObject line = new() { ["name"] = "one", ["channel"] = channel };
        if (channel == "lts") line["endOfSupport"] = "2028-01-01T00:00:00Z";
        product["lines"] = new JsonArray(line);
        f.ReviseProduct(product);
        f.Publish(ReleaseLineFixture.Body("1.0.0-rc.1", "one", "prerelease"));
        f.Publish(ReleaseLineFixture.Body("1.0.0", "one", channel));
        f.AssertValid();
    }

    [Theory]
    [InlineData("1.0.0-rc.1")]
    [InlineData("0.9.9-rc.1")]
    [InlineData("1.1.0")]
    public void AStableLinePreviewMustNameItsNextVersion(string version)
    {
        using var f = new ReleaseLineFixture();
        f.Publish(ReleaseLineFixture.Body("1.0.0"));
        RecordEnvelope preview = f.Candidate(ReleaseLineFixture.Body(version, channel: "prerelease"));
        AssertError(f, preview, "#body.version", "release.prerelease-version");
    }

    [Fact]
    public void AProductDeclarationFixesItsChannelAndRejectsUndeclaredLines()
    {
        using var f = new ReleaseLineFixture();
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(new JsonObject { ["name"] = "preview", ["channel"] = "prerelease" });
        f.ReviseProduct(product);
        RecordEnvelope stable = f.Candidate(ReleaseLineFixture.Body(line: "preview"));
        RecordEnvelope absent = f.Candidate(ReleaseLineFixture.Body("2.2.0", "unknown"));
        AssertError(f, stable, "#body.channel", "release.line-channel");
        AssertError(f, absent, "#body.line", "release.line");
        product["lines"]![0]!["channel"] = "stable";
        RecordEnvelope changed = f.ReviseProduct(product);
        AssertError(f, changed, "#body.lines[0].channel", "product.line-channel");
    }

    [Fact]
    public void AProductCannotRedefineAnImplicitExistingLine()
    {
        using var f = new ReleaseLineFixture();
        f.Publish(ReleaseLineFixture.Body());
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(new JsonObject { ["name"] = "current", ["channel"] = "nightly" });
        AssertError(f, f.ReviseProduct(product), "#body.lines[0].channel", "product.line-channel");
    }

    [Fact]
    public void NamingAnEarlierProductRevisionCannotBypassADeclaredChannel()
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope earlier = f.Product;
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(new JsonObject { ["name"] = "current", ["channel"] = "nightly" });
        f.ReviseProduct(product);
        RecordEnvelope conflict = f.Estate.Add("release", f.Estate.OperatorId, ReleaseLineFixture.Body().ToJsonString(), earlier.Reference);
        AssertError(f, conflict, "#body.channel", "release.line-channel");
    }

    [Theory]
    [InlineData(null, "body.missing")]
    [InlineData("2028-02-30T00:00:00Z", "body.timestamp")]
    [InlineData("2028-01-01", "body.timestamp")]
    [InlineData("2028-01-01T00:00:00", "body.timestamp")]
    [InlineData("2028-01-01 00:00:00Z", "body.timestamp")]
    [InlineData("soon", "body.timestamp")]
    public void LtsRequiresAValidRfc3339EndOnTheReleaseAndDeclaration(string? end, string code)
    {
        using var f = new ReleaseLineFixture();
        JsonObject body = ReleaseLineFixture.Body(line: "1.x", channel: "lts");
        if (end is null) body.Remove("endOfSupport"); else body["endOfSupport"] = end;
        RecordEnvelope release = f.Candidate(body);
        JsonObject declaration = new() { ["name"] = "1.x", ["channel"] = "lts" };
        if (end is not null) declaration["endOfSupport"] = end;
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(declaration);
        AssertError(f, release, "#body.endOfSupport", code);
        AssertError(f, f.ReviseProduct(product), "#body.lines[0].endOfSupport", code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stable")]
    [InlineData("prerelease")]
    [InlineData("nightly")]
    public void OtherChannelsCannotCarryEndOfSupport(string? channel)
    {
        using var f = new ReleaseLineFixture();
        JsonObject body = ReleaseLineFixture.Body(channel: channel);
        body["endOfSupport"] = "2028-01-01T00:00:00Z";
        AssertError(f, f.Candidate(body), "#body.endOfSupport", "release.end-of-support");
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(new JsonObject { ["name"] = "current", ["channel"] = channel ?? "stable", ["endOfSupport"] = "2028-01-01T00:00:00Z" });
        AssertError(f, f.ReviseProduct(product), "#body.lines[0].endOfSupport", "release.end-of-support");
    }

    [Theory]
    [InlineData("2026-10-13T07:00:00-05:00", false)]
    [InlineData("2026-10-13T12:00:07Z", true)]
    [InlineData("2026-10-13T07:00:07-05:00", true)]
    [InlineData("2028-01-01T00:00:00.123+05:30", true)]
    [InlineData("2028-01-01t00:00:00z", true)]
    public void EndOfSupportIsComparedAsAnInstant(string end, bool valid)
    {
        using var f = new ReleaseLineFixture();
        JsonObject body = ReleaseLineFixture.Body(channel: "lts");
        body["endOfSupport"] = end;
        RecordEnvelope published = f.Publish(body);
        if (valid) f.AssertValid(); else AssertError(f, published, "#body.endOfSupport", "release.end-of-support");
    }

    [Fact]
    public void ProductRevisionCannotHideADuplicateVersionAcrossLines()
    {
        using var f = new ReleaseLineFixture();
        f.Publish(ReleaseLineFixture.Body("1.0.0", "one"));
        f.ReviseProduct(ReleaseLineFixture.ProductBody());
        RecordEnvelope duplicate = f.Publish(ReleaseLineFixture.Body("1.0.0", "two"));
        Assert.Contains(f.Estate.Load().Findings, x => x.Code == "release.duplicate-version");
        AssertError(f, duplicate, "#body.line", "release.version-line");
    }

    [Theory]
    [InlineData("hash", "release.backport-hash")]
    [InlineData("candidate", "release.backport-published")]
    [InlineData("withdrawn", "release.backport-published")]
    [InlineData("stale", "release.backport-published")]
    [InlineData("same-line", "release.backport-line")]
    [InlineData("product", "release.backport-product")]
    [InlineData("wrong-type", "body.reference")]
    [InlineData("missing", "body.reference")]
    public void BackportReferencesMustResolveExactly(string kind, string code)
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope source = kind == "candidate" ? f.Candidate(ReleaseLineFixture.Body()) : f.Publish(ReleaseLineFixture.Body());
        if (kind is "withdrawn" or "stale")
        {
            JsonObject revision = PolicyFixture.Body(source);
            if (kind == "withdrawn") revision["status"] = "withdrawn";
            RecordEnvelope updated = f.Candidate(revision, source.Id);
            if (kind == "withdrawn") source = updated;
        }
        JsonObject body = ReleaseLineFixture.Body("1.1.0", kind == "same-line" ? "current" : "1.x", "stable", source);
        if (kind == "hash") body["backportOf"]!["hash"] = "sha256:" + new string('0', 64);
        if (kind == "wrong-type") body["backportOf"] = new JsonObject { ["id"] = f.Product.Id, ["hash"] = f.Product.ComputedHash };
        if (kind == "missing") body["backportOf"]!["id"] = "01M57TPYNPYJYZKTGCETXTHRT8";
        RecordEnvelope backport = kind == "product"
            ? f.Estate.Add("release", f.Estate.OperatorId, body.ToJsonString(), new("product", f.Estate.Add("product", f.Estate.OperatorId, ReleaseLineFixture.ProductBody().ToJsonString()).Id, null))
            : f.Candidate(body);
        Assert.Contains(f.Estate.Load().Findings, x => x.Code == code && x.Location.StartsWith(backport.ExpectedPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}", "body.missing")]
    [InlineData("{\"id\":null,\"hash\":null}", "body.type")]
    [InlineData("{\"id\":\"bad\",\"hash\":\"bad\"}", "body.ulid")]
    [InlineData("{\"id\":\"01M57TPYNPYJYZKTGCETXTHRT8\",\"hash\":\"bad\"}", "body.hash")]
    public void MalformedBackportPinsAreRefused(string pin, string code)
    {
        using var f = new ReleaseLineFixture();
        JsonObject body = ReleaseLineFixture.Body();
        body["backportOf"] = JsonNode.Parse(pin);
        RecordEnvelope record = f.Candidate(body);
        Assert.Contains(f.Estate.Load().Findings, x => x.Code == code && x.Location.StartsWith(record.ExpectedPath + "#body.backportOf", StringComparison.Ordinal));
    }

    [Fact]
    public void ABackportPinAdmitsOnlyIdAndHashAndCannotNameAFuturePublication()
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope source = f.Publish(ReleaseLineFixture.Body());
        JsonObject body = ReleaseLineFixture.Body("1.9.1", "1.x", backport: source);
        body["backportOf"]!["type"] = "release";
        AssertError(f, f.Candidate(body), "#body.backportOf.type", "body.unknown-key");
        body["backportOf"]!.AsObject().Remove("type");
        body["version"] = "1.9.2";
        f.Estate.SetClock(source.AtTime.AddSeconds(-2));
        AssertError(f, f.Candidate(body), "#body.backportOf.id", "release.backport-time");
    }

    [Fact]
    public void ProductLinesAreStrictDistinctDeclarations()
    {
        using var f = new ReleaseLineFixture();
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["lines"] = new JsonArray(new JsonObject { ["name"] = "one", ["channel"] = "stable", ["extra"] = true }, new JsonObject { ["name"] = "one", ["channel"] = "stable" });
        RecordEnvelope record = f.ReviseProduct(product);
        AssertError(f, record, "#body.lines[0].extra", "body.unknown-key");
        AssertError(f, record, "#body.lines[1].name", "product.line-duplicate");
        product["lines"] = new JsonArray();
        AssertError(f, f.ReviseProduct(product), "#body.lines", "body.empty");
    }

    [Theory]
    [InlineData("line", "\"one\"")]
    [InlineData("channel", "\"nightly\"")]
    [InlineData("endOfSupport", "\"2028-01-01T00:00:00Z\"")]
    [InlineData("backportOf", "{}")]
    public void FormatTwoRefusesEveryNewReleaseField(string field, string value)
    {
        using var f = new ReleaseLineFixture(2);
        JsonObject body = ReleaseLineFixture.Body();
        body[field] = JsonNode.Parse(value);
        AssertError(f, f.Candidate(body), "#body." + field, "body.format");
    }

    [Theory]
    [InlineData("lines", "[]")]
    [InlineData("cadence", "{}")]
    public void FormatTwoRefusesEveryNewProductField(string field, string value)
    {
        using var f = new ReleaseLineFixture(2);
        JsonObject body = ReleaseLineFixture.ProductBody();
        body[field] = JsonNode.Parse(value);
        AssertError(f, f.ReviseProduct(body), "#body." + field, "body.format");
    }

    [Fact]
    public void BackportNotesShowBothVersionsFromEitherLine()
    {
        using var f = new ReleaseLineFixture();
        RecordEnvelope source = f.Publish(ReleaseLineFixture.Body("2.1.0"));
        RecordEnvelope backport = f.Publish(ReleaseLineFixture.Body("1.9.1", "1.x", "lts", source));
        f.AssertValid();
        EstateModel model = f.Estate.Load();
        string expected = $"1.9.1 (1.x) is a backport of 2.1.0 (current); source release {source.Id} at {source.ComputedHash}.";
        Assert.Equal(expected, Assert.Single(ReleaseBackports.Notes(model, source.At, backport.At, "current", f.Product.Id)));
        Assert.Equal(expected, Assert.Single(ReleaseBackports.Notes(model, source.At, backport.At, "1.x", f.Product.Id)));
        Assert.Empty(ReleaseBackports.Notes(model, source.At, source.At, "current"));
        Assert.Empty(ReleaseBackports.Notes(model, source.At, backport.At, "unrelated"));
        Assert.Empty(ReleaseBackports.Notes(model, backport.At, backport.At));
    }

    [Fact]
    public void NewFieldsHaveGoldenCanonicalBytesAndRoundTripWithoutDefaultsBeingWritten()
    {
        const string Golden = """{"backportOf":{"hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111","id":"01M57TPYNPYJYZKTGCETXTHRT8"},"channel":"lts","endOfSupport":"2028-01-01T00:00:00Z","line":"1.x"}""";
        using JsonDocument document = JsonDocument.Parse(Golden);
        AuthoredRecord written = RecordAuthor.Create(new("release", "01M57TPYNPYJYZKTGCETXTHRT8", document.RootElement, Format: 3));
        var findings = new FindingList();
        RecordEnvelope? read = RecordParser.Parse(written.Bytes, written.RelativePath, findings);
        Assert.NotNull(read);
        Assert.False(findings.HasErrors);
        Assert.Equal(Golden, CanonicalJson.Serialize(read.Body));
        Assert.Equal(written.Envelope.ComputedHash, read.ComputedHash);
        Assert.Equal(3, read.Format);
    }

    private static void AssertError(ReleaseLineFixture f, RecordEnvelope record, string field, params string[] codes) =>
        Assert.Contains(f.Estate.Load().Findings, x => codes.Contains(x.Code, StringComparer.Ordinal) && x.Location == record.ExpectedPath + field);
}
