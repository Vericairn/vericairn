// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Events;

/// <summary>
/// One item of a derived projection, as a pass derives it: what an event about it carries and the state a later pass compares. It
/// never holds a body, title, summary, path, secret or command, only identities, hashes, kinds, states and categories.
/// </summary>
/// <param name="Kind">The event kind it raises.</param>
/// <param name="Subject">What it concerns.</param>
/// <param name="Sources">The records it rests on, at most 16.</param>
/// <param name="State">Its derived state: a state word or an opaque digest of one.</param>
/// <param name="To">The principal concerned.</param>
/// <param name="Category">The attention category.</param>
/// <param name="Private">True for per-principal state.</param>
/// <param name="Related">Record ids a subjects filter matches.</param>
internal sealed record EventItem(string Kind, EventReference Subject, IReadOnlyList<EventReference> Sources, string State, string? To, string? Category,
    bool Private, IReadOnlyList<string> Related)
{
    /// <summary>What a change is judged by: everything an envelope carries about the item, and its state.</summary>
    [JsonIgnore]
    public string Fingerprint => JsonSerializer.Serialize(new { Kind, Subject, Sources, State, To, Category }, ProtocolJson.Options);

    /// <summary>
    /// The envelope's key: a hash of the kind, subject, exact sources and state, with the principal concerned, so the same change derived
    /// again has the same key and two principals' per-principal events never share one.
    /// </summary>
    public string Key(string state) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind = Kind, subject = Subject, sources = Sources, state, to = To }, ProtocolJson.Options)));
}
