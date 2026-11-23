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
/// as before; larger content is parts of exactly the limit and a canonical root naming them. A reader treats a root as untrusted: it
/// bounds the root, ties the count to the length, honors the caller's maximum and measures every part before it reads any, and refuses a
/// missing, extra, reordered or altered part. A counting store shows how much each refusal read.
/// </summary>
public sealed class SplitObjectTests
{
    private const long Limit = EstateLayout.MaxObjectBytes;
    private const long Narrow = 1_000;

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
        Assert.Equal([reference], store.Objects.Keys);
        SplitObjectContent read = SplitObject.Read(reference, store);
        Assert.Empty(read.Problems);
        Assert.False(read.Split);
        Assert.Equal(content, read.Content);
    }

    [Theory]
    [InlineData(Limit + 1, 2)]
    [InlineData(2 * Limit + 5, 3)]
    public void ContentAboveTheLimitIsStoredAsPartsOfTheLimitAndACanonicalRootThatReadsBackToTheExactBytes(long size, int count)
    {
        byte[] content = Content(size);
        var store = new Store();

        SplitObjectPlan plan = SplitObject.Plan(content);
        string reference = SplitObject.Write(content, store.Put);

        Assert.True(plan.Split);
        Assert.Equal(count + 1, plan.Objects.Count);
        Assert.Equal(reference, ContentHash.Sha256(plan.Objects[^1]));
        string[] parts = Parts(store, reference);
        Assert.Equal(plan.Objects.SkipLast(1).Select(o => ContentHash.Sha256(o)), parts);
        Assert.All(parts.SkipLast(1), part => Assert.Equal(Limit, store.Objects[part].LongLength));
        // The root is the canonical JSON this format names, written independently here.
        Assert.Equal(Canonical(size, ContentHash.Sha256(content), parts), Encoding.UTF8.GetString(store.Objects[reference]));
        Assert.Equal("vericairn.split-object/1", SplitObject.Format);
        Assert.True(SplitObject.IsRoot(store.Objects[reference]));

        store.Reset();
        SplitObjectContent read = SplitObject.Read(reference, store);
        Assert.Empty(read.Problems);
        Assert.True(read.Split);
        Assert.Equal(content, read.Content);
        // The root and each part are read once, and nothing else.
        Assert.Equal(size + store.Objects[reference].LongLength, store.BytesRead);
    }

    [Fact]
    public void TheLargestRootIsBoundedExactlyAndANarrowerLimitSplitsTheSameWay()
    {
        byte[] content = Content(9_500);
        var store = new Store();
        string reference = SplitObject.Write(content, store.Put, Narrow);
        Assert.Equal(11, store.Objects.Count);
        Assert.Equal(content, SplitObject.Read(reference, store, maxObjectBytes: Narrow).Content);
        // MaxRootBytes is the size of the root of the longest content allowed, and no root of shorter content is larger.
        Assert.Equal(store.Objects[reference].LongLength, SplitObject.MaxRootBytes(9_500, Narrow));
        Assert.True(SplitObject.MaxRootBytes(9_001, Narrow) == SplitObject.MaxRootBytes(9_500, Narrow) && SplitObject.MaxRootBytes(9_000, Narrow) < SplitObject.MaxRootBytes(9_001, Narrow));
        Assert.Equal(Canonical(Array.MaxLength, ContentHash.Sha256([]), [.. Enumerable.Repeat(ContentHash.Sha256([]), 256)]).Length, SplitObject.MaxRootBytes());
    }

    [Fact]
    public void ContentWhoseRootWouldPassTheLimitIsRefusedWithNothingToStore()
    {
        byte[] content = Content(30_000);
        var store = new Store();
        SplitObjectPlan plan = SplitObject.Plan(content, 300);
        Assert.Empty(plan.Objects);
        Assert.StartsWith("Split into 100 parts, its root object would be ", plan.Refusal, StringComparison.Ordinal);
        Assert.Equal(plan.Refusal, Assert.Throws<InvalidOperationException>(() => SplitObject.Write(content, store.Put, 300)).Message);
        Assert.Empty(store.Objects);
    }

    // Content that would itself read as a root is wrapped in a root of one part, so that reading what was written returns it.
    [Fact]
    public void ContentThatLooksLikeARootReadsBackAsWritten()
    {
        var store = new Store();
        string inner = SplitObject.Write(Content(3_500), store.Put, Narrow);
        byte[] root = store.Objects[inner];
        byte[] document = """{"format":"vericairn.split-object/1","note":"an ordinary document"}"""u8.ToArray();
        byte[] escaped = """{"note":1,"format":"vericairn.split\u002Dobject/1"}"""u8.ToArray();
        foreach (byte[] content in new[] { root, document, escaped })
        {
            Assert.True(SplitObject.IsRoot(content));
            SplitObjectPlan plan = SplitObject.Plan(content, Narrow);
            Assert.True(plan.Split);
            Assert.Equal(2, plan.Objects.Count);
            string reference = SplitObject.Write(content, store.Put, Narrow);
            Assert.NotEqual(ContentHash.Sha256(content), reference);
            Assert.Equal(content, SplitObject.Read(reference, store, maxObjectBytes: Narrow).Content);
        }
        // Content that does not read as a root is stored as itself.
        foreach (byte[] content in new[] { """{"format":"vericairn.split-object/2"}"""u8.ToArray(), """[{"format":"vericairn.split-object/1"}]"""u8.ToArray(), """{"format":"vericairn.split-object/1"} x"""u8.ToArray() })
        {
            Assert.False(SplitObject.IsRoot(content));
            Assert.Equal(ContentHash.Sha256(content), SplitObject.Write(content, store.Put, Narrow));
            Assert.Equal(content, SplitObject.Read(ContentHash.Sha256(content), store, maxObjectBytes: Narrow).Content);
        }
        // A root of one part over content that is no root is not what any writer produces.
        byte[] plain = Content(500);
        string part = store.Put(plain);
        string unwrapped = store.Put(Encoding.UTF8.GetBytes(Canonical(500, ContentHash.Sha256(plain), [part])));
        Assert.Equal((SplitObjectFault.Invalid, 0), Problem(Refused(store, unwrapped)));
    }

    // The reviewer's probe: one part of the limit named 113,000 times in a root just within the object limit, claiming one byte.
    [Fact]
    public void ARootRepeatingOnePartFarPastItsLengthIsRefusedBeforeAnyPartIsMeasuredOrRead()
    {
        var store = new Store();
        string part = store.Put(Content(Limit));
        string[] parts = [.. Enumerable.Repeat(part, 113_000)];
        byte[] root = Encoding.UTF8.GetBytes(Canonical(1, ContentHash.Sha256([0]), parts));
        Assert.True(root.LongLength <= Limit);
        string reference = store.Put(root);
        store.Reset();

        SplitObjectProblem problem = Assert.Single(Refused(store, reference, maxObjectBytes: Limit));

        Assert.Equal((SplitObjectFault.Invalid, 0), (problem.Fault, problem.Part));
        Assert.StartsWith($"The root is {root.Length} bytes; the root of content up to ", problem.Message, StringComparison.Ordinal);
        Assert.Equal(root.LongLength, store.BytesRead);
        Assert.Equal([reference], store.Measured);
        // A repeated part is no fault in itself: identical stretches of content give identical parts, read within the length.
        byte[] repetitive = [.. Content(Limit), .. Content(Limit), .. Content(Limit), .. Content(10)];
        string twice = SplitObject.Write(repetitive, store.Put);
        Assert.Equal(2, Parts(store, twice).Distinct().Count());
        store.Reset();
        Assert.Equal(repetitive, SplitObject.Read(twice, store).Content);
        Assert.Equal(repetitive.LongLength + store.Objects[twice].LongLength, store.BytesRead);
    }

    [Theory]
    [InlineData(1L, 2)]
    [InlineData(3 * Narrow, 4)]
    [InlineData(3 * Narrow + 1, 3)]
    [InlineData(64 * Narrow, 2)]
    public void ALengthTheCountDoesNotFollowFromIsRefusedBeforeAnyPartIsMeasured(long length, int count)
    {
        var store = new Store();
        string part = store.Put(Content(Narrow));
        byte[] root = Encoding.UTF8.GetBytes(Canonical(length, ContentHash.Sha256([0]), [.. Enumerable.Repeat(part, count)]));
        store.Reset();
        SplitObjectContent read = SplitObject.Join(root, store, maxObjectBytes: Narrow);
        Assert.Null(read.Content);
        Assert.Equal((SplitObjectFault.Invalid, 0), Problem(read.Problems));
        Assert.Contains($"names {count} parts", read.Problems[0].Message, StringComparison.Ordinal);
        Assert.Equal(0, store.BytesRead);
        Assert.Empty(store.Measured);
    }

    [Fact]
    public void ARootPastTheLargestRootIsRefusedBeforeItIsParsed()
    {
        var store = new Store();
        long largest = SplitObject.MaxRootBytes(3_500, Narrow);
        byte[] padded = Encoding.UTF8.GetBytes(new string(' ', (int)largest + 1));
        SplitObjectProblem problem = Assert.Single(SplitObject.Join(padded, store, 3_500, Narrow).Problems);
        Assert.Equal((SplitObjectFault.Invalid, 0), (problem.Fault, problem.Part));
        Assert.Equal($"The root is {largest + 1} bytes; the root of content up to 3500 bytes is at most {largest}.", problem.Message);
        Assert.Empty(store.Measured);
    }

    [Fact]
    public void APartOfAnotherLengthThanItsPlaceHoldsIsRefusedBeforeAnyPartIsRead()
    {
        (Store store, string reference, string[] parts) = Split();
        string shorter = store.Put(Content(999, seed: 3));
        byte[] root = Encoding.UTF8.GetBytes(Canonical(3_500, ContentHash.Sha256(Content(3_500)), [parts[0], shorter, parts[2], parts[3]]));
        store.Reset();
        SplitObjectContent read = SplitObject.Join(root, store, maxObjectBytes: Narrow);
        Assert.Equal([new SplitObjectProblem(SplitObjectFault.Invalid, 2, shorter, "The part holds 999 bytes; part 2 of this content holds 1000.")], read.Problems);
        Assert.Equal(0, store.BytesRead);
        Assert.Equal(parts.Length, store.Measured.Count);
        // The short last part moved first leaves the first and last places with parts of the wrong length, all measured and none read.
        string moved = store.Put(Encoding.UTF8.GetBytes(Canonical(3_500, ContentHash.Sha256(Content(3_500)), [parts[3], parts[0], parts[1], parts[2]])));
        store.Reset();
        Assert.Equal([1, 4], Refused(store, moved).Select(p => p.Part));
        Assert.Equal(store.Objects[moved].LongLength, store.BytesRead);
        Assert.Equal(Content(3_500), SplitObject.Read(reference, store, maxObjectBytes: Narrow).Content);
    }

    [Fact]
    public void TheCallersMaximumIsHeldBeforeAnyPartIsMeasured()
    {
        (Store store, string reference, _) = Split();
        store.Reset();
        SplitObjectProblem problem = Assert.Single(Refused(store, reference, maxBytes: 3_499));
        Assert.Equal((SplitObjectFault.TooLarge, 0), (problem.Fault, problem.Part));
        Assert.Equal("The root names 3500 bytes, more than the 3499 the reader accepts.", problem.Message);
        Assert.Equal([reference], store.Measured);
        Assert.Equal(store.Objects[reference].LongLength, store.BytesRead);
        Assert.Equal(Content(3_500), SplitObject.Read(reference, store, 3_500, Narrow).Content);
        // An object of its own past the caller's maximum is refused too, and one past the object limit is not read at all.
        string plain = store.Put(Content(800));
        Assert.Equal(SplitObjectFault.TooLarge, Assert.Single(SplitObject.Read(plain, store, 799, Narrow).Problems).Fault);
        string large = store.Put(Content(Narrow + 1));
        store.Reset();
        Assert.Equal((SplitObjectFault.Invalid, 0), Problem(SplitObject.Read(large, store, maxObjectBytes: Narrow).Problems));
        Assert.Equal(0, store.BytesRead);
    }

    [Fact]
    public void ARootThatIsNotCanonicalIsRefused()
    {
        (Store store, string reference, string[] parts) = Split();
        JsonObject root = JsonNode.Parse(store.Objects[reference])!.AsObject();
        string digest = root["digest"]!.GetValue<string>();
        foreach (string text in new[]
        {
            $"{{\"format\":\"{SplitObject.Format}\",\"bytes\":3500,\"digest\":\"{digest}\",\"parts\":{root["parts"]!.ToJsonString()}}}",
            Canonical(3_500, digest, parts).Replace(",", ", ", StringComparison.Ordinal),
            Canonical(3_500, digest, parts).Replace("\"bytes\"", "\"\\u0062ytes\"", StringComparison.Ordinal),
            Canonical(3_500, digest, parts)[..^1] + ",\"extra\":true}",
        })
        {
            store.Reset();
            SplitObjectContent read = SplitObject.Join(Encoding.UTF8.GetBytes(text), store, maxObjectBytes: Narrow);
            Assert.Null(read.Content);
            Assert.Equal((SplitObjectFault.Invalid, 0), Problem(read.Problems));
            Assert.Equal(0, store.BytesRead);
        }
    }

    [Fact]
    public void AMissingPartIsRefusedByItsPlaceAndHashBeforeAnyPartIsRead()
    {
        (Store store, string reference, string[] parts) = Split();
        store.Objects.Remove(parts[1]);
        store.Reset();
        Assert.Equal([new SplitObjectProblem(SplitObjectFault.Missing, 2, parts[1], "No object is held under the hash.")], Refused(store, reference));
        Assert.Equal(store.Objects[reference].LongLength, store.BytesRead);
    }

    [Fact]
    public void AnAlteredPartIsRefusedByItsPlaceAndHash()
    {
        (Store store, string reference, string[] parts) = Split();
        store.Objects[parts[2]] = Content(1_000, seed: 9);
        Assert.Equal([new SplitObjectProblem(SplitObjectFault.Altered, 3, parts[2], "The bytes held do not match the hash.")], Refused(store, reference));
    }

    [Fact]
    public void PartsOutOfOrderExtraOrDroppedAreRefused()
    {
        (Store store, _, string[] parts) = Split();
        string digest = ContentHash.Sha256(Content(3_500));
        // Full parts swapped keep every length and fail the digest.
        Assert.Equal(SplitObjectFault.Mismatch, Problem(Refused(store, Root(store, 3_500, digest, [parts[1], parts[0], parts[2], parts[3]]))).Item1);
        // A part repeated in another's place of the same length fails the digest.
        Assert.Equal(SplitObjectFault.Mismatch, Problem(Refused(store, Root(store, 3_500, digest, [parts[0], parts[0], parts[2], parts[3]]))).Item1);
        // An extra or dropped part no longer follows from the length.
        Assert.Equal(SplitObjectFault.Invalid, Problem(Refused(store, Root(store, 3_500, digest, [.. parts, parts[0]]))).Item1);
        Assert.Equal(SplitObjectFault.Invalid, Problem(Refused(store, Root(store, 3_500, digest, parts[..3]))).Item1);
        // A length changed with the count still fails the digest.
        Assert.Equal(SplitObjectFault.Mismatch, Problem(Refused(store, Root(store, 3_000, digest, parts[..3]))).Item1);
    }

    [Fact]
    public void AnInvalidPartReferenceOrRootFieldIsRefused()
    {
        (Store store, _, string[] parts) = Split();
        string digest = ContentHash.Sha256(Content(3_500));
        Assert.Equal((SplitObjectFault.InvalidReference, 2), Problem(Refused(store, Root(store, 3_500, digest, [parts[0], "sha256:zz", parts[2], parts[3]]))));
        foreach (string text in new[]
        {
            Canonical(3_500, "not-a-hash", parts), Canonical(0, digest, parts), "{\"format\":\"vericairn.split-object/1\",\"parts\":[]}", "not json",
        })
        {
            Assert.Equal((SplitObjectFault.Invalid, 0), Problem(SplitObject.Join(Encoding.UTF8.GetBytes(text), store, maxObjectBytes: Narrow).Problems));
        }
        Assert.Equal(SplitObjectFault.InvalidReference, Assert.Single(SplitObject.Read("not-a-hash", store).Problems).Fault);
        Assert.Equal(SplitObjectFault.Missing, Assert.Single(SplitObject.Read(ContentHash.Sha256("absent"u8), store).Problems).Fault);
    }

    [Fact]
    public void AnEstatesCheckJudgesOnlyCanonicalRootsAndReadsEveryPartOnce()
    {
        (Store store, string reference, string[] parts) = Split();
        Assert.Empty(SplitObject.Verify(store.Objects[reference], store, Narrow)!);
        Assert.Null(SplitObject.Verify("""{"format":"vericairn.split-object/1","note":"an ordinary document"}"""u8, store, Narrow));
        store.Objects.Remove(parts[3]);
        Assert.Equal((SplitObjectFault.Missing, 4), Problem(SplitObject.Verify(store.Objects[reference], store, Narrow)!));
    }

    // The estate's own stores measure an object without reading it and read one only into a destination of exactly its length.
    [Fact]
    public void TheEstatesObjectStoresMeasureBeforeTheyRead()
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("split-object-");
        try
        {
            using var sqlite = new Vericairn.Runtime.Objects.SqliteObjectStore(Path.Combine(scratch.FullName, "objects.db"));
            foreach (Vericairn.Runtime.Objects.IObjectStore store in new Vericairn.Runtime.Objects.IObjectStore[] { new Vericairn.Runtime.Objects.FileObjectStore(Path.Combine(scratch.FullName, "objects")), sqlite })
            {
                var bounded = (Vericairn.Runtime.Objects.IBoundedObjectReads)store;
                string hash = store.Put(Content(100));
                Assert.Equal(100, bounded.Length(hash));
                Assert.Null(bounded.Length(ContentHash.Sha256("absent"u8)));
                Assert.False(bounded.ReadExactly(hash, new byte[99]));
                Assert.False(bounded.ReadExactly(hash, new byte[101]));
                Assert.False(bounded.ReadExactly(ContentHash.Sha256("absent"u8), new byte[100]));
                byte[] read = new byte[100];
                Assert.True(bounded.ReadExactly(hash, read));
                Assert.Equal(Content(100), read);
            }
        }
        finally { scratch.Delete(recursive: true); }
    }

    // Content of 3,500 bytes split under a limit of 1,000: three full parts and a last of 500 bytes.
    private static (Store Store, string Reference, string[] Parts) Split()
    {
        var store = new Store();
        string reference = SplitObject.Write(Content(3_500), store.Put, Narrow);
        return (store, reference, Parts(store, reference));
    }

    private static string[] Parts(Store store, string reference)
    {
        using JsonDocument root = JsonDocument.Parse(store.Objects[reference]);
        return [.. root.RootElement.GetProperty("parts").EnumerateArray().Select(p => p.GetString()!)];
    }

    // The canonical root, as the format names it: members in ordinal order and no whitespace.
    private static string Canonical(long bytes, string digest, IEnumerable<string> parts) =>
        $"{{\"bytes\":{bytes},\"digest\":\"{digest}\",\"format\":\"vericairn.split-object/1\",\"parts\":[{string.Join(",", parts.Select(p => "\"" + p + "\""))}]}}";

    private static string Root(Store store, long bytes, string digest, IEnumerable<string> parts) => store.Put(Encoding.UTF8.GetBytes(Canonical(bytes, digest, parts)));

    private static IReadOnlyList<SplitObjectProblem> Refused(Store store, string reference, long maxBytes = int.MaxValue, long maxObjectBytes = Narrow)
    {
        SplitObjectContent read = SplitObject.Read(reference, store, maxBytes, maxObjectBytes);
        Assert.Null(read.Content);
        Assert.NotEmpty(read.Problems);
        return read.Problems;
    }

    private static (SplitObjectFault, int) Problem(IReadOnlyList<SplitObjectProblem> problems)
    {
        SplitObjectProblem problem = Assert.Single(problems);
        return (problem.Fault, problem.Part);
    }

    private static byte[] Content(long size, int seed = 7)
    {
        byte[] content = new byte[size];
        new Random(seed).NextBytes(content);
        return content;
    }

    // An object store that counts what a reader measures and reads.
    private sealed class Store : ISplitObjectSource
    {
        internal Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

        internal List<string> Measured { get; } = [];

        internal long BytesRead { get; private set; }

        internal void Reset()
        {
            Measured.Clear();
            BytesRead = 0;
        }

        internal string Put(byte[] bytes)
        {
            string hash = ContentHash.Sha256(bytes);
            Objects.TryAdd(hash, bytes);
            return hash;
        }

        public long? Length(string hash)
        {
            Measured.Add(hash);
            return Objects.TryGetValue(hash, out byte[]? bytes) ? bytes.LongLength : null;
        }

        public bool Read(string hash, Span<byte> destination)
        {
            if (!Objects.TryGetValue(hash, out byte[]? bytes) || bytes.Length != destination.Length) return false;
            bytes.CopyTo(destination);
            BytesRead += bytes.LongLength;
            return true;
        }
    }
}
