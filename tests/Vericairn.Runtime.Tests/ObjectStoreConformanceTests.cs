// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Objects;

namespace Vericairn.Runtime.Tests;

/// <summary>The same contract, run against every object store implementation: a provider is replaceable only if it passes this.</summary>
public class ObjectStoreConformanceTests
{
    public static TheoryData<string> Kinds => ["file", "sqlite"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void PutGetHasListRemoveAndIdempotence(string kind)
    {
        using Fixture fixture = new(kind);
        IObjectStore store = fixture.Store;
        Assert.Equal(kind, store.Kind);
        Assert.Empty(store.List());
        byte[] small = Encoding.UTF8.GetBytes("hello, estate\n");
        string hash = store.Put(small);
        Assert.Equal(ContentHash.Sha256(small), hash);
        Assert.Equal(hash, store.Put(small));
        Assert.True(store.Has(hash));
        Assert.Equal(small, store.Read(hash));
        string empty = store.Put([]);
        Assert.Equal(ContentHash.Sha256([]), empty);
        Assert.Equal(Array.Empty<byte>(), store.Read(empty));
        byte[] large = new byte[1024 * 1024 + 17];
        new Random(42).NextBytes(large);
        string largeHash = store.Put(large);
        Assert.Equal(large, store.Read(largeHash));
        Assert.Equal(new[] { empty, hash, largeHash }.Order(StringComparer.Ordinal), store.List());
        Assert.Null(store.Read("sha256:" + new string('f', 64)));
        Assert.False(store.Has("sha256:" + new string('f', 64)));
        Assert.True(store.Remove(hash));
        Assert.False(store.Remove(hash));
        Assert.False(store.Has(hash));
        Assert.Throws<ArgumentException>(() => store.Has("not-a-hash"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void CorruptionIsDetectedOnRead(string kind)
    {
        using Fixture fixture = new(kind);
        byte[] bytes = Encoding.UTF8.GetBytes("custody matters\n");
        string hash = fixture.Store.Put(bytes);
        fixture.Corrupt(hash);
        Assert.True(fixture.Store.Has(hash));
        Assert.Throws<ObjectCorruptedException>(() => fixture.Store.Read(hash));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "vericairn-objects-" + Ulid.NewUlid().Value);
        private readonly string kind;

        public Fixture(string kind)
        {
            this.kind = kind;
            Directory.CreateDirectory(directory);
            Store = kind == "file" ? new FileObjectStore(Path.Combine(directory, "objects")) : new SqliteObjectStore(Path.Combine(directory, "objects.db"));
        }

        public IObjectStore Store { get; }

        public void Corrupt(string hash)
        {
            if (kind == "file")
            {
                File.WriteAllText(Path.Combine(directory, "objects", hash["sha256:".Length..]), "tampered\n");
                return;
            }

            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "objects.db"), Pooling = false }.ToString());
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE objects SET bytes = $bytes WHERE hash = $hash;";
            command.Parameters.AddWithValue("$bytes", Encoding.UTF8.GetBytes("tampered\n"));
            command.Parameters.AddWithValue("$hash", hash);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        public void Dispose()
        {
            Store.Dispose();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
