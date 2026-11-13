// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.3 (FIX-124): shows on standard error, while a command that may write runs, the presence notices of the runtime that serves
/// it, embedded or the daemon: the records an operator action will sign with its owner's presence, announced before the first prompt,
/// "Signature k of n" with what each prompt signs, any it adds, and how many it signed. A command that signs nothing with presence
/// shows nothing, and a runtime that keeps no notices shows nothing either; standard output keeps the command's own answer.
/// </summary>
internal sealed class PresenceDisplay : IDisposable
{
    private const int WaitSeconds = 20;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);
    private readonly IEstateApi api;
    private readonly TextWriter output;
    private readonly CancellationTokenSource stopping = new();
    private readonly object writing = new();
    private readonly Task? following;
    private long cursor;
    private bool stopped;

    private PresenceDisplay(IEstateApi api, TextWriter output, long? start)
    {
        this.api = api;
        this.output = output;
        if (start is long position)
        {
            cursor = position;
            following = Task.Run(FollowAsync);
        }
    }

    /// <summary>
    /// Takes the runtime's position before the command acts, so only the notices of what it does are shown, and follows from there.
    /// A runtime that cannot answer is not followed.
    /// </summary>
    internal static PresenceDisplay Start(IEstateApi api, TextWriter output)
    {
        long? start;
        try
        {
            PresencePromptsResponse head = api.PresencePrompts(new PresencePromptsRequest(null, 0));
            start = head.Problem is null ? head.Cursor : null;
        }
        catch (Exception)
        {
            // A runtime that predates presence notices, or a stand-in that serves none: the command runs as before.
            start = null;
        }

        return new PresenceDisplay(api, output, start);
    }

    /// <summary>Stops following once the command has its answer, after showing every notice raised until then.</summary>
    public void Dispose()
    {
        if (stopped) return;
        stopped = true;
        if (following is not null)
        {
            stopping.Cancel();
            try
            {
                following.Wait(StopWait);
            }
            catch (AggregateException)
            {
                // Following ends with its cancellation; what it showed stays shown.
            }

            // The notices raised as the command finished, such as how many it signed.
            try
            {
                Show(api.PresencePrompts(new PresencePromptsRequest(Volatile.Read(ref cursor), 0)));
            }
            catch (Exception)
            {
                // The command's own answer stands whether or not the last notices could be read.
            }
        }

        // A read still waiting past the stop keeps its token until it returns; the process ends it otherwise.
        if (following is null || following.IsCompleted) stopping.Dispose();
    }

    private async Task FollowAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            PresencePromptsResponse response;
            try
            {
                response = await api.PresencePromptsAsync(new PresencePromptsRequest(Volatile.Read(ref cursor), WaitSeconds), stopping.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            if (response.Problem is not null) return;
            Show(response);
            // A runtime answers an empty read only after its wait; one that answers at once is not asked again at once.
            if (response.Notices.Count == 0)
            {
                try { await Task.Delay(Pause, stopping.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private void Show(PresencePromptsResponse response)
    {
        lock (writing)
        {
            foreach (PresenceNotice notice in response.Notices.Where(n => n.Sequence > cursor))
            {
                output.WriteLine(notice.Text);
                Volatile.Write(ref cursor, notice.Sequence);
            }

            output.Flush();
        }
    }
}
