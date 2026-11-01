// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Disposable release records; all state belongs to the fixture estate.</summary>
public sealed class ReleaseLineFixture : IDisposable
{
    public ReleaseLineFixture(long format = 3)
    {
        Estate = new TestEstate(format);
        Product = Estate.Add("product", Estate.OperatorId, ProductBody().ToJsonString());
    }

    public TestEstate Estate { get; }
    public RecordEnvelope Product { get; private set; }

    public static JsonObject ProductBody() => JsonNode.Parse("""
        {"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}
        """)!.AsObject();

    public static JsonObject Body(string version = "2.1.0", string? line = null, string? channel = null, RecordEnvelope? backport = null)
    {
        JsonObject body = JsonNode.Parse("""
            {"repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":[{"name":"example.zip","hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111","bytes":10,"platform":"any"}],"recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}
            """)!.AsObject();
        body["version"] = version;
        if (line is not null) body["line"] = line;
        if (channel is not null) body["channel"] = channel;
        if (channel == "lts") body["endOfSupport"] = "2028-01-01T00:00:00Z";
        if (backport is not null) body["backportOf"] = new JsonObject { ["id"] = backport.Id, ["hash"] = backport.ComputedHash };
        return body;
    }

    public RecordEnvelope Candidate(JsonObject body, string? supersedes = null) =>
        Estate.Add("release", Estate.OperatorId, body.ToJsonString(), new("product", Product.Id, null), supersedes);

    public RecordEnvelope Publish(JsonObject body)
    {
        RecordEnvelope candidate = Candidate(body);
        Estate.Add("acceptance", Estate.OperatorId, """{"statement":"Accepted release.","basis":[],"self":true}""", candidate.Reference);
        JsonObject published = body.DeepClone().AsObject();
        published["status"] = "published";
        published["publishedAt"] = RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1));
        published["url"] = "https://example.invalid/releases/" + body["version"]!.GetValue<string>();
        return Candidate(published, candidate.Id);
    }

    public RecordEnvelope ReviseProduct(JsonObject body)
    {
        Product = Estate.Add("product", Estate.OperatorId, body.ToJsonString(), supersedes: Product.Id);
        return Product;
    }

    public void AssertValid()
    {
        var model = Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
    }

    public void Dispose() => Estate.Dispose();
}
