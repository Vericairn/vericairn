// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Signing;

namespace Vericairn.TestSupport;

/// <summary>
/// A stand-in for the release helper's side of one presence session (design/presence-sessions.md § 6), in process and on every platform:
/// it reads the terms and records with the same checks the helper applies (the runtime's port of them, held to the shared vectors), signs
/// with a file key in place of the Secure Enclave, and answers the protocol's messages. Its check is simulated: passed, declined, or left
/// unanswered; it can refuse a chosen signature as the helper would refuse a record its own checks reject, and it counts what it was sent.
/// It asks nobody for presence, so it proves the runtime's side of the protocol, never a dialog.
/// </summary>
internal sealed class PresenceHelperStandIn : IPresenceHelperChannel
{
    private readonly BlockingCollection<JsonElement> answers = [];
    private readonly Func<byte[], byte[]> sign;
    private readonly byte[] publicKey;
    private readonly TimeProvider clock;
    private PresenceSessionRules? rules;
    private long openedAt;
    private long openedTimestamp;
    private bool ended;

    /// <param name="sign">Signs a 32-byte digest with the device's key.</param>
    /// <param name="publicKey">The device's public key as SubjectPublicKeyInfo bytes, which the terms must state.</param>
    /// <param name="clock">The wall clock and the session's elapsed time.</param>
    internal PresenceHelperStandIn(Func<byte[], byte[]> sign, byte[] publicKey, TimeProvider? clock = null)
    {
        this.sign = sign;
        this.publicKey = publicKey;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>passed, declined, unavailable or unanswered.</summary>
    internal string Check { get; init; } = "passed";

    /// <summary>The signature, counted from 1, the stand-in refuses with <see cref="RefuseCode"/> whatever its checks say; 0 refuses none.</summary>
    internal int RefuseAt { get; init; }

    internal string RefuseCode { get; init; } = "record.outside";

    /// <summary>A refusal the stand-in answers open with before any dialog, such as helper.unsigned.</summary>
    internal string? RefuseOpen { get; init; }

    /// <summary>The terms each open carried.</summary>
    internal List<byte[]> Terms { get; } = [];

    /// <summary>The content hashes of the records it signed, in order.</summary>
    internal List<string> SignedHashes { get; } = [];

    /// <summary>How many status messages it answered.</summary>
    internal int Statuses { get; private set; }

    /// <summary>Whether the runtime closed the session.</summary>
    internal bool Closed { get; private set; }

    /// <summary>Whether the stand-in was killed.</summary>
    internal bool Killed { get; private set; }

    /// <summary>The rules of the open session, once opened.</summary>
    internal PresenceSessionRules? Rules => rules;

    /// <summary>Runs before each signature with its number from 1, before it is checked.</summary>
    internal Action<int>? BeforeSigning { get; init; }

    /// <inheritdoc />
    public bool Ended => ended && answers.Count == 0;

    /// <inheritdoc />
    public void Send(byte[] message)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        JsonElement root = document.RootElement;
        string? type = root.GetProperty("type").GetString();
        if (ended) return;
        switch (type)
        {
            case "open":
                Open(root);
                break;
            case "sign":
                Sign(root);
                break;
            case "status":
                Statuses++;
                Answer(new JsonObject { ["type"] = "status", ["left"] = rules?.Left ?? 0, ["seconds"] = Seconds() });
                break;
            case "close":
                Closed = true;
                End("closed");
                break;
        }
    }

    /// <inheritdoc />
    public JsonElement? Receive(TimeSpan wait) => answers.TryTake(out JsonElement message, wait < TimeSpan.Zero ? TimeSpan.Zero : wait) ? message : null;

    /// <inheritdoc />
    public void Kill()
    {
        Killed = true;
        ended = true;
    }

    /// <inheritdoc />
    public void Dispose() => Kill();

    private void Open(JsonElement message)
    {
        if (RefuseOpen is string code)
        {
            Refuse(code, "The stand-in refuses to open.", "refusal");
            return;
        }

        byte[] terms = PresenceText.Base64Url(message.GetProperty("terms").GetString())!;
        Terms.Add(terms);
        List<byte[]> bytes = [.. message.GetProperty("bytes").EnumerateArray().Select(b => PresenceText.Base64Url(b.GetString())!)];
        try
        {
            rules = PresenceSessionRules.Open(terms, bytes, clock.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (PresenceSessionRefusal refusal)
        {
            Refuse(refusal.Code, refusal.Message, "refusal");
            return;
        }

        if (!rules.PublicKey.AsSpan().SequenceEqual(publicKey))
        {
            Refuse("terms.key", "The blob's public key differs from the terms' publicKey.", "refusal");
            return;
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        Answer(new JsonObject { ["type"] = "prompting", ["nonce"] = PresenceText.Base64UrlText(nonce), ["fingerprint"] = PresenceSessionRules.Fingerprint(nonce, terms) });
        switch (Check)
        {
            case "declined":
                Refuse("check.declined", "The presence check was declined.", "declined");
                return;
            case "unavailable":
                Refuse("check.unavailable", "The presence check is unavailable.", "refusal");
                return;
            case "unanswered":
                return;
        }

        openedAt = clock.GetUtcNow().ToUnixTimeSeconds();
        openedTimestamp = clock.GetTimestamp();
        Answer(new JsonObject { ["type"] = "opened", ["openedAt"] = Vericairn.Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(openedAt)), ["limitSeconds"] = rules.LimitSeconds });
    }

    private void Sign(JsonElement message)
    {
        if (rules is null) return;
        int number = SignedHashes.Count + 1;
        BeforeSigning?.Invoke(number);
        double elapsed = clock.GetElapsedTime(openedTimestamp).TotalSeconds;
        if (elapsed >= rules.LimitSeconds)
        {
            Refuse("session.ended", "The session reached its limit.", "limit");
            return;
        }

        byte[] record = PresenceText.Base64Url(message.GetProperty("record").GetString())!;
        if (number == RefuseAt)
        {
            Refuse(RefuseCode, "The stand-in refuses this record.", "refusal");
            return;
        }

        int entry;
        PresenceRecord parsed;
        try
        {
            entry = rules.Match(record, openedAt, elapsed, out parsed);
        }
        catch (PresenceSessionRefusal refusal)
        {
            Refuse(refusal.Code, refusal.Message, "refusal");
            return;
        }

        rules.Signed(parsed, entry);
        SignedHashes.Add(parsed.Hash);
        byte[] signature = sign(SHA256.HashData(record));
        Answer(new JsonObject
        {
            ["type"] = "signed", ["signature"] = PresenceText.Base64UrlText(signature), ["entry"] = entry, ["left"] = rules.Left, ["seconds"] = Seconds(),
        });
    }

    private int Seconds() => rules is null ? 0 : Math.Max(0, (int)Math.Floor(rules.LimitSeconds - clock.GetElapsedTime(openedTimestamp).TotalSeconds));

    private void Refuse(string code, string detail, string reason)
    {
        Answer(new JsonObject { ["type"] = "refused", ["code"] = code, ["detail"] = detail });
        End(reason);
    }

    private void End(string reason)
    {
        Answer(new JsonObject { ["type"] = "ended", ["reason"] = reason });
        ended = true;
    }

    private void Answer(JsonObject message)
    {
        using JsonDocument document = JsonDocument.Parse(message.ToJsonString());
        using JsonDocument canonical = JsonDocument.Parse(CanonicalJson.SerializeUtf8(document.RootElement));
        answers.Add(canonical.RootElement.Clone());
    }
}

/// <summary>A clock tests move by hand: the wall clock and the elapsed time a session counts.</summary>
internal sealed class SteppedClock(DateTimeOffset start) : TimeProvider
{
    private long ticks;

    internal void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => start + TimeSpan.FromTicks(Interlocked.Read(ref ticks));

    public override long GetTimestamp() => Interlocked.Read(ref ticks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}
