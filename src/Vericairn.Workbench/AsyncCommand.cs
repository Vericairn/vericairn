// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Windows.Input;

namespace Vericairn.Workbench;

/// <summary>An awaitable UI command whose application flow owns cancellation and failure presentation.</summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute();

    /// <inheritdoc />
    public async void Execute(object? parameter) => await ExecuteAsync().ConfigureAwait(true);

    /// <summary>Runs the command when available, allowing tests and other commands to await completion.</summary>
    public Task ExecuteAsync() => CanExecute(null) ? execute() : Task.CompletedTask;

    /// <summary>Refreshes the UI's enabled state on its owning thread.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
