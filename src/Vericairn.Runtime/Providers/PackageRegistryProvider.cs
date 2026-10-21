// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Providers;

/// <summary>Anonymous registry observations. Implementations bound time, requests and bytes and perform no registry writes.</summary>
public interface IPackageRegistryProvider
{
    /// <summary>The versioned observation contract.</summary>
    const string Contract = "vericairn.provider.package-registry/1";

    /// <summary>The stable registry provider id.</summary>
    string Provider { get; }

    /// <summary>The provider's capabilities and limits.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>Read authoritative public state, or refuse without inventing missing facts.</summary>
    Task<PackageRegistryState> ObserveAsync(string package, string version, CancellationToken cancellationToken = default);
}
