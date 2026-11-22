// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (FIX-145): the way back from an upgrade to the release the estate was pinned to before. Readability is judged before anything
/// is applied: readable when the previous release reads the manifest and every record after the upgrade; one-way when it cannot, which
/// needs the explicit confirmation of <see cref="UpgradeRequest.OneWay"/>; and unconfirmed when the previous pin is a source build whose
/// release is unknown and the upgrade adds something a release first reads, so that whether it reads it cannot be said.
/// </summary>
/// <param name="PreviousPin">The pin before the upgrade, as the plan's from pin.</param>
/// <param name="Readability">readable, one-way or unconfirmed.</param>
/// <param name="Reasons">Each thing the previous release cannot read, or whose reading cannot be confirmed; empty when readable.</param>
/// <param name="Steps">How to return to the previous release, in order, with this machine's paths; what applying will do, for a check.</param>
public sealed record UpgradeReturn(string PreviousPin, string Readability, IReadOnlyList<string> Reasons, IReadOnlyList<string> Steps)
{
    /// <summary>True when the previous release cannot read the estate after the upgrade.</summary>
    public bool OneWay => Readability == "one-way";

    /// <summary>The restore point an applied upgrade recorded before it wrote anything; null for a check.</summary>
    public UpgradeRestorePoint? RestorePoint { get; init; }
}

/// <summary>
/// From 0.9.4 (FIX-145): an upgrade's restore point: the estate repository's commit before the upgrade and an export of this machine's local
/// state for the clone, kept beside its live store. The migration record names its id, commit and inventory hash, never its directory.
/// </summary>
/// <param name="Id">Its id.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="Commit">The estate repository's checked-out commit before the upgrade; null for a local-only estate or one with no repository.</param>
/// <param name="Clean">Whether the working tree had no change beside that commit.</param>
/// <param name="Directory">Where it is kept on the runtime's machine.</param>
/// <param name="StateHash">The hash of its inventory of exported files.</param>
/// <param name="Files">The exported files, by name.</param>
public sealed record UpgradeRestorePoint(string Id, string CreatedAt, string? Commit, bool Clean, string Directory, string StateHash, IReadOnlyList<string> Files);
