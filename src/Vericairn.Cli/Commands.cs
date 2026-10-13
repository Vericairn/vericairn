// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
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

            DateTimeOffset? atTime = null;
            if (at is not null)
            {
                if (!RecordParser.IsTimestamp(at))
                {
                    return Fail("--at must look like 2026-10-13T14:00:00Z.");
                }

                atTime = DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            }

            if (id is not null && !Ulid.IsValid(id))
            {
                return Fail("--id must be a ULID.");
            }

            byte[] bodyBytes = bodyPath == "-" ? ReadStandardInput() : File.ReadAllBytes(bodyPath);
            using JsonDocument body = CanonicalJson.Parse(bodyBytes);
            AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, body.RootElement, subjectReference, supersedes, atTime, id));
            string written = RecordAuthor.Write(root, record);
            EstateModel model = EstateLoader.Load(root);
            FindingList relevant = new();
            foreach (Finding finding in model.Findings.Where(f => f.Severity == Severity.Error))
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException or InvalidOperationException or ArgumentException)
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

    public static int Doctor(string root)
    {
        Console.Out.WriteLine($"vericairn {VersionText()}");
        Console.Out.WriteLine($"runtime   .NET {Environment.Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        Console.Out.WriteLine($"git       {Probe("git", "--version") ?? "not found on PATH"}");
        Console.Out.WriteLine($"dotnet    {Probe("dotnet", "--version") ?? "not found on PATH (needed only to build from source)"}");
        string manifest = Path.Combine(root, EstateLayout.ManifestPath);
        Console.Out.WriteLine(File.Exists(manifest) ? $"estate    {Path.GetFullPath(root)} (manifest present)" : $"estate    {Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath})");
        return Clean;
    }

    public static int Version()
    {
        Console.Out.WriteLine(VersionText());
        return Clean;
    }

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
