// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Estate;

/// <summary>Hashes an explicit, versioned implementation identity, independent of source and assembly contents.</summary>
internal static class CompanionImplementation
{
    internal static string Hash(string identity) => ContentHash.Sha256(Encoding.UTF8.GetBytes(identity));
}
