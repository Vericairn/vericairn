// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>What an adapter or provider implementation is and may claim: its contract version, the operations it supports, its recorded status and its limitations.</summary>
/// <param name="Id">Stable implementation id, for example git-command or github-releases-gh.</param>
/// <param name="Contract">The versioned contract it implements, for example vericairn.adapter.git/1.</param>
/// <param name="Status">candidate, implemented, qualified or available; never claimed above what tests established.</param>
/// <param name="Operations">The operations it supports.</param>
/// <param name="Limitations">What it does not do.</param>
public sealed record ProviderDescriptor(string Id, string Contract, string Status, IReadOnlyList<string> Operations, IReadOnlyList<string> Limitations)
{
    /// <summary>The statuses an implementation may record.</summary>
    public static readonly string[] Statuses = ["candidate", "implemented", "qualified", "available"];
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
