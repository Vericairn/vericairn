// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Estate;

/// <summary>Retained implementation sources, hashed once at registration independently of any output commit.</summary>
internal static class CompanionImplementation
{
    internal static string Hash { get; } = Compute();

    private static string Compute()
    {
        Assembly assembly = typeof(CompanionImplementation).Assembly;
        var basis = new StringBuilder();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".cs", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            basis.Append(name).Append('\n').Append(reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\0');
        }
        return ContentHash.Sha256(Encoding.UTF8.GetBytes(basis.ToString()));
    }
}
