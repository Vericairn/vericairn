// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Vericairn.Adapters.Git;

/// <summary>
/// One isolated <c>git cat-file --batch</c> over a registered clone, so the blobs of an exact commit are read through one
/// process instead of one process each. It starts with the adapter's exact arguments and environment. Each request is one
/// exact object id, answered in full before the next is sent, so a response always belongs to the object that was asked
/// for. An absent, mistyped, oversized, late or malformed response ends the reader; the caller then treats its whole read
/// as unavailable, as a failed single read was.
/// </summary>
internal sealed class GitBlobBatch : IDisposable
{
    /// <summary>The largest blob read, the same bound as a single bounded git read.</summary>
    internal const long MaximumBlobBytes = 64L * 1024 * 1024;

    // A single read was stopped after this long; each object now has the same allowance.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(30);
    private readonly Process process;
    private readonly StreamWriter input;
    private readonly Stream requests;
    private readonly BufferedStream responses;
    private readonly Task errors;
    private bool ended;

    private GitBlobBatch(Process process)
    {
        this.process = process;
        input = process.StandardInput;
        requests = input.BaseStream;
        responses = new BufferedStream(process.StandardOutput.BaseStream, 1 << 16);
        // Diagnostics are drained so the process can never block on them; their content is never used.
        errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    }

    /// <summary>Starts the reader in the clone, or returns null when git cannot start there.</summary>
    internal static GitBlobBatch? Start(string clone)
    {
        ProcessStartInfo info = GitFileSource.StartInfo(clone, isolated: true, ["cat-file", "--batch"]);
        info.RedirectStandardInput = true;
        try
        {
            return new(GitFileSource.Start(info));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>The exact bytes of one blob, or null when it cannot be read as a bounded blob; after null the reader is ended.</summary>
    internal byte[]? Read(string id)
    {
        if (ended || !IsObjectId(id)) return End();
        using var deadline = new CancellationTokenSource(Allowance);
        using CancellationTokenRegistration stop = deadline.Token.Register(Kill);
        try
        {
            requests.Write(Encoding.ASCII.GetBytes(id + "\n"));
            requests.Flush();
            string[] header = (Header() ?? "").Split(' ');
            if (header.Length != 3 || header[0] != id || header[1] != "blob" ||
                !long.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out long size) || size > MaximumBlobBytes) return End();
            byte[] bytes = new byte[size];
            responses.ReadExactly(bytes);
            return responses.ReadByte() == '\n' && !deadline.IsCancellationRequested ? bytes : End();
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return End();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            // Closing the requests ends the batch; git then exits on its own.
            input.Dispose();
            if (!process.WaitForExit(5_000)) Kill();
            else errors.Wait(5_000);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or AggregateException)
        {
            Kill();
        }
        finally
        {
            responses.Dispose();
            process.Dispose();
        }
    }

    // Only a full lowercase object id is sent: never a revision expression, path or line break.
    internal static bool IsObjectId(string id) => id.Length is 40 or 64 && id.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    private string? Header()
    {
        var line = new StringBuilder();
        for (int next = responses.ReadByte(); next != '\n'; next = responses.ReadByte())
        {
            if (next < 0 || line.Length >= 256) return null;
            line.Append((char)next);
        }

        return line.ToString();
    }

    private byte[]? End()
    {
        ended = true;
        Kill();
        return null;
    }

    private void Kill()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already exited or never started; nothing remains to stop.
        }
    }
}
