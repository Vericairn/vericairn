// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// The pairing codes a daemon has issued, held in its memory only: a code lives ten minutes, pairs one device, and survives five
/// confirmations that do not match it, after which none of its exchanges is open; a daemon that restarts forgets every code. Only
/// the keys a code gives are kept, never the code. A hello names no code: the registry tries the codes it holds against its MAC, so
/// nothing about a code is learnt, and none of its attempts used, by anyone who does not hold it; a hello that matches none is
/// refused alike. Failed hellos are counted for a minute: an address that sends too many is refused until its minute ends, an
/// address is never forgotten while it is counted (when the count of addresses is full, a new one waits instead), and all of them
/// together have a budget, which bounds the work of trying codes.
/// </summary>
internal sealed class PairingRegistry
{
    /// <summary>How many confirmations that do not match its code a pairing survives.</summary>
    internal const int Attempts = PairingProtocol.Attempts;

    /// <summary>How many codes may wait at once.</summary>
    internal const int MaxPending = 16;

    /// <summary>How many exchanges of one code may be open at once; a further hello closes the oldest.</summary>
    internal const int MaxExchanges = 4;

    /// <summary>
    /// How many hellos matching no code one address may send in a window before it is refused until the window ends: enough that a
    /// mistyped code, or a few, from behind a shared address (a NAT, a proxy, a container's gateway) refuses no other device there.
    /// </summary>
    internal const int MaxFailures = 50;

    /// <summary>How many hellos matching no code all addresses together may send in a window before every hello waits for the next.</summary>
    internal const int MaxFailuresInAll = 4096;

    /// <summary>The most addresses whose failures are counted at once; while that many are, a hello from another waits.</summary>
    internal const int MaxAddresses = 1024;

    /// <summary>How long a code works.</summary>
    internal static readonly TimeSpan Lifetime = PairingProtocol.Lifetime;

    /// <summary>The window failed hellos are counted in.</summary>
    internal static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);

    /// <summary>How long a used or expired code is remembered, so that its holder is told so rather than that it matches nothing.</summary>
    private static readonly TimeSpan Remembered = TimeSpan.FromHours(1);

    /// <summary>The most codes remembered at once, waiting, used or expired; the oldest ended ones go first.</summary>
    private const int MaxRemembered = 64;

    private readonly TimeProvider time;
    private readonly Func<PairingAuthority, string?> authorityRefusal;
    private readonly object gate = new();
    private readonly List<Pairing> pairings = [];
    private readonly Dictionary<string, (DateTimeOffset Since, int Count)> failures = new(StringComparer.Ordinal);
    private (DateTimeOffset Since, int Count) failuresInAll;
    private long opened;

    internal PairingRegistry(TimeProvider time, Func<PairingAuthority, string?> authorityRefusal)
    {
        this.time = time;
        this.authorityRefusal = authorityRefusal;
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

        /// <summary>Its issuing device or administrator's authority was revoked; the code cannot revive.</summary>
        Revoked,
    }

    /// <summary>Issues a code for a principal under an administrator; refused while too many codes wait.</summary>
    internal (string? Code, Pairing? Pairing, string? Problem) Issue(string principal, PairingAuthority issuer, IReadOnlyList<string> profiles)
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Sweep(now);
            foreach (Pairing pending in pairings)
            {
                _ = Refusal(pending, now);
            }

            if (authorityRefusal(issuer) is string refusal)
            {
                return (null, null, refusal);
            }

            if (pairings.Count(p => p.Stage is Stage.Pending or Stage.Completing && now < p.ExpiresAt) >= MaxPending)
            {
                return (null, null, $"{MaxPending} pairing codes are waiting already; use them or let them expire first.");
            }

            string code = PairingProtocol.NewCode();
            (byte[] runtimeKey, byte[] deviceKey) = PairingProtocol.Keys(code);
            var pairing = new Pairing(Ulid.NewUlid().Value, runtimeKey, deviceKey, principal, issuer, profiles, now, now + Lifetime);
            pairings.Add(pairing);
            return (code, pairing, null);
        }
    }

    /// <summary>
    /// The pairing whose code a hello proves with its MAC (32 bytes, decoded once by the caller), over the certificate the runtime
    /// presented on the hello's connection; refused alike when no code it holds matches. Before any code is tried, a hello is refused
    /// from an address that sent too many failed hellos this minute, from a new address while as many addresses as are counted are,
    /// and from anywhere once failed hellos in all have used this minute's budget.
    /// </summary>
    internal (Pairing? Pairing, string? Problem) Hello(PairingHello hello, ReadOnlySpan<byte> mac, string certificate, string address)
    {
        ArgumentNullException.ThrowIfNull(hello);
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Sweep(now);
            bool counted = failures.TryGetValue(address, out (DateTimeOffset Since, int Count) failed);
            if (counted && failed.Count >= MaxFailures)
            {
                return (null, $"Too many pairing attempts that matched no code came from this address; it is refused until {RecordParser.FormatTimestamp(failed.Since + FailureWindow)}.");
            }

            if (!counted && failures.Count >= MaxAddresses)
            {
                DateTimeOffset first = failures.Values.Min(f => f.Since) + FailureWindow;
                return (null, $"Pairing attempts that matched no code came from too many addresses this minute; a new address is refused until {RecordParser.FormatTimestamp(first)}.");
            }

            if (now < failuresInAll.Since + FailureWindow && failuresInAll.Count >= MaxFailuresInAll)
            {
                return (null, $"Too many pairing attempts that matched no code came in all this minute; every hello is refused until {RecordParser.FormatTimestamp(failuresInAll.Since + FailureWindow)}.");
            }

            // Every code is tried, so the time taken says nothing about which one, if any, was close.
            Pairing? found = null;
            foreach (Pairing pairing in pairings)
            {
                if (CryptographicOperations.FixedTimeEquals(PairingProtocol.HelloMacBytes(pairing.DeviceKey, hello.Nonce, certificate), mac) && found is null)
                {
                    found = pairing;
                }
            }

            if (found is null)
            {
                failures[address] = counted ? (failed.Since, failed.Count + 1) : (now, 1);
                failuresInAll = now < failuresInAll.Since + FailureWindow ? (failuresInAll.Since, failuresInAll.Count + 1) : (now, 1);
                return (null, "No pairing here matches this code over this connection: it is mistyped, it was never issued here, or something between this device and the runtime answered. Nothing was recorded.");
            }

            return Refusal(found, now) is string refusal ? (null, refusal) : (found, null);
        }
    }

    /// <summary>
    /// Remembers the challenge an answer sent, under its runtime nonce, for the device's confirmation: at most
    /// <see cref="MaxExchanges"/> of a code at once, the oldest closed for a new one, and none once the code cannot go on. The problem
    /// to answer with instead, when it cannot.
    /// </summary>
    internal string? Open(Pairing pairing, PairingHello hello, PairingChallenge challenge, PairingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        ArgumentNullException.ThrowIfNull(challenge);
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            if (Refusal(pairing, now) is string refusal)
            {
                return refusal;
            }

            while (pairing.Exchanges.Count >= MaxExchanges)
            {
                pairing.Exchanges.Remove(pairing.Exchanges.MinBy(e => e.Value.Order).Key);
            }

            pairing.Exchanges[challenge.RuntimeNonce] = new Exchange(hello, challenge, plan, now, ++opened);
            return null;
        }
    }

    /// <summary>
    /// Starts recording a confirmation: refused for an exchange this registry did not open, for a code that expired or was used, and
    /// for a MAC that does not match, which ends the exchange and uses one of the code's attempts. Only one confirmation of a code is
    /// recorded at a time.
    /// </summary>
    internal (Pairing? Pairing, Exchange? Exchange, string? Problem) Confirm(PairingConfirm confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Pairing? pairing = confirm.RuntimeNonce is null ? null : pairings.FirstOrDefault(p => p.Exchanges.ContainsKey(confirm.RuntimeNonce));
            if (pairing is null || !pairing.Exchanges.Remove(confirm.RuntimeNonce!, out Exchange? exchange))
            {
                return (null, null, "No exchange of a pairing code is open here under this nonce; start the pairing again.");
            }

            if (Refusal(pairing, now) is string refusal)
            {
                return (null, null, refusal);
            }

            if (confirm.Device is null || !PairingProtocol.Matches(PairingProtocol.ConfirmMac(pairing.DeviceKey, exchange.Hello, exchange.Challenge, confirm), confirm.Mac))
            {
                // The code's last attempt closes every exchange of it still open, so nothing more is tried against it.
                pairing.AttemptsLeft--;
                if (pairing.AttemptsLeft <= 0)
                {
                    pairing.Exchanges.Clear();
                }

                return (null, null, "The confirmation does not match this pairing code: the message was changed on its way. Nothing was recorded.");
            }

            pairing.Stage = Stage.Completing;
            return (pairing, exchange, null);
        }
    }

    /// <summary>Ends a confirmation: a recorded device uses the code up; otherwise it waits again.</summary>
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
            pairing.Exchanges.Clear();
        }
    }

    /// <summary>Why a pairing whose code was proved cannot go on; the refusal names no device and nothing of the estate.</summary>
    private string? Refusal(Pairing pairing, DateTimeOffset now)
    {
        if (pairing.Stage == Stage.Pending && authorityRefusal(pairing.Issuer) is not null)
        {
            pairing.Stage = Stage.Revoked;
            pairing.Exchanges.Clear();
        }

        return pairing.Stage switch
        {
            Stage.Used => "This pairing code was used already; a code pairs one device. Ask the administrator for a new one.",
            Stage.Completing => "Another device is completing this pairing code right now.",
            Stage.Revoked => "This pairing code lost its issuing device or administrator's authority; ask the administrator for a new one.",
            _ when now >= pairing.ExpiresAt => $"This pairing code expired at {RecordParser.FormatTimestamp(pairing.ExpiresAt)}; ask the administrator for a new one.",
            _ when pairing.AttemptsLeft <= 0 => $"This pairing code has had {Attempts} confirmations that did not match it; ask the administrator for a new one.",
            _ => null,
        };
    }

    /// <summary>Forgets codes long past their expiry, and the oldest ended ones beyond the most remembered, wiping their keys; and stale failure counts.</summary>
    private void Sweep(DateTimeOffset now)
    {
        List<Pairing> forgotten = [.. pairings.Where(p => p.Stage != Stage.Completing && now >= p.ExpiresAt + Remembered)];
        forgotten.AddRange(pairings.Except(forgotten).Where(p => p.Stage is Stage.Used or Stage.Revoked || now >= p.ExpiresAt).OrderBy(p => p.IssuedAt).Take(Math.Max(0, pairings.Count - forgotten.Count - MaxRemembered)));
        foreach (Pairing old in forgotten)
        {
            CryptographicOperations.ZeroMemory(old.RuntimeKey);
            CryptographicOperations.ZeroMemory(old.DeviceKey);
            pairings.Remove(old);
        }

        // A count is forgotten only once its minute has ended: a full table makes new addresses wait, it never forgives an old one.
        foreach (string ended in failures.Where(f => now >= f.Value.Since + FailureWindow).Select(f => f.Key).ToList())
        {
            failures.Remove(ended);
        }
    }

    /// <summary>One issued code: the keys it gives, whom it pairs for and under whom, and how far it has got.</summary>
    internal sealed class Pairing(string id, byte[] runtimeKey, byte[] deviceKey, string principal, PairingAuthority issuer, IReadOnlyList<string> profiles, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        /// <summary>The pairing's id in the daemon's log; not part of the code.</summary>
        public string Id { get; } = id;

        public byte[] RuntimeKey { get; } = runtimeKey;

        public byte[] DeviceKey { get; } = deviceKey;

        public string Principal { get; } = principal;

        public PairingAuthority Issuer { get; } = issuer;

        public string By => Issuer.Principal;

        public IReadOnlyList<string> Profiles { get; } = profiles;

        public DateTimeOffset IssuedAt { get; } = issuedAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public int AttemptsLeft { get; set; } = Attempts;

        public Stage Stage { get; set; } = Stage.Pending;

        public Dictionary<string, Exchange> Exchanges { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>An answered hello: the hello, the challenge sent, the records it promised, when it was opened and in what order.</summary>
    internal sealed record Exchange(PairingHello Hello, PairingChallenge Challenge, PairingPlan Plan, DateTimeOffset Opened, long Order);
}
