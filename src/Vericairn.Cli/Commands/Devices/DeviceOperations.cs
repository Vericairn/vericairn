// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
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

    public int DevicesReach(string root, string device, string since, bool json)
    {
        if (!DateTimeOffset.TryParseExact(since, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset start))
            return context.Fail("--since requires an ISO 8601 timestamp with seconds and Z or an explicit offset.");
        if (!IsEstate(root)) return context.Fail(NotAnEstate(root));

        try
        {
            using EstateService service = EstateService.OpenReadOnly(root);
            DeviceReachReport? report = service.DeviceReach(device, start);
            if (report is null) return context.Fail($"Device {device} is not a device of this estate.");
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(report, ProtocolJson.Options));
                return Clean;
            }

            context.Output.WriteLine($"device {report.Device} since {Records.Model.RecordParser.FormatTimestamp(report.Since)}");
            if (report.Records.Count == 0) context.Output.WriteLine("  no signatures in the available records or presence ledger");
            foreach (DeviceSignatureReach record in report.Records)
            {
                string sessions = record.Sessions.Count > 0 ? $"  sessions {string.Join(", ", record.Sessions)}" : string.Empty;
                string line = $"  {record.At}  {record.Type} {record.Id}  {record.Hash}  {record.Source}{sessions}";
                if (record.Source == "presence-ledger") WriteRuntime(context.Output, line);
                else context.Output.WriteLine(line);
            }

            foreach (PresenceSessionEntry session in report.Sessions)
            {
                WriteRuntime(context.Output, $"{session.AnnouncedAt} session {session.Session} {session.Named ?? session.Action}");
                if (session.Terms is { } terms) WriteRuntime(context.Output, $"  announced list {terms}");
            }

            if (report.PresenceProblem is { } presenceProblem) context.Output.WriteLine($"  presence evidence unavailable: {presenceProblem}");
            if (report.JournalProblem is { } journalProblem) context.Output.WriteLine($"  server evidence unavailable: {journalProblem}");
            if (report.Revocations.Count == 0) context.Output.WriteLine("  no revocation records in this copy");
            foreach (DeviceRevocationReach revocation in report.Revocations)
            {
                string current = revocation.Current ? "  current admission revision" : string.Empty;
                context.Output.WriteLine($"  revocation {revocation.Id}  {revocation.Hash}  {revocation.At}  {(revocation.Counts ? "counts" : "does not count")}{current}");
                foreach (RevocationObserver observation in revocation.Observations)
                    context.Output.WriteLine($"    {observation.Kind} {observation.Identity}  {observation.Evidence}  {observation.At}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
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

    /// <summary>
    /// From 0.9.4 (CODE-80): a device owner's presence setting on the machine holding its key (design/presence-sessions.md § 10), kept
    /// owner-only in this user's machine state (presence.json under the Vericairn home), never in a record: per-signature, or per-action with
    /// a limit of 60 to 300 seconds. Without a choice it shows the setting. The estate's charter may ask per-signature, and the stricter wins.
    /// </summary>
    public int DevicesPresence(string root, string deviceId, bool perSignature, bool perAction, int? limit, bool json)
    {
        if (perSignature && perAction) return context.Fail("Choose one of --per-signature and --per-action.");
        if (limit is not null && !perAction) return context.Fail("--limit sets a presence session's limit, so it goes with --per-action.");
        if (limit is < PresenceSessionProtocol.MinLimitSeconds or > PresenceSessionProtocol.MaxLimitSeconds)
            return context.Fail($"A presence session's limit is {PresenceSessionProtocol.MinLimitSeconds} to {PresenceSessionProtocol.MaxLimitSeconds} seconds.");
        DeviceRow? row = null;
        int found = context.WithApi(root, api =>
        {
            row = api.Devices().FirstOrDefault(d => d.Id == deviceId);
            return Clean;
        }, readOnly: true, presence: false);
        if (found != Clean) return found;
        if (row is null) return context.Fail($"Device {deviceId} is not a device of this estate.");
        if (!row.HeldHere) return context.Fail($"Device {deviceId}'s key is not held on this machine; set its presence on the machine that holds it.");

        string? charter;
        try
        {
            charter = Records.Types.EstateType.PresencePolicy(LoadCopy(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }

        if (perSignature || perAction)
        {
            try
            {
                PresenceSettings.Write(deviceId, perSignature, limit);
            }
            catch (UnreadableSettingsException unreadable)
            {
                // A damaged settings file counts as per-signature for every device; writing one device's setting would loosen the rest.
                return context.Fail(unreadable.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return context.Fail($"The presence setting could not be kept on this machine: {exception.Message}");
            }
        }

        PresenceSetting setting = PresenceSettings.Read(deviceId);
        string owner = setting.PerSignature ? "per-signature" : "per-action";
        bool charterStricter = charter is not (null or "per-action");
        string effective = setting.PerSignature || charterStricter ? "per-signature" : "per-action";
        int sessionLimit = setting.LimitSeconds ?? PresenceSessionProtocol.MaxLimitSeconds;
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new
            {
                device = deviceId, presence = owner, limitSeconds = setting.LimitSeconds, charter, effective, required = row.Presence == "required", problem = setting.Problem,
            }, ProtocolJson.Options));
            return Clean;
        }

        context.Output.WriteLine(setting.PerSignature
            ? $"device {deviceId} ({row.Name}): one presence check per signature, kept owner-only on this machine"
            : $"device {deviceId} ({row.Name}): an operator action may ask one presence check for the list it announces, a session of at most {sessionLimit} s, kept owner-only on this machine");
        if (setting.Problem is string problem) context.Output.WriteLine("  " + problem);
        if (charterStricter)
            context.Output.WriteLine(charter == "per-signature"
                ? "  the estate's charter asks one presence check per signature for every device, which wins"
                : "  the estate's charter states a presence policy that is neither per-signature nor per-action, so every signature asks its own check");
        if (row.Presence != "required") context.Output.WriteLine("  this device's signatures ask no presence check, so the setting changes nothing for it");
        context.Output.WriteLine(effective == "per-signature"
            ? "  in effect: one presence check per signature"
            : "  in effect: presence sessions where this build and this macOS version open them ('vericairn doctor' says whether they do)");
        return Clean;
    }

    /// <summary>
    /// From 0.9.4 (CODE-80): the presence sessions this machine's runtime opened or tried to open for the estate, from the owner-only ledger
    /// beside its live store; no daemon is needed.
    /// </summary>
    public int DevicesSessions(string root, string? device, bool json)
    {
        try
        {
            if (!IsEstate(root)) return context.Fail(NotAnEstate(root));
            List<PresenceSessionEntry> sessions = [.. PresenceSessionLedger.Read(PresenceSessionLedger.DirectoryOf(root)).Where(s => device is null || s.Device == device)];
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(sessions, ProtocolJson.Options));
                return Clean;
            }

            if (sessions.Count == 0)
                context.Output.WriteLine(device is null ? "this machine's runtime has opened no presence session for this estate" : $"this machine's runtime has opened no presence session for device {device}");
            foreach (PresenceSessionEntry session in sessions)
            {
                string limit = session.LimitSeconds is int seconds ? $"  limit {seconds} s" : string.Empty;
                string fingerprint = session.Fingerprint is null ? "  no check asked" : $"  fingerprint {session.Fingerprint}";
                string opened = session.OpenedAt is null ? "  not opened" : $"  opened {session.OpenedAt}";
                string ended = session.Ended is null ? "  not ended in the ledger" : $"  ended {session.EndedAt} ({session.Ended})";
                context.Output.WriteLine($"{session.AnnouncedAt}  session {session.Session}  {session.Named ?? session.Action}  device {session.Device}{limit}{fingerprint}{opened}  {session.Signed.Count} signed{ended}");
                if (session.Why is string why) context.Output.WriteLine($"  announced as a renewal, because {why}");
                foreach (PresenceSessionSigned signed in session.Signed)
                    context.Output.WriteLine($"  signed {signed.Type} {signed.Record}  {signed.Hash}");
                foreach (PresenceSessionRefused refused in session.Refused)
                    context.Output.WriteLine($"  refused {refused.Code}{(refused.Hash is null ? string.Empty : $"  {refused.Type} {refused.Hash}")}: {refused.Detail}");
                if (session.EndedWhy is string text && session.Ended is not null and not "closed") context.Output.WriteLine("  " + text);
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
