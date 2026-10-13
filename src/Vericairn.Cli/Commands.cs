// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Cli;

/// <summary>Command implementations. Exit codes: 0 clean, 1 findings with errors, 2 usage or failure.</summary>
internal static class Commands
{
    private const int Clean = 0;
    private const int HasErrors = 1;
    private const int Failed = 2;

    public static int Validate(string root, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            return Report(model.Findings, json, $"{model.Records.Count} records");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Check(string root, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            FindingList checks = EstateChecks.Run(model);
            var all = new FindingList();
            foreach (Finding finding in model.Findings.Concat(checks))
            {
                all.Add(finding);
            }

            return Report(all, json, $"{model.Records.Count} records");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Status(string root, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            FindingList checks = EstateChecks.Run(model);
            Console.Out.Write(json ? EstateStatus.RenderJson(model, checks) : EstateStatus.Render(model, checks));
            return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int New(string root, string type, string by, string bodyPath, string? subject, string? supersedes, string? at, string? id, bool json)
    {
        try
        {
            if (RecordTypeRegistry.Find(type) is null)
            {
                return Fail($"Unknown record type '{type}'. Run 'vericairn records describe'.");
            }

            if (!Ulid.IsValid(by))
            {
                return Fail("--by must be a ULID.");
            }

            RecordReference? subjectReference = null;
            if (subject is not null)
            {
                string[] parts = subject.Split(':', 3);
                if (parts.Length < 2 || !Ulid.IsValid(parts[1]) || (parts.Length == 3 && !ContentHash.IsValid("sha256:" + parts[2].Replace("sha256:", string.Empty, StringComparison.Ordinal))))
                {
                    return Fail("--subject must be type:id or type:id:hash.");
                }

                subjectReference = new RecordReference(parts[0], parts[1], parts.Length == 3 ? "sha256:" + parts[2].Replace("sha256:", string.Empty, StringComparison.Ordinal) : null);
            }

            DateTimeOffset? atTime = ParseAt(at);
            if (at is not null && atTime is null)
            {
                return Fail("--at must look like 2026-10-13T14:00:00Z.");
            }

            if (id is not null && !Ulid.IsValid(id))
            {
                return Fail("--id must be a ULID.");
            }

            using JsonDocument body = ReadBody(bodyPath);
            return WriteValidated(root, new RecordDraft(type, by, body.RootElement, subjectReference, supersedes, atTime, id), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int WorkBrief(string root, string key, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? order = WorkView.FindOrder(model, key);
            if (order is null)
            {
                return Fail($"No current work order has key or id {key}.");
            }

            Console.Out.Write(json ? WorkView.RenderBriefJson(model, order) : WorkView.RenderBrief(model, order));
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int WorkObserve(string root, string key, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? order = WorkView.FindOrder(model, key);
            if (order is null)
            {
                return Fail($"No current work order has key or id {key}.");
            }

            Console.Out.Write(json ? WorkView.RenderBriefJson(model, order) : WorkView.RenderObserve(model, order));
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int WorkAssign(string root, string key, string contributor, string by, string briefPath, string? note, bool json)
    {
        try
        {
            if (!Ulid.IsValid(by) || !Ulid.IsValid(contributor))
            {
                return Fail("--by and --contributor must be ULIDs.");
            }

            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? order = WorkView.FindOrder(model, key);
            if (order is null)
            {
                return Fail($"No current work order has key or id {key}.");
            }

            using JsonDocument brief = ReadBody(briefPath);
            var body = new JsonObject { ["contributor"] = contributor, ["brief"] = JsonNode.Parse(brief.RootElement.GetRawText()) };
            if (note is not null)
            {
                body["note"] = note;
            }

            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteValidated(root, new RecordDraft("assignment", by, document.RootElement, order.Reference), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int WorkAttempt(string root, string assignmentId, string by, string workspace, string note, string? at, bool json)
    {
        try
        {
            if (!Ulid.IsValid(by))
            {
                return Fail("--by must be a ULID.");
            }

            DateTimeOffset? atTime = ParseAt(at);
            if (at is not null && atTime is null)
            {
                return Fail("--at must look like 2026-10-13T14:00:00Z.");
            }

            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? assignment = Latest(model, assignmentId, "assignment");
            if (assignment is null)
            {
                return Fail($"No assignment has id {assignmentId}.");
            }

            var body = new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(atTime ?? DateTimeOffset.UtcNow), ["workspace"] = workspace, ["note"] = note };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteValidated(root, new RecordDraft("attempt", by, document.RootElement, assignment.Reference, At: atTime), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int WorkResult(string root, string attemptId, string by, string bodyPath, bool json) =>
        WorkRecordAbout(root, "result", "attempt", attemptId, by, bodyPath, json);

    public static int WorkAmend(string root, string assignmentId, string by, string bodyPath, bool json) =>
        WorkRecordAbout(root, "amendment", "assignment", assignmentId, by, bodyPath, json);

    public static int Init(string root, string name, string slug, string purpose, string operatorName, string organization, string? github, string repository, string commit, bool json)
    {
        try
        {
            (RecordEnvelope operatorRecord, RecordEnvelope estateRecord) = EstateInitializer.Initialize(root, new EstateInitialization(name, slug, purpose, operatorName, organization, github, repository, commit));
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { @operator = operatorRecord.Id, estate = estateRecord.Id, manifest = EstateLayout.ManifestPath }));
            }
            else
            {
                Console.Out.WriteLine($"operator  {operatorRecord.Id}  {operatorRecord.ExpectedPath}");
                Console.Out.WriteLine($"estate    {estateRecord.Id}  {estateRecord.ExpectedPath}");
                Console.Out.WriteLine($"manifest  {EstateLayout.ManifestPath}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Describe(string? type)
    {
        if (type is null)
        {
            Console.Out.WriteLine("Record types in record format 1:");
            foreach (IRecordType recordType in RecordTypeRegistry.All)
            {
                Console.Out.WriteLine($"  {recordType.Name,-18} {recordType.Summary}");
            }

            Console.Out.WriteLine();
            Console.Out.WriteLine("Reserved for later releases: " + string.Join(", ", RecordTypeRegistry.Reserved));
            Console.Out.WriteLine("Every record also has the envelope: format, type, id, at, by, body, hash, and optionally subject, supersedes, signatures.");
            return Clean;
        }

        IRecordType? found = RecordTypeRegistry.Find(type);
        if (found is null)
        {
            return Fail($"Unknown record type '{type}'.");
        }

        Console.Out.WriteLine($"{found.Name}: {found.Summary}");
        Console.Out.WriteLine(found.SubjectType is null ? (found.SubjectHashRequired ? "Subject: required, pinned to an exact hash." : "Subject: none.") : $"Subject: {found.SubjectType}{(found.SubjectHashRequired ? ", pinned to an exact hash" : string.Empty)}.");
        Console.Out.WriteLine("Body fields:");
        foreach (FieldDescription field in found.Fields)
        {
            Console.Out.WriteLine($"  {field.Name,-18} {field.Kind,-28} {(field.Required ? "required" : "optional"),-9} {field.Description}");
        }

        return Clean;
    }

    public static int Format(string[] files)
    {
        int result = Clean;
        foreach (string file in files)
        {
            try
            {
                byte[] original = File.ReadAllBytes(file);
                byte[] formatted = RecordAuthor.Format(original);
                if (!formatted.AsSpan().SequenceEqual(original))
                {
                    File.WriteAllBytes(file, formatted);
                    Console.Out.WriteLine($"formatted {file}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
            {
                Console.Error.WriteLine($"{file}: {exception.Message}");
                result = Failed;
            }
        }

        return result;
    }

    public static int Hash(string file)
    {
        try
        {
            var findings = new FindingList();
            RecordEnvelope? record = RecordParser.Parse(File.ReadAllBytes(file), file, findings, checkFormatting: false);
            if (record is null)
            {
                return Report(findings, false, file);
            }

            Console.Out.WriteLine(record.ComputedHash);
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Decisions(string root, string? id, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            if (id is null)
            {
                Console.Out.Write(json ? DecisionView.RenderJson(model) : DecisionView.RenderList(model));
                return Clean;
            }

            RecordEnvelope? decision = DecisionView.Find(model, id);
            if (decision is null)
            {
                return Fail($"No decision has id {id}.");
            }

            Console.Out.Write(json ? CanonicalJson.SerializePretty(decision.Body) : DecisionView.Render(model, decision));
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Doctor(string root)
    {
        Console.Out.WriteLine($"vericairn {VersionText()}");
        Console.Out.WriteLine($"runtime   .NET {Environment.Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        Console.Out.WriteLine($"git       {Probe("git", "--version") ?? "not found on PATH"}");
        Console.Out.WriteLine($"dotnet    {Probe("dotnet", "--version") ?? "not found on PATH (needed only to build from source)"}");
        string manifest = Path.Combine(root, EstateLayout.ManifestPath);
        Console.Out.WriteLine(File.Exists(manifest) ? $"estate    {Path.GetFullPath(root)} (manifest present)" : $"estate    {Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath})");
        Console.Out.WriteLine($"custody   {DeviceKeyStores.Default().Kind} (default on this platform); per-user state at {VericairnHome.Path()}");
        return Clean;
    }

    public static int Version()
    {
        Console.Out.WriteLine(VersionText());
        return Clean;
    }

    /// <summary>Creates a record about an existing record (pinned to its exact hash) from a body file.</summary>
    private static int WorkRecordAbout(string root, string type, string subjectType, string subjectId, string by, string bodyPath, bool json)
    {
        try
        {
            if (!Ulid.IsValid(by))
            {
                return Fail("--by must be a ULID.");
            }

            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? subject = Latest(model, subjectId, subjectType);
            if (subject is null)
            {
                return Fail($"No {subjectType} has id {subjectId}.");
            }

            using JsonDocument body = ReadBody(bodyPath);
            return WriteValidated(root, new RecordDraft(type, by, body.RootElement, subject.Reference), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    /// <summary>Authors a record, writes it, validates the estate with it, and removes it again if the estate would be invalid.</summary>
    private static int WriteValidated(string root, RecordDraft draft, bool json, IRecordSigner? signer = null)
    {
        EstateModel current = EstateLoader.Load(root);
        if (signer is null)
        {
            string? refusal = ResolveSigner(current, draft, out signer);
            if (refusal is not null)
            {
                return Fail(refusal);
            }
        }

        // A successor is never older than the record it supersedes, even when clocks or fast scripts put them in the same second.
        if (draft.Supersedes is not null && current.ById.TryGetValue(draft.Supersedes, out RecordEnvelope? predecessor) && (draft.At ?? DateTimeOffset.UtcNow) < predecessor.AtTime)
        {
            draft = draft with { At = predecessor.AtTime };
        }

        AuthoredRecord record = RecordAuthor.Create(draft, signer);
        string written = RecordAuthor.Write(root, record);
        EstateModel model = EstateLoader.Load(root);
        var relevant = new FindingList();
        foreach (Finding finding in model.Findings.Where(f => f.Severity == Severity.Error))
        {
            relevant.Add(finding);
        }

        // Repository checks apply to the new record itself (secrets, absolute paths); problems elsewhere are not this record's fault.
        foreach (Finding finding in EstateChecks.Run(model).Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.RelativePath, StringComparison.Ordinal)))
        {
            relevant.Add(finding);
        }

        if (relevant.HasErrors)
        {
            File.Delete(written);
            Console.Error.WriteLine($"Not written: the estate would be invalid with this record ({relevant.Errors} errors).");
            return Report(relevant, json, "rejected");
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { path = record.RelativePath, id = record.Envelope.Id, hash = record.Envelope.ComputedHash }));
        }
        else
        {
            Console.Out.WriteLine($"{record.RelativePath}  {record.Envelope.Id}  {record.Envelope.ComputedHash}");
        }

        return Clean;
    }

    public static int DevicesCreate(string root, string principal, string name, string? custody, string[] profiles, bool json)
    {
        IDeviceKeyStore? store = null;
        string? id = null;
        try
        {
            if (!Ulid.IsValid(principal))
            {
                return Fail("--principal must be a ULID.");
            }

            store = DeviceKeyStores.Named(custody);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            id = Ulid.NewUlid(now).Value;
            byte[] publicKey = store.Create(id);
            var body = new JsonObject
            {
                ["principal"] = principal,
                ["name"] = name,
                ["platform"] = VericairnHome.Platform(),
                ["custody"] = store.Kind,
                ["algorithm"] = RecordSigning.Algorithm,
                ["publicKey"] = RecordSigning.EncodePublicKey(publicKey),
                ["profiles"] = new JsonArray((profiles.Length == 0 ? ["embedded"] : profiles).Select(p => (JsonNode)p).ToArray()),
            };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            int code = WriteValidated(root, new RecordDraft("device", principal, document.RootElement, At: now, Id: id), json, new StoreSigner(store, id));
            if (code != Clean)
            {
                store.Remove(id);
            }

            return code;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            if (store is not null && id is not null)
            {
                store.Remove(id);
            }

            return Fail(exception.Message);
        }
    }

    public static int DevicesList(string root, bool json)
    {
        try
        {
            EstateModel model = EstateLoader.Load(root);
            IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
            var rows = model.Current("device").OrderBy(d => d.Id, StringComparer.Ordinal).Select(device =>
            {
                RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, device);
                string principal = EstateModel.BodyString(device, "principal") ?? "?";
                return new
                {
                    id = device.Id,
                    principal,
                    principalName = model.CurrentPrincipal(principal) is RecordEnvelope p ? EstateModel.BodyString(p, "name") : principal,
                    name = EstateModel.BodyString(device, "name"),
                    platform = EstateModel.BodyString(device, "platform"),
                    custody = EstateModel.BodyString(device, "custody"),
                    admission = admission is null ? "not admitted" : EstateModel.BodyString(admission, "status"),
                    heldHere = stores.Any(s => s.Has(device.Id)),
                };
            }).ToList();
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(rows));
            }
            else
            {
                foreach (var row in rows)
                {
                    Console.Out.WriteLine($"{row.id}  {row.principalName}  {row.name}  {row.platform}  {row.custody}  {row.admission}{(row.heldHere ? "  key held on this machine" : string.Empty)}");
                }
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int DevicesAdmit(string root, string deviceId, string by, string note, bool json)
    {
        try
        {
            if (!Ulid.IsValid(by))
            {
                return Fail("--by must be a ULID.");
            }

            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? device = Latest(model, deviceId, "device");
            if (device is null)
            {
                return Fail($"No device has id {deviceId}.");
            }

            var body = new JsonObject { ["principal"] = EstateModel.BodyString(device, "principal"), ["status"] = "admitted", ["note"] = note };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteValidated(root, new RecordDraft("admission", by, document.RootElement, device.Reference), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int DevicesRevoke(string root, string deviceId, string by, string reason, bool json)
    {
        try
        {
            if (!Ulid.IsValid(by))
            {
                return Fail("--by must be a ULID.");
            }

            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope? device = Latest(model, deviceId, "device");
            if (device is null)
            {
                return Fail($"No device has id {deviceId}.");
            }

            RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, device);
            if (admission is null || EstateModel.BodyString(admission, "status") != "admitted")
            {
                return Fail($"Device {deviceId} has no current admission to revoke.");
            }

            var body = new JsonObject { ["principal"] = EstateModel.BodyString(device, "principal"), ["status"] = "revoked", ["note"] = reason, ["revokedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteValidated(root, new RecordDraft("admission", by, document.RootElement, device.Reference, admission.Id), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    /// <summary>Chooses the device that signs a record for its author on this machine, or explains why none can.</summary>
    private static string? ResolveSigner(EstateModel model, RecordDraft draft, out IRecordSigner? signer)
    {
        signer = null;
        IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
        List<RecordEnvelope> devices = model.Current("device").Where(d => EstateModel.BodyString(d, "principal") == draft.By).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        foreach (RecordEnvelope device in devices.Where(d => SignatureChecks.IsAdmitted(model, d)))
        {
            IDeviceKeyStore? store = stores.FirstOrDefault(s => s.Has(device.Id));
            if (store is not null)
            {
                signer = new StoreSigner(store, device.Id);
                return null;
            }
        }

        RecordEnvelope? estate = model.CurrentEstate;
        string? rootOperator = estate is null ? null : EstateModel.BodyString(estate, "operator");
        if (draft.Type == "admission" && draft.Subject is not null && draft.By == rootOperator)
        {
            RecordEnvelope? own = devices.FirstOrDefault(d => d.Id == draft.Subject.Id);
            IDeviceKeyStore? store = own is null ? null : stores.FirstOrDefault(s => s.Has(own.Id));
            if (own is not null && store is not null)
            {
                signer = new StoreSigner(store, own.Id);
                return null;
            }
        }

        bool required = estate is not null && estate.Body.TryGetProperty("policies", out JsonElement policies) && policies.TryGetProperty("signatures", out JsonElement policy) && policy.GetString() == "required";
        if (required)
        {
            return $"This clone is read-only for principal {draft.By}: no admitted device key is held on this machine. Create a device with 'vericairn devices create' and have an administrator admit it.";
        }

        return null;
    }

    private static RecordEnvelope? Latest(EstateModel model, string id, string type) =>
        model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Type == type ? model.Latest(record) : null;

    private static DateTimeOffset? ParseAt(string? at) =>
        at is not null && RecordParser.IsTimestamp(at)
            ? DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;

    private static JsonDocument ReadBody(string bodyPath) =>
        CanonicalJson.Parse(bodyPath == "-" ? ReadStandardInput() : File.ReadAllBytes(bodyPath));

    private static string VersionText() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static string? Probe(string tool, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { argument }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output.Split('\n')[0] : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] ReadStandardInput()
    {
        using Stream input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static int Report(FindingList findings, bool json, string summary)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                summary,
                errors = findings.Errors,
                warnings = findings.Warnings,
                findings = findings.Sorted().Select(f => new { severity = f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message }),
            }));
        }
        else
        {
            foreach (Finding finding in findings.Sorted())
            {
                Console.Out.WriteLine(finding.ToString());
            }

            Console.Out.WriteLine($"{summary}: {findings.Errors} errors, {findings.Warnings} warnings");
        }

        return findings.HasErrors ? HasErrors : Clean;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return Failed;
    }
}
