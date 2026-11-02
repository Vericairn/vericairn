// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Signing;

namespace Vericairn.Sdk;

/// <summary>How a new device pairs: its descriptive name and where its key is held.</summary>
/// <param name="Name">The device's descriptive name, never a machine path.</param>
public sealed record PairingOptions(string Name)
{
    /// <summary>Key custody: default (the strongest this machine offers), file, cng, secure-enclave or secret-service.</summary>
    public string? Custody { get; init; }

    /// <summary>Whether every signature needs the owner's presence; by default a person's device needs it wherever its custody enforces it.</summary>
    public bool? Presence { get; init; }

    /// <summary>A key store to make the device's key in instead of the custody's; for tests and unusual machines.</summary>
    public IDeviceKeyStore? KeyStore { get; init; }

    /// <summary>
    /// From 0.9.0 (REMOTE-11): a store in which to make a second key, the presence device, whose every signature needs its owner's
    /// presence. A runtime that offers one records and admits it with the paired device under the same code, for the same principal
    /// and profiles, stating presence required; it signs records, never requests. Its custody must enforce presence, or nothing is
    /// made. Null pairs one device, as before; so does a runtime that does not offer one (an earlier release, an agent's or a
    /// service's principal, or record format 1), and so does a presence key that cannot be made or whose prompt the person declines;
    /// <see cref="PairedRuntime.PresenceDevice"/> then shows null.
    /// </summary>
    public IDeviceKeyStore? PresenceKeyStore { get; init; }

    /// <summary>The kept connection a pairing adds its device to: the challenge must name the same estate, runtime identity and principal before any key is made.</summary>
    internal RuntimeConnection? Joining { get; init; }

    /// <summary>How long to wait for the runtime.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    internal TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>A pairing that completed: the runtime this device now reaches and what it recorded.</summary>
/// <param name="Address">Where the runtime listens.</param>
/// <param name="Estate">The estate's id.</param>
/// <param name="EstateName">Its name.</param>
/// <param name="Identity">The runtime identity's public key, pinned.</param>
/// <param name="Certificate">The fingerprint of the certificate the runtime presented while pairing.</param>
/// <param name="Device">The device record's id: this device's identity, under which its key is held here.</param>
/// <param name="DevicePublicKey">The device's public key.</param>
/// <param name="Admission">The admission's id.</param>
/// <param name="Principal">The principal the device acts for.</param>
/// <param name="PrincipalName">Its name.</param>
/// <param name="Profiles">The profiles the device is permitted.</param>
/// <param name="Custody">How its key is held.</param>
/// <param name="Presence">required or none from record format 2.</param>
public sealed record PairedRuntime(string Address, string Estate, string? EstateName, string Identity, string Certificate, string Device, string DevicePublicKey, string Admission, string Principal, string? PrincipalName, IReadOnlyList<string> Profiles, string Custody, string? Presence)
{
    /// <summary>From 0.9.0 (REMOTE-11): the presence device's id, when one was recorded and admitted with the paired device; its key is held under this id in the presence store.</summary>
    public string? PresenceDevice { get; init; }

    /// <summary>The presence device's public key.</summary>
    public string? PresenceDevicePublicKey { get; init; }

    /// <summary>The presence device's admission id.</summary>
    public string? PresenceAdmission { get; init; }

    /// <summary>How the presence device's key is held.</summary>
    public string? PresenceCustody { get; init; }
}

/// <summary>Why a pairing did not complete: the runtime refused (denied, refused), or nothing answered as the runtime (unavailable).</summary>
public sealed class PairingException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PairingException()
        : this("unavailable", "The pairing did not complete.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public PairingException(string message)
        : this("unavailable", message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    public PairingException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "unavailable";
    }

    /// <summary>Creates the exception with a problem code.</summary>
    public PairingException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>denied, refused, rejected or unavailable.</summary>
    public string Code { get; }
}

/// <summary>
/// The new device's side of a pairing, from 0.6.0 (scheme vericairn-pairing/1), over one TLS connection. The device sends a nonce and
/// its MAC, with the key the code gives it, over the nonce and the certificate the connection showed; the runtime finds the code that
/// MAC proves and answers with its identity, that certificate and the device record it will write, under a MAC only the code gives;
/// the device checks that MAC and that certificate before it makes its key, then presents its key and descriptive identity with its
/// signature of the record under its own MAC; the runtime records the device and its admission and confirms with a last MAC. The
/// code itself never crosses the network.
/// </summary>
public static class RuntimePairing
{
    /// <summary>Pairs this machine with the runtime at an address using a code an administrator issued; throws <see cref="PairingException"/> when it does not complete, with nothing kept here.</summary>
    public static PairedRuntime Pair(string address, string code, PairingOptions options) =>
        PairWithKeysAsync(address, code, options, confirmationStarted: null, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Pairs with cancellation and a deadline for the complete exchange; a lost confirmation preserves uncertainty.</summary>
    public static Task<PairedRuntime> PairAsync(string address, string code, PairingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        bool confirmationStarted = false;
        return ClientOperation.RunAsync("Pair", token => PairWithKeysAsync(address, code, options,
            () => Volatile.Write(ref confirmationStarted, true), token), () => Volatile.Read(ref confirmationStarted), options.Timeout, options.Clock, cancellationToken);
    }

    private static async Task<PairedRuntime> PairWithKeysAsync(string address, string code, PairingOptions options, Action? confirmationStarted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!RuntimeConnection.TryParseAddress(address, out _, out _))
        {
            throw new ArgumentException($"'{address}' is not an address to reach the runtime at: give its address and port, for example 192.168.1.20:7443.", nameof(address));
        }

        if (!PairingProtocol.TryParse(code, out string secret))
        {
            throw new ArgumentException($"That is not a pairing code: a code is {PairingProtocol.CodeLength} characters, shown in four groups of four.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format))
        {
            throw new ArgumentException("A device has a descriptive name, text without control or formatting characters.", nameof(options));
        }

        (byte[] runtimeKey, byte[] deviceKey) = PairingProtocol.Keys(secret);
        try
        {
            try { return await PairExchangeAsync(address.Trim(), runtimeKey, deviceKey, options, confirmationStarted, cancellationToken).ConfigureAwait(false); }
            catch (PairingException exception) when (confirmationStarted is not null)
            {
                throw new ClientRefusedException(exception.Message, exception);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(runtimeKey);
            CryptographicOperations.ZeroMemory(deviceKey);
        }
    }

    private static async Task<PairedRuntime> PairExchangeAsync(string address, byte[] runtimeKey, byte[] deviceKey, PairingOptions options, Action? confirmationStarted, CancellationToken cancellationToken)
    {
        if (!RuntimeConnection.TryParseAddress(address, out string host, out int port))
        {
            throw new PairingException("refused", $"'{address}' is not an address and port.");
        }

        bool legacy = confirmationStarted is null;
        using var timeout = legacy ? new CancellationTokenSource(options.Timeout) : null;
        cancellationToken = timeout?.Token ?? cancellationToken;
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        byte[]? presented = null;
        TlsScreenStream? screen = null;
        SslStream tls;
        try
        {
            await socket.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            // The screen refuses a certificate that names an address to fetch before this machine's TLS reads it.
            screen = new TlsScreenStream(new NetworkStream(socket, ownsSocket: false));
            tls = new SslStream(screen, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(
                CreateAuthenticationOptions((_, certificate, _, _) =>
                {
                    presented = certificate?.GetRawCertData();
                    return certificate is not null;
                }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or IOException or System.Security.Authentication.AuthenticationException || (legacy && exception is OperationCanceledException))
        {
            if (!legacy)
            {
                if (screen?.RefusedPeer == true || exception is System.Security.Authentication.AuthenticationException)
                    throw new ClientRefusedException(exception.Message, exception);
                throw new ClientUnavailableException(false, exception);
            }
            throw new PairingException("unavailable", $"No runtime answered at {address}: {exception.Message}");
        }

        await using (tls)
        {
            string certificate = RuntimeIdentityScheme.CertificateFingerprint(presented ?? []);
            string nonce = PairingProtocol.NewNonce();
            var hello = new PairingHello(nonce, PairingProtocol.HelloMac(deviceKey, nonce, certificate)) { Presence = options.PresenceKeyStore is null ? null : true };
            PairingChallenge challenge = await SendAsync<PairingHello, PairingChallenge>(tls, address, "/v1/pairing/hello", hello, legacy, mayHaveEffect: false, cancellationToken).ConfigureAwait(false);
            if (!PairingProtocol.Matches(PairingProtocol.ChallengeMac(runtimeKey, hello, challenge), challenge.Mac) || challenge.DeviceNonce != nonce)
            {
                throw new PairingException("denied", $"The runtime at {address} does not know this pairing code: something between this device and the runtime answered. Nothing was made or recorded here.");
            }

            if (challenge.Certificate != certificate || !RecordSigning.IsPublicKey(challenge.Identity) || challenge.Profiles is null)
            {
                throw new PairingException("denied", $"The runtime at {address} named a certificate other than the one this connection showed, so something between this device and the runtime answered. Nothing was made or recorded here.");
            }

            if (!DateTimeOffset.TryParseExact(challenge.At, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at))
            {
                throw new PairingException("refused", "The runtime's answer names no time for the device record.");
            }

            if (options.Joining is RuntimeConnection joining && (challenge.Estate != joining.Estate || challenge.Identity != joining.Identity || challenge.Principal != joining.Principal))
            {
                throw new PairingException("refused", "This code pairs a device for another estate, runtime identity or principal than the connection it would join. Nothing was made or recorded here.");
            }

            (IDeviceKeyStore store, bool presence) = Custody(challenge, options);
            IDeviceKeyStore? presenceStore = PresenceCustody(challenge, options);
            byte[] publicKey = store.Create(challenge.Record);
            bool presenceMade = false;
            try
            {
                var device = new PairingDevice(options.Name.Trim(), VericairnHome.Platform(), store.Kind, RecordSigning.EncodePublicKey(publicKey), challenge.Format >= 2 ? (presence ? "required" : "none") : null);
                JsonObject body = DeviceRecords.Body(challenge.Principal, device.Name, device.Platform, device.Custody, device.PublicKey, challenge.Profiles, challenge.Format, device.Presence);
                string hash = DeviceRecords.Hash(DeviceRecords.Draft(challenge.Principal, challenge.Record, at, challenge.Format, body));
                string signature = Base64Url.EncodeToString(store.Sign(challenge.Record, RecordSigning.Digest(hash)));
                var confirm = new PairingConfirm(challenge.RuntimeNonce, device, signature, string.Empty);
                string? presenceHash = null;
                if (presenceStore is not null)
                {
                    // The presence key signs its own record, so its owner's presence is asked for once here, before anything is recorded.
                    try
                    {
                        byte[] presenceKey = presenceStore.Create(challenge.PresenceRecord!);
                        presenceMade = true;
                        var second = new PairingDevice(options.Name.Trim() + " (presence key)", VericairnHome.Platform(), presenceStore.Kind, RecordSigning.EncodePublicKey(presenceKey), "required");
                        JsonObject secondBody = DeviceRecords.Body(challenge.Principal, second.Name, second.Platform, second.Custody, second.PublicKey, challenge.Profiles, challenge.Format, second.Presence);
                        presenceHash = DeviceRecords.Hash(DeviceRecords.Draft(challenge.Principal, challenge.PresenceRecord!, at, challenge.Format, secondBody));
                        confirm = confirm with { PresenceDevice = second, PresenceSignature = Base64Url.EncodeToString(presenceStore.Sign(challenge.PresenceRecord!, RecordSigning.Digest(presenceHash))) };
                    }
                    catch (Exception exception) when (exception is not (OperationCanceledException or PairingException))
                    {
                        // Nothing has been sent. A presence key that cannot be made (a phone without a passcode) or a prompt the
                        // person declines leaves the phone paired with its request device alone, which signs no decision; the
                        // presence key is added later with a new code.
                        if (presenceMade) presenceStore.Remove(challenge.PresenceRecord!);
                        presenceMade = false;
                        presenceStore = null;
                        presenceHash = null;
                        confirm = confirm with { PresenceDevice = null, PresenceSignature = null };
                    }
                }

                confirm = confirm with { Mac = PairingProtocol.ConfirmMac(deviceKey, hello, challenge, confirm) };
                if (!legacy) cancellationToken.ThrowIfCancellationRequested();
                confirmationStarted?.Invoke();
                PairingCompleted completed = await SendAsync<PairingConfirm, PairingCompleted>(tls, address, "/v1/pairing/confirm", confirm, legacy, mayHaveEffect: true, cancellationToken).ConfigureAwait(false);
                if (!PairingProtocol.Matches(PairingProtocol.CompletedMac(runtimeKey, hello, challenge, confirm, completed), completed.Mac) || completed.Device != challenge.Record || completed.Hash != hash
                    || (presenceStore is null ? completed.PresenceDevice is not null || completed.PresenceAdmission is not null || completed.PresenceHash is not null
                        : completed.PresenceDevice != challenge.PresenceRecord || completed.PresenceHash != presenceHash || string.IsNullOrEmpty(completed.PresenceAdmission)))
                {
                    var exception = new PairingException("unavailable", $"The runtime's confirmation does not match this exchange, so this device keeps nothing of it; an administrator revokes device {challenge.Record}{(presenceStore is null ? "" : " and presence device " + challenge.PresenceRecord)} if it was recorded.");
                    if (!legacy) throw new ClientUnavailableException(true, exception);
                    throw exception;
                }

                return new PairedRuntime(address, challenge.Estate, challenge.EstateName, challenge.Identity, challenge.Certificate, challenge.Record, device.PublicKey, completed.Admission, challenge.Principal, challenge.PrincipalName, challenge.Profiles, store.Kind, device.Presence)
                {
                    PresenceDevice = presenceStore is null ? null : completed.PresenceDevice,
                    PresenceDevicePublicKey = confirm.PresenceDevice?.PublicKey,
                    PresenceAdmission = presenceStore is null ? null : completed.PresenceAdmission,
                    PresenceCustody = presenceStore?.Kind,
                };
            }
            catch
            {
                store.Remove(challenge.Record);
                if (presenceMade) presenceStore!.Remove(challenge.PresenceRecord!);
                throw;
            }
        }
    }

    /// <summary>The store the device's key is made in: the strongest custody here unless named, in the record format the estate writes; presence by default for a person's device where the custody enforces it.</summary>
    private static (IDeviceKeyStore Store, bool Presence) Custody(PairingChallenge challenge, PairingOptions options)
    {
        bool person = challenge.PrincipalKind == "human";
        bool formatTwo = challenge.Format >= 2;
        IDeviceKeyStore store;
        bool presence;
        try
        {
            if (options.KeyStore is not null)
            {
                store = options.KeyStore;
                presence = options.Presence ?? false;
            }
            else
            {
                string kind = DeviceKeyStores.Named(options.Custody, enclave: formatTwo).Kind;
                presence = options.Presence ?? (formatTwo && DeviceKeyStores.PresenceByDefault(person, kind));
                store = DeviceKeyStores.Named(kind, presence, enclave: formatTwo);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or PlatformNotSupportedException)
        {
            throw new PairingException("refused", exception is ArgumentException { ParamName: { } name } ? exception.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal) : exception.Message);
        }

        if (presence && !person)
        {
            throw new PairingException("refused", "An agent's or a service's device signs unattended and never claims its owner's presence.");
        }

        if (presence && DeviceKeyStores.PresenceKinds.IndexOf(store.Kind) < 0)
        {
            throw new PairingException("refused", $"{store.Kind} custody cannot enforce its owner's presence, and custody never claims presence it cannot enforce.");
        }

        if (!formatTwo && (presence || store.Kind == SecureEnclaveCustody.Kind))
        {
            throw new PairingException("refused", "This estate writes record format 1, which names neither presence nor Secure Enclave custody; pair with another custody.");
        }

        return (store, presence);
    }

    /// <summary>
    /// The store the presence device's key is made in, when the device asked for one and the runtime offered it; null otherwise. Its
    /// custody must enforce presence, and only a person's device in an estate of record format 2 or later has one.
    /// </summary>
    private static IDeviceKeyStore? PresenceCustody(PairingChallenge challenge, PairingOptions options)
    {
        if (options.PresenceKeyStore is not IDeviceKeyStore store || challenge.PresenceRecord is null)
        {
            return null;
        }

        if (challenge.PrincipalKind != "human" || challenge.Format < 2 || challenge.PresenceRecord == challenge.Record || !Records.Identity.Ulid.IsValid(challenge.PresenceRecord))
        {
            throw new PairingException("refused", "The runtime offered a presence device that a person's device in a record format 2 estate alone has. Nothing was made or recorded here.");
        }

        if (DeviceKeyStores.PresenceKinds.IndexOf(store.Kind) < 0)
        {
            throw new PairingException("refused", $"{store.Kind} custody cannot enforce its owner's presence, so it cannot hold a presence device. Nothing was made or recorded here.");
        }

        return store;
    }

    private static SslClientAuthenticationOptions CreateAuthenticationOptions(RemoteCertificateValidationCallback validate) =>
        new SslClientAuthenticationOptions
        {
            TargetHost = "vericairn-runtime",
            AllowTlsResume = false,
            AllowRenegotiation = false,
            ApplicationProtocols = [SslApplicationProtocol.Http11],

            // TLS 1.2 only, so the runtime's certificate travels in the clear, where the screen reads it first.
            EnabledSslProtocols = TlsScreenStream.Protocols,
            CipherSuitesPolicy = TlsScreenStream.CipherSuites,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            CertificateChainPolicy = LocalNetworkTransport.OfflineChainPolicy(),

            // Any certificate completes the handshake: the hello's MAC covers it, and the runtime's answer must name it.
            RemoteCertificateValidationCallback = validate,
        };

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(Stream tls, string address, string path, TRequest request, bool legacy, bool mayHaveEffect, CancellationToken cancellationToken)
        where TResponse : class
    {
        int status;
        byte[] answer;
        try
        {
            (status, answer) = await RawHttp.PostJsonAsync(tls, address, path, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or RuntimeConnectionException || (legacy && exception is OperationCanceledException))
        {
            if (!legacy) throw new ClientUnavailableException(mayHaveEffect, exception);
            throw new PairingException("unavailable", $"The runtime at {address} did not answer: {exception.Message}");
        }

        if (status == 200)
        {
            try
            {
                return JsonSerializer.Deserialize<TResponse>(answer, ProtocolJson.Options) ?? throw new JsonException();
            }
            catch (JsonException)
            {
                var exception = new PairingException("unavailable", $"The runtime at {address} answered nothing readable.");
                if (!legacy) throw new ClientUnavailableException(mayHaveEffect, exception);
                throw exception;
            }
        }

        Problem? problem = RawHttp.ProblemOf(answer);
        string findings = problem is { Findings.Count: > 0 } ? " " + string.Join(" ", problem.Findings.Select(f => $"{f.Code}: {f.Message}")) : string.Empty;
        if (!legacy && status >= 500 && problem?.Code is not ("refused" or "denied"))
            throw new ClientUnavailableException(mayHaveEffect, new PairingException("unavailable", (problem?.Message ?? $"The runtime at {address} answered {status}.") + findings));
        throw new PairingException(problem?.Code ?? "unavailable", (problem?.Message ?? $"The runtime at {address} answered {status}.") + findings);
    }
}
