// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>A read of a brief, record, pinned document or context package; identifiers grant no authority.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceRequest(string Kind, string Key, long? MaxBytes = null, string? Recipient = null, string? Assignment = null, string? Form = null, string? Estate = null);

/// <summary>A discoverable read, with no body or retained state. The runtime rechecks access on retrieval.</summary>
public sealed record ResourceEntry(string Name, string Description, string MimeType, ResourceRequest Request);

/// <summary>The current estate resource catalogue, or a refusal without metadata.</summary>
public sealed record ResourcesResponse(string? Estate, IReadOnlyList<ResourceEntry> Resources, Problem? Problem = null);

/// <summary>Exact bytes and their SHA-256; the optional source hash identifies the record or context payload separately.</summary>
public sealed record ResourceResponse(byte[]? Content, string? MimeType, string? Hash, string? SourceHash = null, Problem? Problem = null);
