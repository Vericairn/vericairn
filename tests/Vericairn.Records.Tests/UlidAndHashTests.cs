// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;

namespace Vericairn.Records.Tests;

public class UlidAndHashTests
{
    [Fact]
    public void SameMillisecondBurstStaysInGenerationOrder()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        Ulid previous = Ulid.NewUlid(at);
        for (int i = 0; i < 10000; i++)
        {
            Ulid next = Ulid.NewUlid(at);
            Assert.True(previous < next);
            previous = next;
        }
    }

    [Fact]
    public void AClockMovingBackwardsDoesNotReverseGenerationOrder()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        Ulid previous = Ulid.NewUlid(at);
        foreach (int offset in new[] { -1, -1000, -60000, 0, 1 })
        {
            Ulid next = Ulid.NewUlid(at.AddMilliseconds(offset));
            Assert.True(previous < next);
            previous = next;
        }
    }

    [Fact]
    public void RepeatedOrEarlierTimesIncrementTheSuffixUntilTheClockAdvances()
    {
        var generator = new MonotonicUlidGenerator();
        var at = new DateTimeOffset(2026, 10, 13, 14, 0, 0, 123, TimeSpan.Zero);
        Ulid previous = generator.Next(at);
        for (int i = 0; i < 1000; i++)
        {
            Ulid next = generator.Next(at.AddMilliseconds(-i));
            Assert.Equal(at, next.Timestamp);
            byte[] expected = previous.ToBytes();
            for (int j = 15; j >= 6; j--)
            {
                if (++expected[j] != 0) break;
            }

            Assert.Equal(expected, next.ToBytes());
            previous = next;
        }

        Ulid advanced = generator.Next(at.AddMilliseconds(1));
        Assert.Equal(at.AddMilliseconds(1), advanced.Timestamp);
        Assert.True(previous < advanced);
    }

    [Fact]
    public void ConcurrentBurstsAreUniqueAndEachThreadStaysOrdered()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var identifiers = new Ulid[8][];
        Parallel.For(0, identifiers.Length, thread =>
        {
            identifiers[thread] = Enumerable.Range(0, 2000).Select(_ => Ulid.NewUlid(at)).ToArray();
        });
        Assert.Equal(16000, identifiers.SelectMany(ids => ids).Distinct().Count());
        foreach (Ulid[] sequence in identifiers)
        {
            Assert.True(sequence.Zip(sequence.Skip(1), (first, second) => first < second).All(ordered => ordered));
        }
    }

    [Fact]
    public void NewUlidsAreValidAndCarryTheirTimestamp()
    {
        var generator = new MonotonicUlidGenerator();
        var at = new DateTimeOffset(2026, 10, 13, 14, 0, 0, 123, TimeSpan.Zero);
        Ulid ulid = generator.Next(at);
        Assert.Equal(26, ulid.Value.Length);
        Assert.True(Ulid.IsValid(ulid.Value));
        Assert.Equal(at, ulid.Timestamp);
        Assert.Equal(ulid, Ulid.FromBytes(ulid.ToBytes()));
        Assert.NotEqual(ulid, generator.Next(at));
    }

    [Fact]
    public void UlidsSortByTime()
    {
        var generator = new MonotonicUlidGenerator();
        Ulid earlier = generator.Next(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Ulid later = generator.Next(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));
        Assert.True(earlier < later);
        Assert.True(string.CompareOrdinal(earlier.Value, later.Value) < 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FA")]
    [InlineData("01arz3ndektsv4rrffq69g5fav")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAI")]
    [InlineData("81ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public void RejectsMalformedUlids(string? text)
    {
        Assert.False(Ulid.IsValid(text));
    }

    [Fact]
    public void AcceptsWellFormedUlids()
    {
        Assert.True(Ulid.IsValid("01ARZ3NDEKTSV4RRFFQ69G5FAV"));
        Assert.True(Ulid.IsValid("7ZZZZZZZZZZZZZZZZZZZZZZZZZ"));
    }

    [Fact]
    public void HashesWithKnownVector()
    {
        Assert.Equal("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ContentHash.Sha256([]));
        Assert.True(ContentHash.IsValid("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
        Assert.False(ContentHash.IsValid("sha256:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
        Assert.False(ContentHash.IsValid("sha1:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
    }
}
