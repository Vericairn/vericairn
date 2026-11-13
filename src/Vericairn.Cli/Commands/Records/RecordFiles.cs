// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal static class RecordFiles
{
    public static int Format(CommandContext context, string[] files, bool json = false)
    {
        int result = Clean;
        var rewritten = new List<string>();
        var unchanged = new List<string>();
        foreach (string file in files)
        {
            try
            {
                byte[] original = File.ReadAllBytes(file);
                byte[] formatted = RecordAuthor.Format(original);
                if (!formatted.AsSpan().SequenceEqual(original))
                {
                    File.WriteAllBytes(file, formatted);
                    rewritten.Add(file);
                    if (!json) context.Output.WriteLine($"formatted {file}");
                }
                else
                {
                    unchanged.Add(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
            {
                context.Error.WriteLine($"{file}: {exception.Message}");
                result = Failed;
            }
        }

        // The files as they were named; a file that could not be read or formatted is named on standard error alone.
        if (json) context.Output.WriteLine(JsonSerializer.Serialize(new { formatted = rewritten, unchanged }, ProtocolJson.Options));
        return result;
    }

    public static int Hash(CommandContext context, string file, bool json = false)
    {
        try
        {
            var findings = new FindingList();
            RecordEnvelope? record = RecordParser.Parse(File.ReadAllBytes(file), file, findings, checkFormatting: false);
            if (record is null)
            {
                return context.ReportFindings(ToFindings(findings, file), json);
            }

            context.Output.WriteLine(json ? JsonSerializer.Serialize(new { hash = record.ComputedHash }, ProtocolJson.Options) : record.ComputedHash);
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }
}
