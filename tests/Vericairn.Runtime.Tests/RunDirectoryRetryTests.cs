// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RunDirectoryRetryTests : IDisposable
{
    private readonly string estate = Ulid.NewUlid().Value;
    private readonly string run = Ulid.NewUlid().Value;
    private readonly WorkerClock clock = new();
    private readonly RunDirectory observer;

    public RunDirectoryRetryTests()
    {
        observer = new RunDirectory(estate, run);
        observer.Create();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ATransientReadFailureRetriesWithoutLosingTheStateOrStop(bool request, bool access, bool afterOpen)
    {
        Publish(observer, request ? "request" : "state", "kept");
        int attempts = 0;
        int waits = 0;
        void FailOnce()
        {
            if (++attempts == 1) throw Contention(access);
        }

        var reader = new RunDirectory(estate, run)
        {
            BeforeReadOpen = afterOpen ? null : FailOnce,
            AfterReadOpen = afterOpen ? FailOnce : null,
            RetryClock = clock,
            RetryWait = delay => { waits++; clock.Advance(delay); },
        };

        AssertPublication(reader, request ? "request" : "state", "kept");
        Assert.Equal(2, attempts);
        Assert.Equal(1, waits);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AReadThatExhaustsItsRetryBudgetThrowsInsteadOfReportingMissingState(bool request, bool access)
    {
        Publish(observer, request ? "request" : "state", "kept");
        Exception failure = Contention(access);
        int attempts = 0;
        var reader = new RunDirectory(estate, run)
        {
            BeforeReadOpen = () =>
            {
                Assert.InRange(++attempts, 1, 101); // A broken retry bound must fail rather than hang the test.
                throw failure;
            },
            RetryClock = clock,
            RetryWait = clock.Advance,
        };

        Exception? observed = Record.Exception(() => { if (request) reader.ReadStop(); else reader.Read(); });
        Assert.Same(failure, observed);
        Assert.InRange(attempts, 2, 101);
        Assert.InRange(clock.GetElapsedTime(0), TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
        AssertPublication(observer, request ? "request" : "state", "kept");
    }

    [Theory]
    [InlineData("state", false)]
    [InlineData("state", true)]
    [InlineData("stop", false)]
    [InlineData("stop", true)]
    [InlineData("request", false)]
    [InlineData("request", true)]
    public void AContendedPublicationRetriesTheCompleteStagedFile(string file, bool access)
    {
        if (file != "request") Publish(observer, file, "before");
        int attempts = 0;
        int waits = 0;
        string? staged = null;
        var writer = new RunDirectory(estate, run)
        {
            BeforeMove = () =>
            {
                string temporary = Assert.Single(Directory.EnumerateFiles(observer.Path, "*.tmp"));
                staged ??= temporary;
                Assert.Equal(staged, temporary);
                using JsonDocument complete = JsonDocument.Parse(File.ReadAllText(temporary));
                Assert.Equal(JsonValueKind.Object, complete.RootElement.ValueKind);
                if (file == "request") Assert.Null(observer.ReadStop());
                else AssertPublication(observer, file, "before");
                if (++attempts == 1) throw Contention(access);
            },
            RetryClock = clock,
            RetryWait = delay => { waits++; clock.Advance(delay); },
        };

        Publish(writer, file, "after");
        Assert.Equal(2, attempts);
        Assert.Equal(1, waits);
        AssertPublication(observer, file, "after");
        Assert.Empty(Directory.EnumerateFiles(observer.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("state", false)]
    [InlineData("state", true)]
    [InlineData("stop", false)]
    [InlineData("stop", true)]
    [InlineData("request", false)]
    [InlineData("request", true)]
    public void APublicationThatExhaustsItsRetryBudgetThrowsAndKeepsThePreviousFile(string file, bool access)
    {
        if (file != "request") Publish(observer, file, "before");
        Exception failure = Contention(access);
        int attempts = 0;
        var writer = new RunDirectory(estate, run)
        {
            BeforeMove = () =>
            {
                Assert.InRange(++attempts, 1, 101);
                throw failure;
            },
            RetryClock = clock,
            RetryWait = clock.Advance,
        };

        Assert.Same(failure, Record.Exception(() => Publish(writer, file, "after")));
        Assert.InRange(attempts, 2, 101);
        Assert.InRange(clock.GetElapsedTime(0), TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
        if (file == "request") Assert.Null(observer.ReadStop());
        else AssertPublication(observer, file, "before");
        Assert.Empty(Directory.EnumerateFiles(observer.Path, "*.tmp"));
        Publish(observer, file, "after");
        AssertPublication(observer, file, "after");
    }

    [Fact]
    public void ACompetingStopRequestReadsTheWinnerDespiteTransientContention()
    {
        var winner = Request("winner");
        int reads = 0;
        var writer = new RunDirectory(estate, run)
        {
            BeforeStopRequestSerialization = () => Assert.Equal(winner, observer.RequestStop(winner)),
            BeforeReadOpen = () => { if (++reads == 1) throw Contention(access: false); },
            RetryClock = clock,
            RetryWait = clock.Advance,
        };

        Assert.Equal(winner, writer.RequestStop(Request("loser")));
        Assert.Equal(2, reads);
        Assert.Equal(winner, observer.ReadStop());
        Assert.Empty(Directory.EnumerateFiles(observer.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAbsentFileOrDirectoryIsNotAnAccessFailure(bool missingDirectory)
    {
        if (missingDirectory) Directory.Delete(observer.Path);
        var reader = new RunDirectory(estate, run)
        {
            RetryWait = _ => Assert.Fail("An absent file needs no retry."),
        };

        Assert.Null(reader.Read());
        Assert.Null(reader.ReadStop());
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "{")]
    [InlineData(false, "null")]
    [InlineData(true, "")]
    [InlineData(true, "{")]
    [InlineData(true, "null")]
    public void AnInvalidPublishedSnapshotIsNeverReportedAsAbsent(bool request, string content)
    {
        File.WriteAllText(request ? observer.StopRequestFile : observer.StateFile, content);
        var reader = new RunDirectory(estate, run)
        {
            RetryWait = _ => Assert.Fail("Invalid published JSON is not a sharing failure."),
        };

        Assert.Throws<JsonException>(() => { if (request) reader.ReadStop(); else reader.Read(); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailureAfterOpeningIsNeverMistakenForAnAbsentFile(bool missingDirectory)
    {
        Publish(observer, "state", "kept");
        IOException failure = missingDirectory ? new DirectoryNotFoundException("Read interrupted.") : new FileNotFoundException("Read interrupted.");
        var reader = new RunDirectory(estate, run)
        {
            AfterReadOpen = () => throw failure,
            RetryWait = _ => Assert.Fail("Only sharing and access failures are retried."),
        };

        Assert.Same(failure, Record.Exception(() => reader.Read()));
    }

    [Fact]
    public void AnUnrelatedIoFailureIsNotRetriedOrReportedAsMissing()
    {
        var failure = new IOException("Permanent storage failure.");
        var directory = new RunDirectory(estate, run)
        {
            BeforeReadOpen = () => throw failure,
            BeforeMove = () => throw failure,
            RetryWait = _ => Assert.Fail("Only sharing and access failures are retried."),
        };

        Assert.Same(failure, Record.Exception(() => directory.Read()));
        Assert.Same(failure, Record.Exception(() => directory.ReadStop()));
        Assert.Same(failure, Record.Exception(() => Publish(directory, "state", "failed")));
        Assert.Empty(Directory.EnumerateFiles(observer.Path, "*.tmp"));
    }

    private static Exception Contention(bool access) => access
        ? new UnauthorizedAccessException("Synthetic access contention.")
        : new IOException("Synthetic sharing violation.", unchecked((int)0x80070020));

    private static StopRequest Request(string reason) => new("2026-10-15T12:00:00Z", "worker", reason, 1);

    private void Publish(RunDirectory directory, string file, string reason)
    {
        if (file == "request") Assert.Equal(Request(reason), directory.RequestStop(Request(reason)));
        else if (file == "stop") directory.TransmitStop(Request(reason));
        else directory.Write(new RunObservedState { Run = run, Stop = new RunStop("2026-10-15T12:00:00Z", "worker", reason) });
    }

    private void AssertPublication(RunDirectory directory, string file, string reason)
    {
        if (file == "request") Assert.Equal(Request(reason), directory.ReadStop());
        else if (file == "stop") Assert.Equal(Request(reason), JsonSerializer.Deserialize<StopRequest>(File.ReadAllText(directory.StopFile), ProtocolJson.Options));
        else
        {
            RunObservedState state = Assert.IsType<RunObservedState>(directory.Read());
            Assert.Equal(run, state.Run);
            Assert.Equal(new RunStop("2026-10-15T12:00:00Z", "worker", reason), state.Stop);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(observer.Path)) Directory.Delete(observer.Path, recursive: true);
    }
}
