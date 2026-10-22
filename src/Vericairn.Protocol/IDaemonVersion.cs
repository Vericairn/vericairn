// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>A client's release diagnostic from the daemon's most recent health response.</summary>
public interface IDaemonVersion
{
    /// <summary>Both releases and restart instructions when they differ; null before health is read or when they match.</summary>
    string? DaemonVersionWarning { get; }
}
