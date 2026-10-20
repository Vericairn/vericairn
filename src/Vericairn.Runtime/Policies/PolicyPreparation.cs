// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Policies;

/// <summary>Pure preparation operations shared by the file-only CLI and the estate API.</summary>
internal static class PolicyPreparation
{
    internal static PolicyHashResponse Hash(PolicyHashRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try { return new(PolicyDefinition.Hash(request.Body)); }
        catch (CanonicalJsonException exception) { return new(null, new("invalid", exception.Message, [])); }
    }
}
