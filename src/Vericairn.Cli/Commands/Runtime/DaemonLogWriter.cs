// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli;

/// <summary>Redacts complete physical lines before they reach the detached daemon's log.</summary>
internal sealed class DaemonLogWriter(TextWriter output, WorkerConfigurationStore store) : TextWriter
{
    private const int MatchOverlap = SecretShapes.ProvisionFragmentLength - 1;
    private readonly SecretShapes.OutputRedactor redactor = new(containsProvision: text => ClaudeOutputSecrets.ContainsFragment(text, store));
    private StringBuilder? pending;
    private bool afterCarriageReturn;
    private bool withholdPartial;

    public override Encoding Encoding => output.Encoding;

    public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));
    public override void Write(string? value) => Write(value.AsSpan());
    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void WriteLine(string? value) => Write(string.Concat(value, NewLine));

    public override void Write(ReadOnlySpan<char> buffer)
    {
        while (!buffer.IsEmpty)
        {
            if (afterCarriageReturn)
            {
                afterCarriageReturn = false;
                if (buffer[0] == '\n')
                {
                    buffer = buffer[1..];
                    continue;
                }
            }

            int end = buffer.IndexOfAny('\r', '\n');
            if (end < 0)
            {
                // A provision may rotate before the line ends. Compare the new text and its overlap while accepting it.
                if (!withholdPartial && (pending?.Length ?? 0) + buffer.Length >= SecretShapes.ProvisionFragmentLength)
                {
                    int overlap = Math.Min(pending?.Length ?? 0, MatchOverlap);
                    string prefix = overlap == 0 ? "" : pending!.ToString(pending.Length - overlap, overlap);
                    withholdPartial = ClaudeOutputSecrets.ContainsFragment(string.Concat(prefix, buffer), store);
                }
                (pending ??= new StringBuilder()).Append(buffer);
                return;
            }

            FinishLine(buffer[..end]);
            afterCarriageReturn = buffer[end] == '\r';
            buffer = buffer[(end + 1)..];
        }
    }

    public override void Flush() => output.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (pending is not null) FinishLine([]);
                foreach (var (line, _) in redactor.Complete()) output.WriteLine(line);
            }
            finally
            {
                output.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private void FinishLine(ReadOnlySpan<char> tail)
    {
        string line = pending is null ? tail.ToString() : pending.Append(tail).ToString();
        pending = null;
        foreach (var (safe, _) in redactor.Push(line, withholdPartial)) output.WriteLine(safe);
        withholdPartial = false;
    }
}
