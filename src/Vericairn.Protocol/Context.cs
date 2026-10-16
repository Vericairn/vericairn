// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A derived context artifact, never an assignment, authority or an editable record.</summary>
/// <param name="Hash">SHA-256 of the exact canonical pretty UTF-8 payload, distinct from record content hashes.</param>
/// <param name="Bytes">The serialized payload's byte count, including its manifest, originals, derivatives and accounting.</param>
/// <param name="Payload">The versioned context contract. The hash and byte count are an excluded sidecar.</param>
public sealed record ContextPackage(string Hash, long Bytes, JsonElement Payload);

/// <summary>A context package or a refusal. Human text is a separately identified rendering, not a model-fit claim.</summary>
/// <param name="Text">Human rendering.</param>
/// <param name="Package">The package, absent on refusal.</param>
/// <param name="Problem">Why assembly could not complete.</param>
public sealed record ContextResponse(string Text, ContextPackage? Package, Problem? Problem);

/// <summary>The exact original read through a retained context manifest, with permission checked again.</summary>
/// <param name="Identity">Pinned source identity; absent when completeness cannot be confirmed.</param>
/// <param name="Content">Original bytes (base64 in JSON), never the latest revision or a derivative.</param>
/// <param name="Problem">Why bytes cannot be returned.</param>
public sealed record ContextReadResponse(JsonElement? Identity, byte[]? Content, Problem? Problem);
