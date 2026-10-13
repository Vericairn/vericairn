// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Canonicalization is where record systems usually break: the parser must refuse or accept, never crash, and canonical output must round-trip.</summary>
public class CanonicalFuzzTests
{
    private static readonly string[] Tokens = ["\"", "{", "}", "[", "]", ",", ":", "\\u0000", "1e5", "-0", "\n", "\u00e9"];

    [Fact]
    public void MutatedRecordBytesNeverCrashTheParser()
    {
        using var estate = new TestEstate();
        byte[] original = File.ReadAllBytes(Path.Combine(estate.Root, estate.Estate.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
        var random = new Random(20261013);
        int accepted = 0, refused = 0;
        for (int iteration = 0; iteration < 3000; iteration++)
        {
            byte[] mutated = Mutate(original, random);
            var findings = new FindingList();
            RecordEnvelope? record = RecordParser.Parse(mutated, "fuzz", findings);
            if (record is null)
            {
                refused++;
                Assert.True(findings.HasErrors, "A refused record must say why.");
            }
            else
            {
                accepted++;
            }
        }

        Assert.True(refused > 0);
        Assert.True(accepted + refused == 3000);
    }

    [Fact]
    public void RandomJsonRoundTripsThroughCanonicalForm()
    {
        var random = new Random(20261014);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            string text = RandomJson(random, depth: 0);
            using JsonDocument first = JsonDocument.Parse(text);
            string canonical = CanonicalJson.Serialize(first.RootElement);
            string pretty = CanonicalJson.SerializePretty(first.RootElement);
            using JsonDocument second = CanonicalJson.Parse(Encoding.UTF8.GetBytes(pretty));
            Assert.Equal(canonical, CanonicalJson.Serialize(second.RootElement));
            using JsonDocument third = CanonicalJson.Parse(Encoding.UTF8.GetBytes(canonical));
            Assert.Equal(pretty, CanonicalJson.SerializePretty(third.RootElement));
        }
    }

    private static byte[] Mutate(byte[] original, Random random)
    {
        var bytes = new List<byte>(original);
        int edits = random.Next(1, 6);
        for (int edit = 0; edit < edits; edit++)
        {
            switch (random.Next(5))
            {
                case 0 when bytes.Count > 0:
                    bytes[random.Next(bytes.Count)] = (byte)random.Next(256);
                    break;
                case 1 when bytes.Count > 1:
                    bytes.RemoveAt(random.Next(bytes.Count));
                    break;
                case 2:
                    bytes.Insert(random.Next(bytes.Count + 1), (byte)random.Next(256));
                    break;
                case 3 when bytes.Count > 2:
                    bytes.RemoveRange(random.Next(bytes.Count / 2), random.Next(1, Math.Max(2, bytes.Count / 4)));
                    break;
                default:
                    byte[] token = Encoding.UTF8.GetBytes(Tokens[random.Next(Tokens.Length)]);
                    bytes.InsertRange(random.Next(bytes.Count + 1), token);
                    break;
            }
        }

        return bytes.ToArray();
    }

    private static string RandomJson(Random random, int depth)
    {
        int kind = depth > 3 ? random.Next(4) : random.Next(6);
        switch (kind)
        {
            case 0:
                return "null";
            case 1:
                return random.Next(2) == 0 ? "true" : "false";
            case 2:
                return random.NextInt64(-9007199254740991, 9007199254740991).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case 3:
                return JsonSerializer.Serialize(RandomString(random));
            case 4:
                return "[" + string.Join(",", Enumerable.Range(0, random.Next(4)).Select(_ => RandomJson(random, depth + 1))) + "]";
            default:
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var members = new List<string>();
                for (int i = random.Next(5); i > 0; i--)
                {
                    string key = RandomString(random);
                    if (keys.Add(key))
                    {
                        members.Add(JsonSerializer.Serialize(key) + ":" + RandomJson(random, depth + 1));
                    }
                }

                return "{" + string.Join(",", members) + "}";
        }
    }

    private static string RandomString(Random random)
    {
        var builder = new StringBuilder();
        int length = random.Next(8);
        for (int i = 0; i < length; i++)
        {
            builder.Append(random.Next(6) switch
            {
                0 => (char)random.Next(0x20, 0x7f),
                1 => (char)random.Next(0x00, 0x20),
                2 => 'é',
                3 => '中',
                4 => "😀",
                _ => "\"\\",
            });
        }

        return builder.ToString();
    }
}
