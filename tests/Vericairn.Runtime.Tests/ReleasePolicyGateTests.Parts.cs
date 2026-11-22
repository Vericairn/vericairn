// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

// FIX-163: a basis past the estate's object limit is admitted and retained as a split object, each object within the limit, and reads
// back as the one basis it is; a basis that fits one object is retained as that object, as before.
public sealed partial class ReleasePolicyGateTests
{
    private const string SplitFormat = "vericairn.split-object/1";

    // Files at the commit whose names take the basis to about 10.6 MB, past the 8 MiB limit: two parts and a root.
    private const int PastTheLimit = 3500;

    [Fact]
    public void ABasisPastTheObjectLimitIsAdmittedAndRetainedInPartsThatReadBackToItsExactBytes()
    {
        using var f = new Fixture();
        LargeBasisPolicy(f);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new SizedInventory(PastTheLimit);
            ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
            Assert.True(check.Ok, check.Message);
            Assert.True(check.Check!.BasisBytes > EstateLayout.MaxObjectBytes);
            Assert.Empty(f.Api.Session.Objects.List());

            ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);

            Assert.True(submitted.Ok, submitted.Message);
            Assert.Equal(1, f.Provider.Calls);
            string root = Assert.Single(submitted.BasisEvidence!);
            // Every object retained is within the limit: the parts, then the root that names them in order.
            Assert.All(f.Api.Session.Objects.List(), hash => Assert.True(f.Api.Session.Objects.Read(hash)!.LongLength <= EstateLayout.MaxObjectBytes, hash));
            using JsonDocument named = JsonDocument.Parse(f.Api.Session.Objects.Read(root)!);
            Assert.Equal(SplitFormat, named.RootElement.GetProperty("format").GetString());
            Assert.Equal(check.Check.BasisBytes, named.RootElement.GetProperty("bytes").GetInt64());
            string[] parts = [.. named.RootElement.GetProperty("parts").EnumerateArray().Select(p => p.GetString()!)];
            Assert.Equal((int)((check.Check.BasisBytes + EstateLayout.MaxObjectBytes - 1) / EstateLayout.MaxObjectBytes), parts.Length);
            byte[] joined = [.. parts.SelectMany(part => f.Api.Session.Objects.Read(part)!)];
            Assert.Equal(check.Check.BasisBytes, joined.LongLength);
            Assert.Equal(named.RootElement.GetProperty("digest").GetString(), ContentHash.Sha256(joined));
            // The joined bytes are the canonical format-3 basis admission measured.
            using JsonDocument whole = JsonDocument.Parse(joined);
            Assert.Equal("vericairn.release-basis/3", whole.RootElement.GetProperty("format").GetString());
            Assert.Equal(joined, CanonicalJson.SerializeUtf8(whole.RootElement));

            // The intent's evidence names the root, and the estate it was written into is valid and within its limits.
            RecordEnvelope operation = Assert.Single(f.Api.Session.Load().Current("operation"));
            Assert.Contains(operation.Body.GetProperty("evidence").EnumerateArray(), e => e.TryGetProperty("hash", out JsonElement hash) && hash.GetString() == root);
            Assert.Equal(0, f.Api.Validate().Errors);
            Assert.DoesNotContain(f.Api.Check().Findings, finding => finding.Severity == "error");

            // The root reads as the basis its parts join to, exactly as those bytes read held in one object.
            ReleaseBasisResponse read = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(root));
            Assert.Null(read.Problem);
            Assert.Equal(root, read.Hash);
            Assert.Equal("vericairn.release-basis/3", read.Evidence!.Format);
            Assert.Contains(read.Evidence.Repositories, r => r.Files.Count == PastTheLimit);
            string single = f.Api.Session.Objects.Put(joined);
            ReleaseBasisResponse direct = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(single));
            Assert.Equal(JsonSerializer.Serialize(direct with { Hash = root }, ProtocolJson.Options), JsonSerializer.Serialize(read, ProtocolJson.Options));
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    [Fact]
    public void ABasisWhosePartIsAlteredLostOrRepeatedIsUnavailableNamingThePart()
    {
        using var f = new Fixture();
        LargeBasisPolicy(f);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new SizedInventory(PastTheLimit);
            ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
            Assert.True(submitted.Ok, submitted.Message);
            string root = Assert.Single(submitted.BasisEvidence!);
            JsonObject named = JsonNode.Parse(f.Api.Session.Objects.Read(root)!)!.AsObject();
            string[] parts = [.. named["parts"]!.AsArray().Select(p => p!.GetValue<string>())];
            string digest = named["digest"]!.GetValue<string>();
            Assert.Equal(2, parts.Length);

            // A root naming a part twice joins to other bytes than the basis its digest records.
            named["parts"] = new JsonArray(parts[0], parts[0]);
            string repeated = f.Api.Session.Objects.Put(System.Text.Encoding.UTF8.GetBytes(named.ToJsonString()));
            Assert.Equal(("release-basis.hash-mismatch", "basis:" + digest), Failure(f, repeated));

            byte[] last = f.Api.Session.Objects.Read(parts[1])!;
            Assert.True(f.Api.Session.Objects.Remove(parts[1]));
            Assert.Equal(("release-basis.missing", $"basis-part:2@{parts[1]}"), Failure(f, root));
            f.Api.Session.Objects.Put(last);
            Assert.Null(f.Api.ReleaseBasis(root)!.Problem);

            File.WriteAllText(Path.Combine(f.Records.Estate.Root, EstateLayout.ObjectsDirectory, parts[0][ContentHash.Prefix.Length..]), "Changed bytes.\n");
            Assert.Equal(("release-basis.hash-mismatch", $"basis-part:1@{parts[0]}"), Failure(f, root));
            Assert.Equal("The basis part's bytes do not match its hash.", Assert.Single(f.Api.ReleaseBasis(root)!.Problem!.Findings).Message);
            // The estate check refuses the altered part as it refuses any object whose bytes no longer match its name.
            Assert.Contains(f.Api.Check().Findings, finding => finding.Severity == "error" && finding.Code == "objects.hash" &&
                finding.Location == EstateLayout.ObjectsDirectory + "/" + parts[0][ContentHash.Prefix.Length..]);
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    [Fact]
    public void ABasisThatFitsOneObjectIsRetainedAsThatObjectExactlyAsBefore()
    {
        using var f = new Fixture();
        f.Duty();
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
        Assert.True(submitted.Ok, submitted.Message);
        string hash = Assert.Single(submitted.BasisEvidence!);
        byte[] stored = f.Api.Session.Objects.Read(hash)!;
        Assert.Equal(check.Check!.BasisBytes, stored.LongLength);
        using JsonDocument basis = JsonDocument.Parse(stored);
        Assert.Equal("vericairn.release-basis/3", basis.RootElement.GetProperty("format").GetString());
        Assert.DoesNotContain(f.Api.Session.Objects.List(), o => System.Text.Encoding.UTF8.GetString(f.Api.Session.Objects.Read(o)!).Contains(SplitFormat, StringComparison.Ordinal));
        Assert.Equal(hash, f.Api.ReleaseBasis(hash)!.Hash);
        Assert.Null(f.Api.ReleaseBasis(hash)!.Problem);
    }

    // A source policy over the fixture repository, so that the inventory's files at the commit enter the basis.
    private static void LargeBasisPolicy(Fixture f)
    {
        RecordEnvelope resource = Assert.Single(f.Api.Session.Read().Current("resource"));
        JsonObject definition = f.Records.PolicyBody("source");
        definition["level"] = "remind";
        definition["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-10));
        definition["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = resource.Id, ["selectors"] = new JsonArray("docs/**") };
        definition["rules"] = new JsonArray(new JsonObject { ["key"] = "revision", ["kind"] = "vericairn.estate/revision-line", ["version"] = 1,
            ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        f.Records.Policy(definition);
    }

    private sealed class SizedInventory(int count) : IPolicyRepositorySource
    {
        private readonly System.Collections.Immutable.ImmutableArray<PolicyFile> files = [.. Enumerable.Range(0, count).Select(i =>
            new PolicyFile("docs/" + new string('a', 180) + "/file-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".md", ContentHash.Sha256([]), []))];
        public PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit) => new(repository.Id, commit!, PolicyBasis.RepositoryContract, true, files);
    }

    private static (string Code, string Location) Failure(Fixture f, string hash)
    {
        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(hash));
        Assert.Null(response.Evidence);
        Assert.Equal("release-basis-unavailable", response.Problem!.Code);
        FindingDto finding = Assert.Single(response.Problem.Findings);
        return (finding.Code, finding.Location);
    }
}
