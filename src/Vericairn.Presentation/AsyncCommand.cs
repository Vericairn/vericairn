// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Windows.Input;

namespace Vericairn.Presentation;

/// <summary>
/// An awaitable UI command whose application flow owns cancellation and failure presentation. From 0.9.5 (FIX-164) an exception the
/// flow did not handle goes to <paramref name="failed"/>, which shows it, and never out of <see cref="Execute"/>: an exception that
/// leaves an async void handler ends the app, and on a phone it closes it.
/// </summary>
/// <param name="execute">The command's flow.</param>
/// <param name="canExecute">Whether the command is available now.</param>
/// <param name="failed">Shows an exception the flow did not handle; it must not throw.</param>
public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> failed) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute();

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            failed(exception);
        }
    }

    /// <summary>Runs the command when available, allowing tests and other commands to await completion; an unhandled exception reaches the caller.</summary>
    public Task ExecuteAsync() => CanExecute(null) ? execute() : Task.CompletedTask;

    /// <summary>Refreshes the UI's enabled state on its owning thread.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
