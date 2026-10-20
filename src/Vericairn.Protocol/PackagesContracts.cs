// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Observe every declared package for a product release, or the package pinned by an original operation.</summary>
public sealed record PackagesObserveRequest(string By, string? Product = null, string? Version = null, string? Operation = null);

/// <summary>Record an account holder's intended unlisting; this request never changes the registry.</summary>
public sealed record PackagesUnlistRequest(string Product, string Version, string Package, string By);

/// <summary>Record the exact intended deprecation before the account holder changes nuget.org.</summary>
public sealed record PackagesDeprecateRequest(string Product, string Version, string Package, string By,
    IReadOnlyList<string> Reasons, string? Message = null, string? AlternatePackage = null, string? AlternateRange = null);

/// <summary>A digest of the exact response body received at a public registry URL.</summary>
public sealed record RegistryEvidence(string Url, string Hash, int Status, long Bytes, string Encoding = "identity");

/// <summary>Deprecation metadata reported by the registry, including its optional replacement package and version range.</summary>
public sealed record PackageDeprecation(IReadOnlyList<string> Reasons, string? Message = null, string? AlternatePackage = null, string? AlternateRange = null);

/// <summary>Registry facts. A not-found version has unknown listing and deprecation, never an inferred unlisting.</summary>
public sealed record PackageRegistryState(string Package, string Version, string Publication, string Listing, string Deprecation,
    PackageDeprecation? Details, string? PackageContent, string AsOf, IReadOnlyList<RegistryEvidence> Evidence);

/// <summary>A package observation and the record retaining it.</summary>
public sealed record PackageObservationResult(PackageRegistryState State, RecordWritten Observation);

/// <summary>Recorded observations or an account-holder intent. Intent always names the original revision, including on reruns.</summary>
public sealed record PackagesResponse(bool Ok, string Status, string Message, IReadOnlyList<PackageObservationResult> Packages,
    RecordWritten? Intent = null, RecordWritten? Operation = null, string? Instruction = null, Problem? Problem = null);
