// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vericairn.Daemon;

/// <summary>Forwards only TLS authentication exceptions Kestrel consumes internally, without its state, scopes or exception stack.</summary>
internal sealed class TlsHandshakeLog(Action<string> log) : ILoggerProvider
{
    internal const string Category = "Microsoft.AspNetCore.Server.Kestrel.Https.Internal.HttpsConnectionMiddleware";

    public ILogger CreateLogger(string categoryName) =>
        string.Equals(categoryName, Category, StringComparison.Ordinal) ? new HandshakeLogger(log) : NullLogger.Instance;

    internal static string Message(Exception exception) => $"TLS handshake failed: {exception.GetType().FullName}: {exception.Message}";

    public void Dispose()
    {
        // The callback belongs to the daemon's caller.
    }

    private sealed class HandshakeLogger(Action<string> log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // AuthenticationFailed is event 1 in Kestrel's HTTPS middleware. Never render the formatter or state: other
            // events in this category include certificate and connection details. Nor do we render inner exceptions.
            if (eventId.Id == 1 && eventId.Name == "AuthenticationFailed" && exception is not null)
            {
                log(Message(exception));
            }
        }
    }
}
