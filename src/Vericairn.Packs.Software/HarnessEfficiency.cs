// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;

namespace Vericairn.Packs.Software;

internal static class HarnessEfficiency
{
    internal static void CheckAddress(Uri? address)
    {
        if (address is not null && (!address.IsAbsoluteUri || address.Scheme != "http" || address.Host != "127.0.0.1"
            || address.Port <= 0 || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0))
            throw new ProviderRefusedException("The harness efficiency hook accepts only the worker's loopback address.");
    }
}
