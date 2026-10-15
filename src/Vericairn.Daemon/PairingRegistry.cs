// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// The pairing codes a daemon has issued, held in its memory only: a code lives ten minutes, pairs one device, and allows five
/// exchanges; a daemon that restarts forgets every code. Only the keys a code gives are kept, never the code itself.
/// </summary>
internal sealed class PairingRegistry
{
    /// <summary>How many exchanges one code allows; each answer to a hello uses one.</summary>
    internal const int Attempts = PairingProtocol.Attempts;

    /// <summary>How many codes may wait at once.</summary>
    internal const int MaxPending = 16;

    /// <summary>How long a code works.</summary>
    internal static readonly TimeSpan Lifetime = PairingProtocol.Lifetime;

    /// <summary>How long a used or expired code is remembered, so that it is refused as such rather than as unknown.</summary>
    private static readonly TimeSpan Remembered = TimeSpan.FromHours(1);

    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly Dictionary<string, Pairing> pairings = new(StringComparer.Ordinal);

    internal PairingRegistry(TimeProvider time)
    {
        this.time = time;
    }

    /// <summary>Where a pairing stands.</summary>
    internal enum Stage
    {
        /// <summary>Waiting for its device.</summary>
        Pending,

        /// <summary>A device's confirmation is being recorded.</summary>
        Completing,

        /// <summary>Its device is recorded; the code pairs nothing more.</summary>
        Used,
    }

    /// <summary>Issues a code for a principal under an administrator; refused while too many codes wait.</summary>
    internal (string? Code, Pairing? Pairing, string? Problem) Issue(string principal, string by, IReadOnlyList<string> profiles)
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Sweep(now);
            if (pairings.Values.Count(p => p.Stage != Stage.Used && now < p.ExpiresAt) >= MaxPending)
            {
                return (null, null, $"{MaxPending} pairing codes are waiting already; use them or let them expire first.");
            }

            string code;
            do
            {
                code = PairingProtocol.NewCode();
            }
            while (pairings.ContainsKey(code[..PairingProtocol.HandleLength]));

            string handle = code[..PairingProtocol.HandleLength];
            (byte[] runtimeKey, byte[] deviceKey) = PairingProtocol.Keys(handle, code[PairingProtocol.HandleLength..]);
            var pairing = new Pairing(handle, runtimeKey, deviceKey, principal, by, profiles, now, now + Lifetime);
            pairings[handle] = pairing;
            return (code, pairing, null);
        }
    }

    /// <summary>Takes one attempt of the pairing a hello names, when it is waiting, has not expired and has an attempt left.</summary>
    internal (Pairing? Pairing, string? Problem) Hello(PairingHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Sweep(now);
            if (hello.Handle is null || !pairings.TryGetValue(hello.Handle.ToUpperInvariant(), out Pairing? pairing))
            {
                return (null, "No pairing here has this code: check it, or ask the administrator for a new one.");
            }

            if (Refusal(pairing, now) is string refusal)
            {
                return (null, refusal);
            }

            if (pairing.AttemptsLeft <= 0)
            {
                return (null, $"This pairing code has had its {Attempts} attempts; ask the administrator for a new one.");
            }

            pairing.AttemptsLeft--;
            return (pairing, null);
        }
    }

    /// <summary>Remembers the challenge an answer sent, under its runtime nonce, for the device's confirmation.</summary>
    internal void Open(Pairing pairing, PairingChallenge challenge, PairingPlan plan)
    {
        lock (gate)
        {
            pairing.Exchanges[challenge.RuntimeNonce] = new Exchange(challenge, plan);
        }
    }

    /// <summary>
    /// Starts recording a confirmation: refused for an exchange this registry did not open, for a code that expired or was used,
    /// and for a MAC that does not match, which also ends the exchange. Only one confirmation of a code is recorded at a time.
    /// </summary>
    internal (Pairing? Pairing, Exchange? Exchange, string? Problem) Confirm(PairingConfirm confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            if (confirm.Handle is null || confirm.RuntimeNonce is null || !pairings.TryGetValue(confirm.Handle.ToUpperInvariant(), out Pairing? pairing) || !pairing.Exchanges.Remove(confirm.RuntimeNonce, out Exchange? exchange))
            {
                return (null, null, "No exchange of this pairing code is open here; start the pairing again.");
            }

            if (Refusal(pairing, now) is string refusal)
            {
                return (null, null, refusal);
            }

            if (confirm.Device is null || !PairingProtocol.Matches(PairingProtocol.ConfirmMac(pairing.DeviceKey, exchange.Challenge, confirm), confirm.Mac))
            {
                return (null, null, "The confirmation does not match this pairing code: the code was mistyped, or the message was changed on its way. Nothing was recorded.");
            }

            pairing.Stage = Stage.Completing;
            return (pairing, exchange, null);
        }
    }

    /// <summary>Ends a confirmation: a recorded device uses the code up; otherwise it waits again while it has attempts left.</summary>
    internal void Finish(Pairing pairing, string? device)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        lock (gate)
        {
            if (device is null)
            {
                pairing.Stage = Stage.Pending;
                return;
            }

            pairing.Stage = Stage.Used;
            pairing.Device = device;
            pairing.Exchanges.Clear();
        }
    }

    private static string? Refusal(Pairing pairing, DateTimeOffset now) => pairing.Stage switch
    {
        Stage.Used => $"This pairing code was used already, by device {pairing.Device}; a code pairs one device. Ask the administrator for a new one.",
        Stage.Completing => "Another device is completing this pairing code right now.",
        _ when now >= pairing.ExpiresAt => $"This pairing code expired at {RecordParser.FormatTimestamp(pairing.ExpiresAt)}; ask the administrator for a new one.",
        _ => null,
    };

    /// <summary>Forgets codes long past their expiry, wiping their keys.</summary>
    private void Sweep(DateTimeOffset now)
    {
        foreach (Pairing old in pairings.Values.Where(p => now >= p.ExpiresAt + Remembered && p.Stage != Stage.Completing).ToList())
        {
            CryptographicOperations.ZeroMemory(old.RuntimeKey);
            CryptographicOperations.ZeroMemory(old.DeviceKey);
            pairings.Remove(old.Handle);
        }
    }

    /// <summary>One issued code: its handle, the keys it gives, whom it pairs for and under whom, and how far it has got.</summary>
    internal sealed class Pairing(string handle, byte[] runtimeKey, byte[] deviceKey, string principal, string by, IReadOnlyList<string> profiles, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        public string Handle { get; } = handle;

        public byte[] RuntimeKey { get; } = runtimeKey;

        public byte[] DeviceKey { get; } = deviceKey;

        public string Principal { get; } = principal;

        public string By { get; } = by;

        public IReadOnlyList<string> Profiles { get; } = profiles;

        public DateTimeOffset IssuedAt { get; } = issuedAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public int AttemptsLeft { get; set; } = Attempts;

        public Stage Stage { get; set; } = Stage.Pending;

        public string? Device { get; set; }

        public Dictionary<string, Exchange> Exchanges { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>An answered hello: the challenge sent and the records it promised.</summary>
    internal sealed record Exchange(PairingChallenge Challenge, PairingPlan Plan);
}
