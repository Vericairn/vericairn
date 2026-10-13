// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;

namespace Vericairn.Records.Tests;

public class UlidAndHashTests
{
    [Fact]
    public void NewUlidsAreValidAndCarryTheirTimestamp()
    {
        var at = new DateTimeOffset(2026, 10, 13, 14, 0, 0, 123, TimeSpan.Zero);
        Ulid ulid = Ulid.NewUlid(at);
        Assert.Equal(26, ulid.Value.Length);
        Assert.True(Ulid.IsValid(ulid.Value));
        Assert.Equal(at, ulid.Timestamp);
        Assert.Equal(ulid, Ulid.FromBytes(ulid.ToBytes()));
        Assert.NotEqual(ulid, Ulid.NewUlid(at));
    }

    [Fact]
    public void UlidsSortByTime()
    {
        Ulid earlier = Ulid.NewUlid(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Ulid later = Ulid.NewUlid(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));
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
