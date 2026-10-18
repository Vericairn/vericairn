// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Cli;

internal static class RuntimeEndpointText
{
    public static string Address(LocalNetworkEndpoint network) =>
        "https://" + new System.Net.IPEndPoint(System.Net.IPAddress.Parse(network.Host), network.Port);
}
