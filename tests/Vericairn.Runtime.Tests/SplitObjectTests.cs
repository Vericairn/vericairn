// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-163: the content-addressed split object, the release basis's way past the object limit. Content within the limit is one object
/// as before; larger content is parts within the limit and a root naming them, and a reader refuses a missing, extra, reordered or
/// altered part.
/// </summary>
public sealed class SplitObjectTests
{
    private const long Limit = EstateLayout.MaxObjectBytes;

    [Theory]
    [InlineData(1)]
    [InlineData(Limit - 1)]
    [InlineData(Limit)]
    public void ContentBelowAndAtTheLimitIsStoredAsOneObjectExactlyAsBefore(long size)
    {
        byte[] content = Content(size);
        var store = new Store();

        SplitObjectPlan plan = SplitObject.Plan(content);
        string reference = SplitObject.Write(content, store.Put);

        Assert.False(plan.Split);
        Assert.Null(plan.Refusal);
        Assert.Same(content, Assert.Single(plan.Objects));
        Assert.Equal(ContentHash.Sha256(content), reference);
        Assert.Equal(plan.Reference, reference);
        Assert.Equal([reference], store.Objects.Keys);
        SplitObjectContent read = SplitObject.Read(reference, store.Read);
        Assert.Empty(read.Problems);
        Assert.Equal(content, read.Content);
    }

    [Theory]
    [InlineData(Limit + 1, 2)]
    [InlineData(2 * Limit + 5, 3)]
    public void ContentAboveTheLimitIsStoredAsPartsWithinItAndARootThatReadsBackToTheExactBytes(long size, int count)
    {
        byte[] content = Content(size);
        var store = new Store();

        SplitObjectPlan plan = SplitObject.Plan(content);
        string reference = SplitObject.Write(content, store.Put);

        Assert.True(plan.Split);
        Assert.Equal(plan.Reference, reference);
        Assert.Equal(count + 1, plan.Objects.Count);
        Assert.All(store.Objects.Values, stored => Assert.True(stored.LongLength <= Limit));
        Assert.Equal(reference, ContentHash.Sha256(plan.Objects[^1]));
        using JsonDocument root = JsonDocument.Parse(store.Objects[reference]);
        Assert.Equal(["bytes", "digest", "format", "parts"], root.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(SplitObject.Format, root.RootElement.GetProperty("format").GetString());
        Assert.Equal("vericairn.split-object/1", SplitObject.Format);
        Assert.Equal(size, root.RootElement.GetProperty("bytes").GetInt64());
        Assert.Equal(ContentHash.Sha256(content), root.RootElement.GetProperty("digest").GetString());
        string[] parts = [.. root.RootElement.GetProperty("parts").EnumerateArray().Select(p => p.GetString()!)];
        Assert.Equal(plan.Objects.SkipLast(1).Select(o => ContentHash.Sha256(o)), parts);
        Assert.All(parts.SkipLast(1), part => Assert.Equal(Limit, store.Objects[part].LongLength));
        Assert.True(SplitObject.IsRoot(store.Objects[reference]));
        Assert.False(SplitObject.IsRoot(store.Objects[parts[0]]));

        SplitObjectContent read = SplitObject.Read(reference, store.Read);
        Assert.Empty(read.Problems);
        Assert.Equal(content, read.Content);
        Assert.Equal(content, SplitObject.Join(store.Objects[reference], store.Read).Content);
    }

    [Fact]
    public void ANarrowerLimitSplitsTheSameWayAndTheSameContentAlwaysHasTheSameRoot()
    {
        byte[] content = Content(9_500);
        var store = new Store();
        string reference = SplitObject.Write(content, store.Put, 1_000);
        Assert.Equal(11, store.Objects.Count);
        Assert.All(store.Objects.Values, stored => Assert.True(stored.LongLength <= 1_000));
        Assert.Equal(reference, SplitObject.Plan(content, 1_000).Reference);
        Assert.Equal(content, SplitObject.Read(reference, store.Read).Content);
    }

    [Fact]
    public void ContentWhoseRootWouldPassTheLimitIsRefusedWithNothingToStore()
    {
        byte[] content = Content(30_000);
        var store = new Store();

        SplitObjectPlan plan = SplitObject.Plan(content, 300);

        Assert.Empty(plan.Objects);
        Assert.StartsWith("Split into 100 parts, its root object would be ", plan.Refusal, StringComparison.Ordinal);
        Assert.EndsWith("; the object limit is 300 bytes.", plan.Refusal, StringComparison.Ordinal);
        Assert.Equal(plan.Refusal, Assert.Throws<InvalidOperationException>(() => SplitObject.Write(content, store.Put, 300)).Message);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public void AMissingPartIsRefusedByItsPlaceAndHash()
    {
        (Store store, string reference, string[] parts) = Split();
        store.Objects.Remove(parts[1]);
        Assert.Equal([new SplitObjectProblem(SplitObjectFault.Missing, 2, parts[1], "No object is held under the hash.")], Refused(store, reference));
    }

    [Fact]
    public void AnAlteredPartIsRefusedWhetherTheStoreReturnsItsBytesOrSaysTheyNoLongerMatch()
    {
        (Store store, string reference, string[] parts) = Split();
        store.Objects[parts[2]] = Content(1_000, seed: 9);
        Assert.Equal([new SplitObjectProblem(SplitObjectFault.Altered, 3, parts[2], "The bytes held do not match the hash.")], Refused(store, reference));

        SplitObjectContent verified = SplitObject.Read(reference, hash => hash == parts[2] ? throw new InvalidDataException("Changed.") : store.Read(hash));
        Assert.Null(verified.Content);
        Assert.Equal((SplitObjectFault.Altered, 3), (Assert.Single(verified.Problems).Fault, verified.Problems[0].Part));
    }

    [Fact]
    public void PartsOutOfOrderAreRefused()
    {
        (Store store, _, string[] parts) = Split();
        // Full parts swapped keep the layout and fail the digest; the short last part moved breaks the layout.
        Assert.Equal(SplitObjectFault.Mismatch, Assert.Single(Refused(store, Root(store, [parts[1], parts[0], parts[2], parts[3]]))).Fault);
        IReadOnlyList<SplitObjectProblem> moved = Refused(store, Root(store, [parts[3], parts[0], parts[1], parts[2]]));
        Assert.Equal([(SplitObjectFault.Invalid, 2), (SplitObjectFault.Invalid, 3), (SplitObjectFault.Invalid, 4)], moved.Select(p => (p.Fault, p.Part)));
    }

    [Fact]
    public void AnExtraOrDroppedPartIsRefused()
    {
        (Store store, _, string[] parts) = Split();
        string empty = store.Put([]);
        // An empty part would join to the same bytes, so the layout refuses it; any other extra or dropped part fails the length.
        Assert.Contains(Refused(store, Root(store, [.. parts, empty])), p => (p.Fault, p.Part, p.Reference) == (SplitObjectFault.Invalid, 5, empty));
        Assert.Equal((SplitObjectFault.Invalid, 4, parts[3]), Problem(Refused(store, Root(store, [.. parts, parts[0]]))));
        Assert.Equal(SplitObjectFault.Mismatch, Assert.Single(Refused(store, Root(store, [parts[0], .. parts]))).Fault);
        Assert.Equal(SplitObjectFault.Mismatch, Assert.Single(Refused(store, Root(store, parts[..3]))).Fault);
    }

    [Fact]
    public void APartPastTheObjectLimitIsRefusedEvenWhenItsBytesAreItsOwn()
    {
        var store = new Store();
        byte[] content = Content(Limit + 2);
        string[] parts = [store.Put(content[..(int)(Limit + 1)]), store.Put(content[(int)(Limit + 1)..])];
        string root = store.Put(Encoding.UTF8.GetBytes(new JsonObject
        {
            ["format"] = SplitObject.Format, ["bytes"] = content.LongLength, ["digest"] = ContentHash.Sha256(content), ["parts"] = new JsonArray(parts[0], parts[1]),
        }.ToJsonString()));
        Assert.Equal((SplitObjectFault.Invalid, 1, parts[0]), Problem(Refused(store, root)));
    }

    [Fact]
    public void AnInvalidPartReferenceOrRootIsRefused()
    {
        (Store store, string reference, string[] parts) = Split();
        Assert.Equal((SplitObjectFault.InvalidReference, 2, "sha256:zz"), Problem(Refused(store, Root(store, [parts[0], "sha256:zz", parts[2], parts[3]]))));
        JsonObject valid = JsonNode.Parse(store.Objects[reference])!.AsObject();
        foreach (Action<JsonObject> change in new Action<JsonObject>[]
        {
            root => root.Remove("digest"),
            root => root["digest"] = "not-a-hash",
            root => root["bytes"] = 0,
            root => root["bytes"] = "3500",
            root => root["parts"] = new JsonArray(parts[0]),
            root => root["parts"] = new JsonArray(parts[0], 7),
            root => root["format"] = "vericairn.split-object/2",
            root => root["extra"] = true,
        })
        {
            JsonObject root = valid.DeepClone().AsObject();
            change(root);
            SplitObjectContent joined = SplitObject.Join(Encoding.UTF8.GetBytes(root.ToJsonString()), store.Read);
            Assert.Null(joined.Content);
            SplitObjectProblem problem = Assert.Single(joined.Problems);
            Assert.Equal((SplitObjectFault.Invalid, 0), (problem.Fault, problem.Part));
        }
        Assert.Equal(SplitObjectFault.Invalid, Assert.Single(SplitObject.Join("not json"u8.ToArray(), store.Read).Problems).Fault);
        // Read takes an object of another format for content of its own; only a root of this format is joined.
        JsonObject later = valid.DeepClone().AsObject();
        later["format"] = "vericairn.split-object/2";
        byte[] other = Encoding.UTF8.GetBytes(later.ToJsonString());
        Assert.Equal(other, SplitObject.Read(store.Put(other), store.Read).Content);
        Assert.Equal(SplitObjectFault.InvalidReference, Assert.Single(SplitObject.Read("not-a-hash", store.Read).Problems).Fault);
        Assert.Equal(SplitObjectFault.Missing, Assert.Single(SplitObject.Read(ContentHash.Sha256("absent"u8), store.Read).Problems).Fault);
    }

    // Content of 3,500 bytes split under a limit of 1,000: three full parts and a last of 500 bytes.
    private static (Store Store, string Reference, string[] Parts) Split()
    {
        var store = new Store();
        string reference = SplitObject.Write(Content(3_500), store.Put, 1_000);
        using JsonDocument root = JsonDocument.Parse(store.Objects[reference]);
        return (store, reference, [.. root.RootElement.GetProperty("parts").EnumerateArray().Select(p => p.GetString()!)]);
    }

    // A root naming the given parts with the length and digest of the original content.
    private static string Root(Store store, IEnumerable<string> parts)
    {
        byte[] content = Content(3_500);
        return store.Put(Encoding.UTF8.GetBytes(new JsonObject
        {
            ["format"] = SplitObject.Format, ["bytes"] = content.LongLength, ["digest"] = ContentHash.Sha256(content),
            ["parts"] = new JsonArray([.. parts.Select(p => (JsonNode)p)]),
        }.ToJsonString()));
    }

    private static IReadOnlyList<SplitObjectProblem> Refused(Store store, string reference)
    {
        SplitObjectContent read = SplitObject.Read(reference, store.Read);
        Assert.Null(read.Content);
        Assert.NotEmpty(read.Problems);
        return read.Problems;
    }

    private static (SplitObjectFault, int, string) Problem(IReadOnlyList<SplitObjectProblem> problems)
    {
        SplitObjectProblem problem = Assert.Single(problems);
        return (problem.Fault, problem.Part, problem.Reference);
    }

    private static byte[] Content(long size, int seed = 7)
    {
        byte[] content = new byte[size];
        new Random(seed).NextBytes(content);
        return content;
    }

    private sealed class Store
    {
        internal Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

        internal string Put(byte[] bytes)
        {
            string hash = ContentHash.Sha256(bytes);
            Objects.TryAdd(hash, bytes);
            return hash;
        }

        internal byte[]? Read(string hash) => Objects.GetValueOrDefault(hash);
    }
}
