// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

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
            error.WriteLine("error: " + failure.Message);
            return failure.ExitCode;
        }
        finally { Console.CancelKeyPress -= OnInterrupt; }
    }

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
