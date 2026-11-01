// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>
/// A record a device will sign with its own key, as the runtime fixed it for that device: the plan to sign before it lapses, the
/// record's id and time, its unsigned form exactly as it will be written, for the person to review, and the hash the device signs.
/// Preparing writes nothing.
/// </summary>
/// <param name="Plan">The plan, for the device that prepared it, once, until <paramref name="ExpiresAt"/>.</param>
/// <param name="Id">The record's id.</param>
/// <param name="At">The record's authoring time.</param>
/// <param name="Hash">The record's content hash; the device signs its record-signing digest.</param>
/// <param name="ExpiresAt">When the plan lapses unsigned.</param>
/// <param name="Record">The unsigned record envelope exactly as it will be written.</param>
public sealed record RecordPrepared(string Plan, string Id, string At, string Hash, string ExpiresAt, JsonElement Record);

/// <summary>What preparing a device-signed record found: the plan, or why there is none.</summary>
/// <param name="Ok">True when a plan was made.</param>
/// <param name="Prepared">The plan.</param>
/// <param name="Problem">Why there is none.</param>
public sealed record RecordPrepareResponse(bool Ok, RecordPrepared? Prepared = null, Problem? Problem = null);

/// <summary>The requesting device's signature of a record the runtime prepared for it.</summary>
/// <param name="Plan">The plan the runtime returned.</param>
/// <param name="Signature">The device's signature of the record's digest, encoded as record signatures are.</param>
public sealed record SignedRecordRequest(string Plan, string Signature);
