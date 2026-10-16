// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A retained hand-over. Hash and bytes measure the canonical pretty UTF-8 payload, including its original bytes and all framing; this sidecar is excluded.</summary>
/// <param name="Hash">SHA-256 of the serialized payload, not a record hash or a signature.</param>
/// <param name="Bytes">Measured payload bytes.</param>
/// <param name="Payload">Versioned, deterministic record-derived package; no authority is granted by it.</param>
public sealed record HandoffPackage(string Hash, long Bytes, JsonElement Payload);

/// <summary>A hand-over, or every reason it could not be assembled.</summary>
/// <param name="Text">Complete human rendering, with originals apart from the derived presentation.</param>
/// <param name="Package">The retained package on success; null on refusal.</param>
/// <param name="Problem">Refusal with all missing or invalid sources; null on success.</param>
public sealed record HandoffResponse(string Text, HandoffPackage? Package, Problem? Problem);

/// <summary>Every observed difference since a retained hand-over; it never restarts work or restores authority.</summary>
/// <param name="Text">Complete human rendering.</param>
/// <param name="Json">Machine comparison, or null when the retained package is invalid.</param>
/// <param name="HasErrors">Whether current records are invalid, missing or changed in place.</param>
/// <param name="Problem">Why the baseline could not be used; null on a comparison.</param>
public sealed record ResumeResponse(string Text, JsonElement? Json, bool HasErrors, Problem? Problem);

/// <summary>An explicit observation of the estate's status by a person. The runtime supplies the timestamp, subject and facts.</summary>
/// <param name="By">The observing human principal.</param>
public sealed record EstateObserveRequest(string By);
