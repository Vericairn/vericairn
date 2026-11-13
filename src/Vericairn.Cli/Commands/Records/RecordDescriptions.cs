// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal static class RecordDescriptions
{
    public static int Show(CommandContext context, string? type, bool json = false)
    {
        if (json)
        {
            return Json(context, type);
        }

        if (type is null)
        {
            context.Output.WriteLine($"Record types this build reads (record formats {string.Join(", ", RecordFormats.Reads)}; new records use the estate's format, at most {RecordFormats.Current}):");
            foreach (IRecordType recordType in RecordTypeRegistry.All)
            {
                context.Output.WriteLine($"  {recordType.Name,-18} {(recordType.SinceFormat > RecordFormats.First ? $"[format {recordType.SinceFormat}] " : string.Empty)}{recordType.Summary}");
            }

            context.Output.WriteLine();
            context.Output.WriteLine("Reserved for later releases: " + string.Join(", ", RecordTypeRegistry.Reserved));
            context.Output.WriteLine("Every record also has the envelope: format, type, id, at, by, body, hash, and optionally subject, supersedes, signatures.");
            return Clean;
        }

        IRecordType? found = RecordTypeRegistry.Find(type);
        if (found is null)
        {
            return context.Fail($"Unknown record type '{type}'.");
        }

        context.Output.WriteLine($"{found.Name}: {found.Summary}");
        context.Output.WriteLine(found.SubjectType is null ? (found.SubjectHashRequired ? "Subject: required, pinned to an exact hash." : "Subject: none.") : $"Subject: {found.SubjectType}{(found.SubjectHashRequired ? ", pinned to an exact hash" : string.Empty)}.");
        if (found.SinceFormat > RecordFormats.First)
        {
            context.Output.WriteLine($"Record format: {found.SinceFormat} and later.");
        }

        context.Output.WriteLine("Body fields:");
        foreach (FieldDescription field in found.Fields)
        {
            string since = field.SinceFormat > RecordFormats.First ? $"[format {field.SinceFormat}] " : string.Empty;
            context.Output.WriteLine($"  {field.Name,-18} {field.Kind,-28} {(field.Required ? "required" : "optional"),-9} {since}{field.Description}");
        }

        if (found.Fields.Any(f => f.Kind == FieldKinds.Timestamp))
        {
            context.Output.WriteLine($"A timestamp is a UTC time in whole seconds, {RecordParser.TimestampForm}, such as 2026-10-13T14:00:00Z; a value with fractional seconds or an offset is refused, never converted.");
        }

        return Clean;
    }

    // The registry the text shows, under stable names: the list of types, or one type with its fields.
    private static int Json(CommandContext context, string? type)
    {
        if (type is null)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new
            {
                reads = RecordFormats.Reads,
                writesAtMost = RecordFormats.Current,
                types = RecordTypeRegistry.All.Select(t => new { name = t.Name, sinceFormat = t.SinceFormat, summary = t.Summary }),
                reserved = RecordTypeRegistry.Reserved,
            }, ProtocolJson.Options));
            return Clean;
        }

        if (RecordTypeRegistry.Find(type) is not IRecordType found)
        {
            return context.Fail($"Unknown record type '{type}'.");
        }

        context.Output.WriteLine(JsonSerializer.Serialize(new
        {
            name = found.Name,
            summary = found.Summary,
            subjectType = found.SubjectType,
            subjectHashRequired = found.SubjectHashRequired,
            sinceFormat = found.SinceFormat,
            fields = found.Fields.Select(f => new { name = f.Name, kind = f.Kind, required = f.Required, sinceFormat = f.SinceFormat, description = f.Description }),
        }, ProtocolJson.Options));
        return Clean;
    }
}
