// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal static class RecordDescriptions
{
    public static int Show(CommandContext context, string? type)
    {
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

        return Clean;
    }
}
