// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Types;

/// <summary>A device that holds a signing key for a principal; signed by that key to prove possession.</summary>
public sealed class DeviceType : IRecordType
{
    /// <summary>Connectivity profiles a device may be permitted.</summary>
    public static readonly string[] Profiles = ["embedded", "local-socket", "loopback", "local-network", "relay"];

    /// <summary>Platforms.</summary>
    public static readonly string[] Platforms = ["windows", "linux", "macos", "ios", "android", "other"];

    /// <summary>Whether each signature needs the owner's presence, from record format 2.</summary>
    public static readonly string[] Presences = ["required", "none"];

    /// <summary>What a device is for, from record format 2: ordinary signing, or the operator's offline recovery.</summary>
    public static readonly string[] Purposes = ["signing", "recovery"];

    /// <inheritdoc />
    public string Name => "device";

    /// <inheritdoc />
    public string Summary => "A device holding a signing key for a principal: platform, custody strength, public key and permitted connectivity profiles. Signed by its own key to prove possession; admitted separately.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("principal", "ulid", true, "The principal the device acts for."),
        new("name", "string", true, "Descriptive name, never a machine path."),
        new("platform", string.Join('|', Platforms), true, "Operating system family."),
        new("custody", string.Join('|', DeviceKeyStores.Format2Kinds), true, "How the private key is held; recorded strength, never assumed. macos-secure-enclave from record format 2."),
        new("algorithm", RecordSigning.Algorithm, true, "Signature algorithm."),
        new("publicKey", "string", true, "Base64url SubjectPublicKeyInfo of the P-256 public key."),
        new("profiles", "string[]", true, "Permitted connectivity profiles: embedded, local-socket, loopback, local-network, relay."),
        new("presence", string.Join('|', Presences), true, "Whether each signature needs the owner's presence; required only where the custody enforces it, and never for an agent's or a service's device.", 2),
        new("purpose", string.Join('|', Purposes), true, "signing for ordinary work; recovery for the operator's offline recovery device.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? principal = body.Ulid("principal", true);
        body.Text("name", true, 200);
        body.Enum("platform", true, Platforms);
        string? custody = body.Enum("custody", true, context.Format >= 2 ? DeviceKeyStores.Format2Kinds : DeviceKeyStores.Kinds);
        body.Enum("algorithm", true, RecordSigning.Algorithm);
        string? publicKey = body.Text("publicKey", true, 400);
        IReadOnlyList<string>? profiles = body.StringArray("profiles", true, allowEmpty: false, maxItemLength: 50);
        string? presence = body.Since(2, "presence") ? body.Enum("presence", true, Presences) : null;
        string? purpose = body.Since(2, "purpose") ? body.Enum("purpose", true, Purposes) : null;
        body.NoOtherKeys();
        if (publicKey is not null && !RecordSigning.IsPublicKey(publicKey))
        {
            body.Error("device.public-key", "publicKey", "Not a base64url P-256 SubjectPublicKeyInfo.");
        }

        foreach (string profile in (profiles ?? []).Where(p => Array.IndexOf(Profiles, p) < 0))
        {
            body.Error("body.enum", "profiles", $"Unknown profile '{profile}'. Known: {string.Join(", ", Profiles)}.");
        }

        if (principal is null)
        {
            return;
        }

        RecordEnvelope? owner = context.Principal(principal);
        if (owner is null)
        {
            context.Error("device.principal", $"Principal {principal} has no principal record.");
        }

        // Custody never claims presence it cannot enforce, and an agent's or a service's device signs unattended.
        if (presence == "required" && custody is not null && Array.IndexOf(DeviceKeyStores.PresenceKinds, custody) < 0)
        {
            context.Error("device.presence", $"{custody} custody cannot enforce its owner's presence, so the device cannot claim it.");
        }

        if (presence == "required" && owner is not null && EstateModel.BodyString(owner, "kind") != "human")
        {
            context.Error("device.presence", "An agent's or a service's device signs unattended and never claims presence.");
        }

        if (purpose == "recovery" && !context.HasRole(principal, Roles.Operator))
        {
            context.Error("device.purpose", "A recovery device belongs to an operator.");
        }

        if (context.Record.By != principal && !context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer))
        {
            context.Error("device.author", "A device record is authored by its principal, by the operator, or under the 'administer' scope.");
        }

        if (!context.Record.Signatures.Any(s => s.Device == context.Record.Id))
        {
            context.Error("device.unsigned", "A device record is signed by its own key to prove possession.");
        }

        RecordEnvelope? previous = context.Record.Supersedes is null ? null : context.Find(context.Record.Supersedes);
        if (previous is not null && previous.Type == Name && EstateModel.BodyString(previous, "publicKey") != publicKey)
        {
            context.Error("device.key-change", "A device revision keeps its public key; a new key is a new device.");
        }

        if (previous is not null && previous.Type == Name && (EstateModel.BodyString(previous, "principal") != principal || (context.Format >= 2 && previous.Format >= 2 && EstateModel.BodyString(previous, "purpose") != purpose)))
        {
            context.Error("device.identity", "A device revision keeps its principal and its purpose; a different one is a different device.");
        }
    }
}
