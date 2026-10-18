// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>A client stopped observing an operation; this does not establish whether its effect happened.</summary>
public interface IClientOperationFailure
{
    /// <summary>True when the operation may have taken effect. Reconcile before any retry.</summary>
    bool OutcomeUnknown { get; }
}

/// <summary>The caller cancelled its observation of a request.</summary>
public sealed class ClientCancelledException(bool outcomeUnknown, CancellationToken cancellationToken, Exception? innerException = null)
    : OperationCanceledException(ClientFailure.CancelledMessage(outcomeUnknown), innerException, cancellationToken), IClientOperationFailure
{
    /// <inheritdoc />
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

/// <summary>The operation's deadline expired, independently of a person's cancellation.</summary>
public sealed class ClientTimeoutException(bool outcomeUnknown, Exception? innerException = null)
    : TimeoutException(ClientFailure.TimeoutMessage(outcomeUnknown), innerException), IClientOperationFailure
{
    /// <inheritdoc />
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

/// <summary>The transport lost the response; a possible effect remains uncertain.</summary>
public sealed class ClientUnavailableException(bool outcomeUnknown, Exception innerException)
    : HttpRequestException(ClientFailure.UnavailableMessage(outcomeUnknown), innerException), IClientOperationFailure
{
    /// <inheritdoc />
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

/// <summary>The shared CLI, MCP and Workbench interpretation of an interrupted request.</summary>
public sealed record ClientFailure(string Code, string Message, bool OutcomeUnknown, int ExitCode)
{
    private const string Uncertain = " The outcome is unknown; reconcile the original operation before retrying.";

    /// <summary>Maps transport failures without changing the wire shape of successful responses.</summary>
    public static ClientFailure? FromException(Exception exception)
    {
        bool unknown = exception is IClientOperationFailure { OutcomeUnknown: true };
        return exception switch
        {
            TimeoutException => new("timed-out", TimeoutMessage(unknown), unknown, 124),
            OperationCanceledException => new("cancelled", CancelledMessage(unknown), unknown, 130),
            HttpRequestException => new("unavailable", UnavailableMessage(unknown), unknown, 2),
            _ => null,
        };
    }

    internal static string CancelledMessage(bool unknown) => "The request was cancelled." + (unknown ? Uncertain : string.Empty);
    internal static string TimeoutMessage(bool unknown) => "The request timed out." + (unknown ? Uncertain : string.Empty);
    internal static string UnavailableMessage(bool unknown) => "The runtime is unavailable." + (unknown ? Uncertain : string.Empty);
}

/// <summary>
/// Deadlines cover the entire operation, including its response body. Cancelling an observation never means rollback and never
/// triggers a retry. Embedded synchronous implementations run off the caller's thread until they can implement cooperative I/O.
/// </summary>
public static class ClientOperation
{
    /// <summary>Interactive reads have a short deadline; checks, transfers and effect preparation have time to finish.</summary>
    public static TimeSpan Deadline(string operation) => operation switch
    {
        "Check" or "Validate" or "Context" or "BriefContext" or "Handoff" or "ResumeHandoff" or
        "ObjectsVerify" or "ObjectGet" or "ObjectPut" or "Export" or "Upgrade" or "DocumentReview" or
        "QualifyHarness" or "RunStart" or "GoalStep" or "ReleaseSubmit" or "ReleaseObserve" => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromSeconds(30),
    };

    /// <summary>Adapts an embedded operation without running synchronous I/O on the caller's thread.</summary>
    public static Task<T> RunSynchronousAsync<T>(string operation, Func<T> action, bool mayHaveEffect, CancellationToken cancellationToken = default) =>
        RunAsync(operation, token => Task.Run(action, token), mayHaveEffect, cancellationToken: cancellationToken);

    /// <summary>Runs one operation with an explicit deadline and preserves uncertainty if observation is interrupted.</summary>
    public static async Task<T> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> action, bool mayHaveEffect,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (cancellationToken.IsCancellationRequested)
        {
            throw new ClientCancelledException(false, cancellationToken);
        }

        using var deadline = new CancellationTokenSource(timeout ?? Deadline(operation));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task<T>? pending = null;
        try
        {
            pending = action(linked.Token);
            return await pending.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new ClientCancelledException(mayHaveEffect, cancellationToken, exception);
            }

            throw new ClientTimeoutException(mayHaveEffect, exception);
        }
        catch (TimeoutException exception) when (exception is not ClientTimeoutException)
        {
            throw new ClientTimeoutException(mayHaveEffect, exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null && exception is not ClientUnavailableException)
        {
            throw new ClientUnavailableException(mayHaveEffect, exception);
        }
        finally
        {
            if (pending is not null && !pending.IsCompletedSuccessfully)
            {
                // A non-cooperative embedded operation may finish after the caller stops waiting. Observe its exception;
                // do not turn its eventual completion into a retry or a new claim about the effect.
                _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
