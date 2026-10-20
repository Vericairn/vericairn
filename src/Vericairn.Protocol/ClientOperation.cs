// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Security.Authentication;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A runtime whose resources must stay alive until non-cooperative embedded operations actually finish.</summary>
public interface IClientOperationLifetime
{
    /// <summary>Retains the runtime for one operation, including work that outlives client cancellation.</summary>
    IDisposable RetainClientOperation();
}

/// <summary>A client operation failed; its cause alone does not establish whether an effect happened.</summary>
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

/// <summary>A definite refusal, before dispatch or in the runtime's answer; no effect is uncertain.</summary>
public sealed class ClientRefusedException(string message, Exception? innerException = null, HttpStatusCode? statusCode = null)
    : HttpRequestException(message, innerException, statusCode), IClientOperationFailure
{
    /// <inheritdoc />
    public bool OutcomeUnknown => false;
}

/// <summary>The shared CLI, MCP and Workbench interpretation of an interrupted request.</summary>
public sealed record ClientFailure(string Code, string Message, bool OutcomeUnknown, int ExitCode)
{
    private const string Uncertain = " The outcome is unknown; reconcile the original operation before retrying.";

    /// <summary>Maps transport failures without changing the wire shape of successful responses.</summary>
    public static ClientFailure? FromException(Exception exception)
    {
        if (Refusal(exception) is Exception refusal) return new("refused", refusal.Message, false, 2);
        bool unknown = exception is IClientOperationFailure { OutcomeUnknown: true };
        if (exception is not IClientOperationFailure && TimedOut(exception)) return new("timed-out", TimeoutMessage(unknown), unknown, 124);
        return exception switch
        {
            TimeoutException => new("timed-out", TimeoutMessage(unknown), unknown, 124),
            OperationCanceledException => new("cancelled", CancelledMessage(unknown), unknown, 130),
            HttpRequestException => new("unavailable", UnavailableMessage(unknown), unknown, 2),
            _ => null,
        };
    }

    internal static Exception? Refusal(Exception exception)
    {
        // An explicit interruption already carries its outcome. Its cause is retained only for diagnosis.
        if (exception is IClientOperationFailure && exception is not ClientRefusedException) return null;
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is ClientRefusedException or AuthenticationException) return cause;
            if (cause is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }) return cause;
        }

        return exception is HttpRequestException { StatusCode: not null } ? exception : null;
    }

    internal static bool TimedOut(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is TimeoutException) return true;
        return false;
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
        "ObjectsVerify" or "ObjectGet" or "ObjectPut" or "Export" or "Upgrade" or "DocumentReview" or "DocumentPin" or
        "Policies" or "Policy" or "PolicyEvaluate" or "WritingExceptions" or "WritingException" or
        "PolicyDeclare" or "PolicyRevise" or "PolicyRetire" or "PolicyExcept" or "PolicyRevokeException" or "WritingExceptionAdd" or "WritingExceptionRemove" or "WorkPlace" or "Result" or "ReviewResult" or "ReleaseBasis" or
        "QualifyHarness" or "RunStart" or "GoalStep" or "ReleaseSubmit" or "ReleaseObserve" or "OperatorRecord" => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromSeconds(30),
    };

    /// <summary>Adapts an embedded operation without running synchronous I/O on the caller's thread.</summary>
    public static Task<T> RunSynchronousAsync<T>(string operation, Func<T> action, bool mayHaveEffect, CancellationToken cancellationToken = default) =>
        RunAsync(operation, token => Task.Run(action, token), mayHaveEffect, cancellationToken: cancellationToken);

    /// <summary>Adapts an owned runtime while retaining its resources until the synchronous operation finishes.</summary>
    public static Task<T> RunSynchronousAsync<T>(string operation, Func<T> action, bool mayHaveEffect, IClientOperationLifetime? lifetime, CancellationToken cancellationToken = default) =>
        RunAsync(operation, async token =>
        {
            using IDisposable? retained = lifetime?.RetainClientOperation();
            // Cleanup follows actual task completion, including cancellation before the queued work starts.
            return await Task.Run(action, token).ConfigureAwait(false);
        }, mayHaveEffect, cancellationToken: cancellationToken);

    /// <summary>Runs one operation with an explicit deadline and preserves uncertainty if observation is interrupted.</summary>
    public static Task<T> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> action, bool mayHaveEffect,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        RunAsync(operation, action, () => mayHaveEffect, timeout, cancellationToken);

    /// <summary>Runs a staged operation whose possible effect begins only after its read-only preparation.</summary>
    public static Task<T> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> action, Func<bool> mayHaveEffect,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        RunAsync(operation, action, mayHaveEffect, timeout, TimeProvider.System, cancellationToken);

    internal static async Task<T> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> action, Func<bool> mayHaveEffect,
        TimeSpan? timeout, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(mayHaveEffect);
        if (cancellationToken.IsCancellationRequested)
        {
            throw new ClientCancelledException(false, cancellationToken);
        }

        TimeSpan budget = timeout ?? Deadline(operation);
        long started = clock.GetTimestamp();
        using var deadline = new CancellationTokenSource(budget, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        bool DeadlineElapsed() => budget != Timeout.InfiniteTimeSpan && clock.GetElapsedTime(started) >= budget;
        Task<T>? pending = null;
        try
        {
            pending = action(linked.Token);
            T result = await pending.WaitAsync(linked.Token).ConfigureAwait(false);
            // A completed task can beat an overdue timer callback (or bypass WaitAsync's cancelled token).
            // Decide at observation using monotonic elapsed time, preserving caller cancellation's precedence.
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested || DeadlineElapsed()) throw new ClientTimeoutException(mayHaveEffect());
            return result;
        }
        catch (OperationCanceledException exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new ClientCancelledException(mayHaveEffect(), cancellationToken, exception);
            }

            if (deadline.IsCancellationRequested || DeadlineElapsed() || (exception is not IClientOperationFailure && ClientFailure.TimedOut(exception)))
                throw new ClientTimeoutException(mayHaveEffect(), exception);
            if (exception is ClientCancelledException) throw;
            // An unrelated transport cancellation is neither proof of a deadline nor a person's cancellation.
            throw new ClientUnavailableException(mayHaveEffect(), exception);
        }
        catch (TimeoutException exception) when (exception is not ClientTimeoutException)
        {
            throw new ClientTimeoutException(mayHaveEffect(), exception);
        }
        catch (HttpRequestException exception) when (exception is not IClientOperationFailure &&
            exception.StatusCode is null && ClientFailure.Refusal(exception) is Exception refusal)
        {
            throw new ClientRefusedException(refusal.Message, exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null && exception is not IClientOperationFailure)
        {
            bool dispatched = exception.HttpRequestError is not (HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError);
            if (ClientFailure.TimedOut(exception)) throw new ClientTimeoutException(mayHaveEffect() && dispatched, exception);
            throw new ClientUnavailableException(mayHaveEffect() && dispatched, exception);
        }
        catch (HttpRequestException exception) when (exception is not IClientOperationFailure && mayHaveEffect() && exception.StatusCode >= HttpStatusCode.InternalServerError)
        {
            throw new ClientUnavailableException(true, exception);
        }
        catch (Exception exception) when (mayHaveEffect() && exception is (IOException or JsonException))
        {
            throw new ClientUnavailableException(true, exception);
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
