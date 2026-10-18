// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal static class RecordFiles
{
    public static int Format(CommandContext context, string[] files)
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
                    context.Output.WriteLine($"formatted {file}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
            {
                context.Error.WriteLine($"{file}: {exception.Message}");
                result = Failed;
            }
        }

        return result;
    }

    public static int Hash(CommandContext context, string file)
    {
        try
        {
            var findings = new FindingList();
            RecordEnvelope? record = RecordParser.Parse(File.ReadAllBytes(file), file, findings, checkFormatting: false);
            if (record is null)
            {
                return context.ReportFindings(ToFindings(findings, file), false);
            }

            context.Output.WriteLine(record.ComputedHash);
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }
}
