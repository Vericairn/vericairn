// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;

namespace Vericairn.Records.Signing;

/// <summary>
/// From 0.9.4 (ID-16): the runtime's side of one presence session's pipes: one compact JSON object per line on the helper's standard
/// input and output (design/presence-sessions.md § 6). The release helper's process is one; tests use a stand-in.
/// </summary>
internal interface IPresenceHelperChannel : IDisposable
{
    /// <summary>Sends one message, a compact JSON object; the channel ends the line.</summary>
    void Send(byte[] message);

    /// <summary>The helper's next message, or null when none came within the wait or its output ended.</summary>
    JsonElement? Receive(TimeSpan wait);

    /// <summary>Whether the helper's output has ended.</summary>
    bool Ended { get; }

    /// <summary>Stops the helper at once.</summary>
    void Kill();
}

/// <summary>An entry of an announced list as the terms state it (§ 6): an exact entry with its bytes, or a kind entry.</summary>
internal sealed record PresenceListEntry
{
    /// <summary>Whether the entry is an exact entry.</summary>
    internal bool Exact { get; init; }

    internal string Type { get; init; } = string.Empty;

    internal string? Action { get; init; }

    /// <summary>An exact entry's content hash.</summary>
    internal string? Hash { get; init; }

    /// <summary>An exact entry's canonical bytes, which go to the helper at opening.</summary>
    internal byte[]? Bytes { get; init; }

    internal int Count { get; init; } = 1;

    internal bool AtMost { get; init; }

    internal IReadOnlyList<string> Statuses { get; init; } = [];

    internal IReadOnlyDictionary<string, string> Fixed { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    internal string? Observation { get; init; }

    internal int? Observes { get; init; }

    internal string? ObservesReference { get; init; }

    internal int? PullRequestFrom { get; init; }

    internal string? Trigger { get; init; }

    /// <summary>How many signatures the entry covers.</summary>
    internal int Signatures => Exact ? 1 : Count;
}

/// <summary>A reference of an announced list: a record it may supersede or observe that the session does not sign, with its bytes.</summary>
internal sealed record PresenceListReference(string Id, string Type, string Hash, byte[] Bytes);

/// <summary>Writes a list's terms in the compact canonical form (§ 6).</summary>
internal static class PresenceTermsWriter
{
    /// <summary>The terms' canonical bytes.</summary>
    internal static byte[] Write(string action, string actionIdentity, string principal, string device, string publicKey, string session, int limitSeconds,
        IReadOnlyList<PresenceListEntry> entries, IReadOnlyList<PresenceListReference> references)
    {
        var list = new JsonArray();
        foreach (PresenceListEntry entry in entries)
        {
            var item = new JsonObject { ["entry"] = entry.Exact ? "exact" : "kind", ["type"] = entry.Type };
            if (entry.Action is not null) item["action"] = entry.Action;
            if (entry.Exact)
            {
                item["hash"] = entry.Hash;
                list.Add(item);
                continue;
            }

            item["count"] = entry.Count;
            item["atMost"] = entry.AtMost;
            if (entry.Type == "history-checkpoint")
            {
                item["trigger"] = entry.Trigger ?? "release";
                list.Add(item);
                continue;
            }

            item["statuses"] = new JsonArray([.. entry.Statuses.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)!)]);
            var values = new JsonObject();
            foreach ((string name, string value) in entry.Fixed) values[name] = value;
            item["fixed"] = values;
            if (entry.Observation is not null) item["observation"] = entry.Observation;
            if (entry.Observes is int observes) item["observes"] = observes;
            if (entry.ObservesReference is not null) item["observesReference"] = entry.ObservesReference;
            if (entry.PullRequestFrom is int from) item["pullRequestFrom"] = from;
            list.Add(item);
        }

        var terms = new JsonObject
        {
            ["format"] = PresenceSessionProtocol.Format, ["action"] = action, ["actionIdentity"] = actionIdentity, ["principal"] = principal, ["device"] = device,
            ["publicKey"] = publicKey, ["session"] = session, ["limitSeconds"] = limitSeconds, ["entries"] = list,
            ["references"] = new JsonArray([.. references.OrderBy(r => r.Id, StringComparer.Ordinal)
                .Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["type"] = r.Type, ["hash"] = r.Hash })]),
        };
        using JsonDocument document = JsonDocument.Parse(terms.ToJsonString());
        return CanonicalJson.SerializeUtf8(document.RootElement);
    }

    /// <summary>The bytes the helper receives with the terms: each exact entry's, then each reference's, in the terms' order.</summary>
    internal static IReadOnlyList<byte[]> Bytes(IReadOnlyList<PresenceListEntry> entries, IReadOnlyList<PresenceListReference> references) =>
        [.. entries.Where(e => e.Exact).Select(e => e.Bytes!), .. references.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Bytes)];
}

/// <summary>
/// A session that ended or refused: the refusal's code when the helper refused (§ 6), and the reason it ended. Every signature asked of
/// an ended session fails, so the action renews or stops, as a declined prompt stops it.
/// </summary>
internal sealed class PresenceSessionEnded : InvalidOperationException
{
    internal PresenceSessionEnded(string? code, string reason, string message)
        : base(message)
    {
        Code = code;
        Reason = reason;
    }

    /// <summary>The helper's refusal code, or null when it ended without one.</summary>
    internal string? Code { get; }

    /// <summary>closed, limit, lock, sleep, refusal, parent, input, declined, or silent when the helper stopped answering.</summary>
    internal string Reason { get; }

    /// <summary>Whether the person declined or did not answer the check.</summary>
    internal bool Declined => Reason == "declined" || Code == "check.declined";
}

/// <summary>What the helper answered at opening: the nonce and fingerprint before its dialog, and the opening once the check passed.</summary>
internal sealed record PresenceSessionOpening(string Nonce, string Fingerprint, long OpenedAt, int LimitSeconds);

/// <summary>A signature the helper made under the session, verified against the device's key.</summary>
internal sealed record PresenceSessionSignature(byte[] Signature, int Entry, int Left, int Seconds);

/// <summary>
/// From 0.9.4 (ID-16): drives one presence session over its channel (design/presence-sessions.md §§ 6, 7 and 9). It waits for the check as
/// long as one prompt waits, 300 seconds; any other answer within ten seconds, or the helper is killed; after close, five seconds for
/// the helper's end. Every signature is verified against the device's public key before it is returned.
/// </summary>
internal sealed class PresenceSessionClient : IDisposable
{
    /// <summary>How long the check may wait for the person.</summary>
    internal static readonly TimeSpan CheckWait = TimeSpan.FromSeconds(PresenceSessionProtocol.MaxLimitSeconds);

    /// <summary>How long the helper has to answer any other request.</summary>
    internal static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(10);

    /// <summary>How long the helper has to end after close.</summary>
    internal static readonly TimeSpan EndWait = TimeSpan.FromSeconds(5);

    private readonly IPresenceHelperChannel channel;
    private readonly string publicKey;
    private bool ended;

    internal PresenceSessionClient(IPresenceHelperChannel channel, string publicKey)
    {
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
    }

    /// <summary>Whether the session has ended.</summary>
    internal bool Ended => ended;

    /// <summary>
    /// Sends the terms and their bytes, hands the nonce and fingerprint to <paramref name="prompting"/> as soon as the helper answers with
    /// them, before its dialog, and waits for the check. A refusal, a declined or unanswered check, or a silent helper ends the session.
    /// </summary>
    internal PresenceSessionOpening Open(byte[] terms, IReadOnlyList<byte[]> bytes, Action<string, string>? prompting, TimeSpan? checkWait = null)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(bytes);
        var open = new JsonObject
        {
            ["type"] = "open", ["terms"] = PresenceText.Base64UrlText(terms),
            ["bytes"] = new JsonArray([.. bytes.Select(b => (JsonNode)JsonValue.Create(PresenceText.Base64UrlText(b))!)]),
        };
        Send(open);
        JsonElement first = Expect(AnswerWait, "prompting");
        string nonce = Text(first, "nonce") ?? throw Fail(null, "refusal", "The helper's prompting carries no nonce.");
        string fingerprint = Text(first, "fingerprint") ?? throw Fail(null, "refusal", "The helper's prompting carries no fingerprint.");
        byte[]? nonceBytes = PresenceText.Base64Url(nonce);
        if (nonceBytes is not { Length: 16 } || PresenceSessionRules.Fingerprint(nonceBytes, terms) != fingerprint)
            throw Fail(null, "refusal", "The helper's fingerprint is not the one its nonce and these terms give.");
        prompting?.Invoke(nonce, fingerprint);
        JsonElement opened = Expect(checkWait ?? CheckWait + AnswerWait, "opened");
        if (PresenceText.Timestamp(Text(opened, "openedAt")) is not long openedAt || !opened.TryGetProperty("limitSeconds", out JsonElement limit) || !limit.TryGetInt32(out int seconds))
            throw Fail(null, "refusal", "The helper's opening is not well formed.");
        return new PresenceSessionOpening(nonce, fingerprint, openedAt, seconds);
    }

    /// <summary>Asks the helper to sign one record's canonical bytes; the signature is verified against the device's key.</summary>
    internal PresenceSessionSignature Sign(byte[] record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Send(new JsonObject { ["type"] = "sign", ["record"] = PresenceText.Base64UrlText(record) });
        JsonElement signed = Expect(AnswerWait, "signed");
        byte[]? signature = PresenceText.Base64Url(Text(signed, "signature"));
        if (signature is not { Length: RecordSigning.SignatureBytes }
            || !RecordSigning.Verify(publicKey, RecordSigning.Digest(PresenceText.Hash(record)), PresenceText.Base64UrlText(signature)))
        {
            Kill();
            throw Fail(null, "refusal", "The helper's signature does not verify with the device's key over the record; it is not used.");
        }

        return new PresenceSessionSignature(signature, Number(signed, "entry"), Number(signed, "left"), Number(signed, "seconds"));
    }

    /// <summary>The signatures the list still covers and the seconds left, as the helper counts them.</summary>
    internal (int Left, int Seconds) Status()
    {
        Send(new JsonObject { ["type"] = "status" });
        JsonElement status = Expect(AnswerWait, "status");
        return (Number(status, "left"), Number(status, "seconds"));
    }

    /// <summary>Ends the session: close, then the helper's end within five seconds, or it is killed.</summary>
    internal void Close()
    {
        if (ended) return;
        ended = true;
        try
        {
            channel.Send(Canonical(new JsonObject { ["type"] = "close" }));
            DateTimeOffset until = DateTimeOffset.UtcNow + EndWait;
            while (DateTimeOffset.UtcNow < until && channel.Receive(until - DateTimeOffset.UtcNow) is JsonElement message)
            {
                if (Text(message, "type") == "ended") break;
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // A helper that is gone has ended its session already.
        }
        finally
        {
            channel.Kill();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
        channel.Dispose();
    }

    private void Send(JsonObject message)
    {
        if (ended) throw Fail("session.ended", "closed", "The presence session has ended.");
        try
        {
            channel.Send(Canonical(message));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Kill();
            throw Fail(null, "input", "The helper no longer reads its input.");
        }
    }

    private JsonElement Expect(TimeSpan wait, string type)
    {
        JsonElement? answer = channel.Receive(wait);
        if (answer is not JsonElement message)
        {
            bool gone = channel.Ended;
            Kill();
            throw gone ? Fail(null, "input", "The helper ended without answering.") : Fail(null, "silent", $"The helper did not answer within {wait.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds and was stopped.");
        }

        string? received = Text(message, "type");
        if (received == type) return message;
        if (received == "refused")
        {
            string code = Text(message, "code") ?? "refusal";
            string detail = Text(message, "detail") ?? "The helper refused.";
            string reason = channel.Receive(EndWait) is JsonElement end && Text(end, "type") == "ended" ? Text(end, "reason") ?? "refusal" : "refusal";
            Kill();
            throw Fail(code, reason, $"The presence session was refused ({code}): {detail}");
        }

        if (received == "ended")
        {
            string reason = Text(message, "reason") ?? "refusal";
            Kill();
            throw Fail(reason == "limit" ? "session.ended" : null, reason, $"The presence session ended ({reason}).");
        }

        Kill();
        throw Fail(null, "refusal", $"The helper answered {received ?? "nothing it names"} where the protocol expects {type}.");
    }

    private void Kill()
    {
        ended = true;
        channel.Kill();
    }

    private PresenceSessionEnded Fail(string? code, string reason, string message)
    {
        ended = true;
        return new PresenceSessionEnded(code, reason, message);
    }

    private static byte[] Canonical(JsonObject message)
    {
        using JsonDocument document = JsonDocument.Parse(message.ToJsonString());
        return CanonicalJson.SerializeUtf8(document.RootElement);
    }

    private static string? Text(JsonElement message, string name) => PresenceRecord.Text(message, name);

    private static int Number(JsonElement message, string name) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;
}
