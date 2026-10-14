// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Sdk;

/// <summary>How a client on this machine finds the daemon that owns an estate: the endpoint file the daemon publishes beside the estate's live store.</summary>
public static class DaemonDiscovery
{
    /// <summary>The endpoint file of an estate on this machine.</summary>
    public static string EndpointFilePath(string root) => EstateLocations.EndpointFilePath(root);

    /// <summary>Reads the endpoint file of an estate, or null when no daemon has published one; whether it answers is the caller's next question.</summary>
    public static DaemonEndpoint? Discover(string root)
    {
        string file;
        try
        {
            file = EndpointFilePath(root);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DaemonEndpoint>(File.ReadAllBytes(file), ProtocolJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
