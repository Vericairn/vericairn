// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class DeviceOperations(CommandContext context)
{
    public int DevicesCreate(string root, string principal, string name, string? custody, bool? presence, string[] profiles, bool json) =>
        context.WithApi(root, api => context.ReportOutcome(api.DeviceCreate(new DeviceCreateRequest(principal, name, custody, profiles.Length == 0 ? null : profiles, presence)), json));

    public int DevicesList(string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<DeviceRow> rows = api.Devices();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows.Select(r => new { id = r.Id, principal = r.Principal, principalName = r.PrincipalName, name = r.Name, platform = r.Platform, custody = r.Custody, presence = r.Presence, purpose = r.Purpose, admission = r.Admission, heldHere = r.HeldHere, profiles = r.Profiles })));
        }
        else
        {
            foreach (DeviceRow row in rows)
            {
                string presence = row.Presence is null ? string.Empty : row.Presence == "required" ? "  presence required" : "  no presence";
                string purpose = row.Purpose == "recovery" ? "  recovery" : string.Empty;
                string profiles = row.Profiles is { Count: > 0 } ? "  profiles " + string.Join(", ", row.Profiles) : string.Empty;
                context.Output.WriteLine($"{row.Id}  {row.PrincipalName}  {row.Name}  {row.Platform}  {row.Custody}{presence}{purpose}  {row.Admission}{(row.HeldHere ? "  key held on this machine" : string.Empty)}{profiles}");
            }
        }

        return Clean;
    });

    public int DevicesAdmit(string root, string deviceId, string by, string note, string? recovery, bool passphraseStdin, bool json)
    {
        string? passphrase = null;
        if (recovery is not null && (passphrase = Passphrase(passphraseStdin, confirm: false)) is null)
        {
            return Failed;
        }

        return context.WithApi(root, api => context.ReportOutcome(api.DeviceAdmit(new AdmitRequest(deviceId, by, note, recovery is null ? null : Path.GetFullPath(recovery), passphrase)), json));
    }

    public int DevicesRevoke(string root, string deviceId, string by, string reason, string? recovery, bool passphraseStdin, bool json)
    {
        string? passphrase = null;
        if (recovery is not null && (passphrase = Passphrase(passphraseStdin, confirm: false)) is null)
        {
            return Failed;
        }

        return context.WithApi(root, api => context.ReportOutcome(api.DeviceRevoke(new RevokeRequest(deviceId, by, reason, recovery is null ? null : Path.GetFullPath(recovery), passphrase)), json));
    }

    public int DevicesRecovery(string root, string principal, string name, string output, bool passphraseStdin, bool json)
    {
        string? passphrase = Passphrase(passphraseStdin, confirm: true);
        if (passphrase is null)
        {
            return Failed;
        }

        string path = Path.GetFullPath(output);
        int code = context.WithApi(root, api => context.ReportOutcome(api.DeviceRecovery(new DeviceRecoveryRequest(principal, name, path, passphrase)), json));
        if (code == 0 && !json)
        {
            context.Output.WriteLine($"The recovery key is in {path}, encrypted with your passphrase. Move it off this machine now and keep the passphrase apart from it.");
        }

        return code;
    }

    /// <summary>Issues a pairing code through the daemon serving the estate, which holds pending codes in its memory and serves the exchange.</summary>
    public int DevicesPair(string root, string principal, string by, string[] profiles, bool json) => context.WithDaemon(root, "pairing", client =>
    {
        (PairingIssued? issued, Problem? problem) = client.IssuePairingAsync(new PairingIssueRequest(principal, by, profiles.Length == 0 ? null : profiles), context.CancellationToken).GetAwaiter().GetResult();
        if (issued is null)
        {
            return context.Fail(problem?.Message ?? "No pairing code was issued.");
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(issued, ProtocolJson.Options));
            return Clean;
        }

        // A runtime listening on every interface has no one address to give: the person pairing names this machine's.
        string address = System.Net.IPEndPoint.TryParse(issued.Address, out System.Net.IPEndPoint? at) && (at.Address.Equals(System.Net.IPAddress.Any) || at.Address.Equals(System.Net.IPAddress.IPv6Any))
            ? $"<this machine's address>:{at.Port}"
            : issued.Address;
        context.Output.WriteLine($"pairing code  {issued.Code}");
        context.Output.WriteLine($"  for {issued.PrincipalName ?? issued.Principal} ({issued.Principal}), permitted {string.Join(", ", issued.Profiles)}");
        context.Output.WriteLine($"  valid until {issued.ExpiresAt}, for one device and {issued.Attempts} attempts; show it only to the person pairing");
        context.Output.WriteLine($"  runtime identity {issued.Identity}");
        context.Output.WriteLine($"on the new device: vericairn connections add {address} --code {issued.Code} --name <device name>");
        return Clean;
    });

    /// <summary>Lists the writes paired devices made over the local network, from the ledger beside the live store; no daemon is needed.</summary>
    public int DevicesWrites(string root, string? device, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            List<LocalNetworkWrite> writes = [.. LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!).Where(w => device is null || w.Device == device)];
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(writes, ProtocolJson.Options));
                return Clean;
            }

            if (writes.Count == 0)
            {
                context.Output.WriteLine(device is null ? "no paired device has written over the local network through this machine's runtime" : $"device {device} has written nothing over the local network through this machine's runtime");
            }

            foreach (LocalNetworkWrite write in writes)
            {
                string outcome = write.State switch
                {
                    "interrupted" => write.Candidates is { Count: > 0 } candidates ? $"interrupted, the runtime stopped before its outcome was kept; it may have written {string.Join(", ", candidates)}" : "interrupted, the runtime stopped before its outcome was kept; its principal wrote nothing since",
                    "pending" => "pending, no outcome kept yet",
                    "failed" => "failed, nothing written",
                    _ => "written",
                };
                context.Output.WriteLine($"{write.At}  device {write.Device} for {write.Principal}  {write.Operation}  {outcome}{(write.Record is null ? string.Empty : $"  {write.Record}{(write.Hash is null ? string.Empty : "  " + write.Hash)}")}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int DevicesRename(string root, string deviceId, string name, bool json) =>
        context.WithApi(root, api => context.ReportOutcome(api.DeviceRename(new DeviceRenameRequest(deviceId, name)), json));

    public int DevicesRotate(string root, string deviceId, string? custody, bool? presence, bool json) =>
        context.WithApi(root, api => context.ReportOutcome(api.DeviceRotate(new DeviceRotateRequest(deviceId, custody, presence)), json));

    /// <summary>A passphrase from the first line of standard input, or typed at the terminal without echo; null, with the reason written, when there is none.</summary>
    private string? Passphrase(bool fromStandardInput, bool confirm)
    {
        if (fromStandardInput)
        {
            string? line = Console.In.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                context.Error.WriteLine("Standard input holds no passphrase.");
                return null;
            }

            return line;
        }

        if (Console.IsInputRedirected)
        {
            context.Error.WriteLine("A passphrase is typed at a terminal, or given on standard input with --passphrase-stdin.");
            return null;
        }

        string first = ReadHidden("Passphrase: ");
        if (confirm && ReadHidden("Passphrase again: ") != first)
        {
            context.Error.WriteLine("The two passphrases differ; nothing was made.");
            return null;
        }

        return first;
    }

    private string ReadHidden(string prompt)
    {
        context.Error.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                text.Length = Math.Max(0, text.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        context.Error.WriteLine();
        return text.ToString();
    }
}
