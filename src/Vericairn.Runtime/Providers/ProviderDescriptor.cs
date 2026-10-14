// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>A setting a provider declares: what a request or a product's declared destination supplies, and the provider's own default when it has one.</summary>
/// <param name="Name">The setting's name, for example repository.</param>
/// <param name="Summary">What it is, for a person filling it in.</param>
/// <param name="Required">Whether an operation is refused without it.</param>
/// <param name="Default">The provider's own default, shown as such; null when there is none.</param>
public sealed record ProviderSetting(string Name, string Summary, bool Required, string? Default = null);

/// <summary>What an adapter or provider implementation is and may claim: its contract version, the operations it supports, its recorded status, its limitations, and the settings it declares.</summary>
/// <param name="Id">Stable implementation id, for example git-command or github-releases-gh.</param>
/// <param name="Contract">The versioned contract it implements, for example vericairn.adapter.git/1.</param>
/// <param name="Status">candidate, implemented, qualified or available; never claimed above what tests established.</param>
/// <param name="Operations">The operations it supports.</param>
/// <param name="Limitations">What it does not do.</param>
/// <param name="Settings">The settings an operation through it takes; empty when it takes none.</param>
/// <param name="Destination">The setting that names the destination an operation's identity is reconciled under, for example repository; null when it has none.</param>
/// <param name="Locations">The resource location keys it serves (a repository resource located at github is served by a GitHub provider); empty when it serves none.</param>
public sealed record ProviderDescriptor(string Id, string Contract, string Status, IReadOnlyList<string> Operations, IReadOnlyList<string> Limitations, IReadOnlyList<ProviderSetting>? Settings = null, string? Destination = null, IReadOnlyList<string>? Locations = null)
{
    /// <summary>The statuses an implementation may record.</summary>
    public static readonly string[] Statuses = ["candidate", "implemented", "qualified", "available"];

    /// <summary>The declared settings, never null.</summary>
    public IReadOnlyList<ProviderSetting> DeclaredSettings => Settings ?? [];

    /// <summary>The served location keys, never null.</summary>
    public IReadOnlyList<string> ServedLocations => Locations ?? [];
}

/// <summary>A provider refused before any effect could have happened: nothing was dispatched.</summary>
public sealed class ProviderRefusedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ProviderRefusedException()
    {
    }

    /// <summary>Creates the exception with a reason.</summary>
    public ProviderRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a reason and a cause.</summary>
    public ProviderRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A provider cannot say whether an effect happened: the operation is uncertain and must be reconciled under its identity before any retry.</summary>
public sealed class ProviderUncertainException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ProviderUncertainException()
    {
    }

    /// <summary>Creates the exception with a reason.</summary>
    public ProviderUncertainException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a reason and a cause.</summary>
    public ProviderUncertainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
