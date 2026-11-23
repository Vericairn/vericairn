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

            // The root reads as the basis its parts join to: its records, repositories, files and documents are the joined bytes'. (The
            // reader tests show it reads exactly as the same basis held in one object; an object past the limit is not read at all.)
            ReleaseBasisResponse read = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(root));
            Assert.Null(read.Problem);
            Assert.Equal(root, read.Hash);
            Assert.Equal("vericairn.release-basis/3", read.Evidence!.Format);
            Assert.Equal(whole.RootElement.GetProperty("records").GetArrayLength(), read.Evidence.Records.Count);
            Assert.Equal(whole.RootElement.GetProperty("repositories").EnumerateArray().Select(r => (r.GetProperty("identity").GetString(), r.GetProperty("files").GetArrayLength())),
                read.Evidence.Repositories.Select(r => ((string?)r.Identity, r.Files.Count)));
            Assert.Contains(read.Evidence.Repositories, r => r.Files.Count == PastTheLimit);
            Assert.Equal(whole.RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetString()!), read.Evidence.Documents);
            Assert.Equal(whole.RootElement.GetProperty("basis").GetProperty("recordDigest").GetString(), read.Evidence.Basis.RecordDigest);
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

            // A root naming the full part twice puts a part of the wrong length in the last place, refused before either is read.
            named["parts"] = new JsonArray(parts[0], parts[0]);
            string repeated = f.Api.Session.Objects.Put(System.Text.Encoding.UTF8.GetBytes(named.ToJsonString()));
            Assert.Equal(("release-basis.invalid", $"basis-part:2@{parts[0]}"), Failure(f, repeated));
            Assert.Contains(f.Api.Check().Findings, finding => finding.Code == "objects.split" && finding.Location.EndsWith(repeated[ContentHash.Prefix.Length..], StringComparison.Ordinal));
            Assert.True(f.Api.Session.Objects.Remove(repeated));

            // A root committed without a part is named by the reader and by the estate check.
            string objects = Path.Combine(f.Records.Estate.Root, EstateLayout.ObjectsDirectory);
            string rootPath = EstateLayout.ObjectsDirectory + "/" + root[ContentHash.Prefix.Length..];
            byte[] last = f.Api.Session.Objects.Read(parts[1])!;
            Assert.True(f.Api.Session.Objects.Remove(parts[1]));
            Assert.Equal(("release-basis.missing", $"basis-part:2@{parts[1]}"), Failure(f, root));
            FindingDto incomplete = Assert.Single(f.Api.Check().Findings, finding => finding.Code == "objects.split");
            Assert.Equal(("error", rootPath), (incomplete.Severity, incomplete.Location));
            Assert.StartsWith($"Part 2 of this split object, {parts[1]}: No object is held under the hash.", incomplete.Message, StringComparison.Ordinal);
            f.Api.Session.Objects.Put(last);
            Assert.Null(f.Api.ReleaseBasis(root)!.Problem);
            Assert.DoesNotContain(f.Api.Check().Findings, finding => finding.Severity == "error");

            // A part of another length is refused before it is read; one of its own length but other bytes, once read.
            string first = Path.Combine(objects, parts[0][ContentHash.Prefix.Length..]);
            byte[] held = File.ReadAllBytes(first);
            File.WriteAllText(first, "Changed bytes.\n");
            Assert.Equal(("release-basis.invalid", $"basis-part:1@{parts[0]}"), Failure(f, root));
            Assert.Equal($"The part holds 15 bytes; part 1 of this content holds {EstateLayout.MaxObjectBytes}.", Assert.Single(f.Api.ReleaseBasis(root)!.Problem!.Findings).Message);
            held[0] ^= 1;
            File.WriteAllBytes(first, held);
            Assert.Equal(("release-basis.hash-mismatch", $"basis-part:1@{parts[0]}"), Failure(f, root));
            Assert.Equal("The basis part's bytes do not match its hash.", Assert.Single(f.Api.ReleaseBasis(root)!.Problem!.Findings).Message);
            // The estate check refuses the altered part as it refuses any object whose bytes no longer match its name, once; its root,
            // whose parts are all there at their lengths, draws nothing more, since the check reads no part.
            IReadOnlyList<FindingDto> findings = f.Api.Check().Findings;
            Assert.Single(findings, finding => finding.Severity == "error" && finding.Code == "objects.hash" &&
                finding.Location == EstateLayout.ObjectsDirectory + "/" + parts[0][ContentHash.Prefix.Length..]);
            Assert.DoesNotContain(findings, finding => finding.Code == "objects.split");
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

    // Admission holds a basis to the bound its reader reads, 256 MiB, before anything is written: narrowed below this basis's size, the
    // check and the submission refuse it.
    [Fact]
    public void ABasisPastItsBoundIsRefusedBeforeAnythingIsWritten()
    {
        using var f = new Fixture();
        Assert.Equal(256L * 1024 * 1024, f.Api.ReleaseBasisBound);
        f.Api.ReleaseBasisBound = 1_000;
        var before = BytesOf(f.Records.Estate.Root);

        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        ReleaseResponse submit = f.Api.ReleaseSubmit(f.Request);

        Assert.False(check.Ok);
        Assert.False(submit.Ok);
        ReleaseObjection objection = Assert.Single(check.Check!.Objections, o => o.Code == "release.basis-size");
        Assert.Equal($"The retained basis would be {check.Check.BasisBytes} bytes; a release basis is at most 1000 bytes, the most its reader reads.", objection.Message);
        Assert.Contains(submit.Problem!.Findings, o => o.Code == "release.basis-size");
        Assert.Empty(f.Api.Session.Objects.List());
        Assert.Equal(before, BytesOf(f.Records.Estate.Root));
        Assert.Equal(0, f.Provider.Calls);
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
