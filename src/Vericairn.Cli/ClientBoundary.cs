// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>One invocation's cancellation and shared client outcome boundary, independent of command output formats.</summary>
internal sealed class ClientBoundary(TextWriter error, CancellationToken cancellationToken = default) : IDisposable
{
    private readonly CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private Action? interrupt;

    public CancellationToken Token => source.Token;

    public int Invoke(Func<int> action)
    {
        Console.CancelKeyPress += OnInterrupt;
        try
        {
            Token.ThrowIfCancellationRequested();
            return action();
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null)
        {
            ClientFailure failure = ClientFailure.FromException(exception)!;
            error.WriteLine("error: " + failure.Message + (failure.Code == "timed-out" && Stopped(exception) is { } read ? " " + Describe(read) : string.Empty));
            return failure.ExitCode;
        }
        finally { Console.CancelKeyPress -= OnInterrupt; }
    }

    // The reads that stay light on a large estate, which a person can use while a heavier read stops at its limit (decision
    // 01M7XFRC7KTTKD96NNBRWERHDZ), each with the operation it asks the runtime for.
    private static readonly (string Operation, string Command)[] LighterReads =
        [("OperatorActions", "vericairn operator list"), ("Goals", "vericairn goal"), ("Check", "vericairn estate check")];

    private static StoppedRead? Stopped(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is ClientTimeoutException { Read: { } read }) return read;
        return null;
    }

    /// <summary>
    /// From 0.9.4 (FIX-152): what follows "The request timed out." for a read that stopped at its request limit, such as status, human or
    /// --json: the read, its limit, what the runtime last reported it was judging, and the lighter reads to use meanwhile. A write's
    /// timeout, and a timeout that was not the read's own limit, keep the shared diagnostic alone; nothing is written to standard output.
    /// </summary>
    internal static string Describe(StoppedRead read)
    {
        string judged = read.Judging is { } judging ? " while the runtime was " + judging + "." : "; the runtime did not report what it was judging.";
        string[] lighter = LighterReads.Where(r => r.Operation != read.Operation).Select(r => r.Command).ToArray();
        string reads = lighter.Length == 1 ? lighter[0] : string.Join(", ", lighter[..^1]) + " and " + lighter[^1];
        return $"The {Words(read.Operation)} read stopped at its {Limit(read.Limit)} request limit{judged} Lighter reads to use meanwhile: {reads}.";
    }

    // An operation's name as words: OperatorActions is "operator actions".
    private static string Words(string operation)
    {
        var words = new StringBuilder(operation.Length + 4);
        foreach (char c in operation)
        {
            if (char.IsUpper(c) && words.Length > 0) words.Append(' ');
            words.Append(char.ToLowerInvariant(c));
        }

        return words.ToString();
    }

    private static string Limit(TimeSpan limit) =>
        limit >= TimeSpan.FromMinutes(1) && limit.Ticks % TimeSpan.TicksPerMinute == 0 ? string.Create(CultureInfo.InvariantCulture, $"{(long)limit.TotalMinutes}-minute")
        : limit.Ticks % TimeSpan.TicksPerSecond == 0 ? string.Create(CultureInfo.InvariantCulture, $"{(long)limit.TotalSeconds}-second")
        : string.Create(CultureInfo.InvariantCulture, $"{limit.TotalSeconds:0.###}-second");

    // Foreground worker following and runtime serving already define Ctrl+C as an explicit stop request.
    // Keep that behavior while the requests implementing it still have their own deadlines.
    public IDisposable HandleInterrupt(Action action)
    {
        Action? previous = interrupt;
        interrupt = action;
        return new Registration(() => interrupt = previous);
    }

    private void OnInterrupt(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        if (interrupt is Action handler) handler();
        else source.Cancel();
    }

    public void Dispose() => source.Dispose();

    private sealed class Registration(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
