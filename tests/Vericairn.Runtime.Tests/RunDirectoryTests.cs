// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunDirectoryTests
{
    [Fact]
    public void AStateReadSharingWithReplacementDoesNotLoseTheStop()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows requires readers to share a replacement's delete access.");
        using var run = new RunEstate();
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, Ulid.NewUlid().Value);
        directory.Create();
        var stop = new RunStop("2026-10-15T12:00:00Z", "worker", "stop") { TransmittedAt = "2026-10-15T12:00:01Z" };
        directory.Write(new RunObservedState { Stop = stop });

        // A rename needs DELETE access. Hold that access without completing the replacement so the
        // reader deterministically overlaps it; File.ReadAllText's FileShare.Read rejects this handle.
        using var replacing = new FileStream(directory.StateFile, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
        Assert.Equal(stop, directory.Read()?.Stop);
    }

    [Fact]
    public void AnOpenStateReaderKeepsAWholeSnapshotWhileTheNextStopObservationIsPublished()
    {
        using var run = new RunEstate();
        string estate = run.Service.Session.Manifest.Estate;
        string id = Ulid.NewUlid().Value;
        var writer = new RunDirectory(estate, id);
        writer.Create();
        var stop = new RunStop("2026-10-15T12:00:00Z", "worker", "stop");
        var transmitted = stop with { TransmittedAt = "2026-10-15T12:00:01Z", Transmission = "stop file" };
        writer.Write(new RunObservedState { Run = id, Stop = stop });
        var reader = new RunDirectory(estate, id)
        {
            AfterReadOpen = () => writer.Write(new RunObservedState { Run = id, Stop = transmitted }),
        };

        Assert.Equal(stop, reader.Read()!.Stop);
        Assert.Equal(transmitted, writer.Read()!.Stop);
        Assert.Empty(Directory.EnumerateFiles(writer.Path, "*.tmp"));
    }

    [Fact]
    public void ConcurrentStopRequestsReturnTheFirstCompletelyPublishedRequest()
    {
        using var run = new RunEstate();
        string estate = run.Service.Session.Manifest.Estate;
        string id = Ulid.NewUlid().Value;
        var observer = new RunDirectory(estate, id);
        var first = new StopRequest("2026-10-15T12:00:00Z", "first", "first writer");
        var second = new StopRequest("2026-10-15T12:01:00Z", "second", "second writer");
        bool exposedUnserializedRequest = false;
        StopRequest? competing = null;
        var writer = new RunDirectory(estate, id)
        {
            BeforeStopRequestSerialization = () =>
            {
                // Pause the first writer after opening its file, before writing any JSON. Another instance
                // represents a different worker process: a process-local lock cannot protect this reader.
                exposedUnserializedRequest = File.Exists(observer.StopRequestFile);
                Assert.Null(observer.ReadStop());
                competing = observer.RequestStop(second);
            },
        };

        StopRequest published = writer.RequestStop(first);
        Assert.False(exposedUnserializedRequest);
        Assert.Equal(second, competing);
        Assert.Equal(second, published);
        Assert.Equal(second, observer.ReadStop());
        Assert.Equal(second, observer.RequestStop(first));
        Assert.Empty(Directory.EnumerateFiles(observer.Path, "*.tmp"));
    }

    [Fact]
    public void AnInterruptedStopWriteLeavesNoRequestAndCanBeRetried()
    {
        using var run = new RunEstate();
        string estate = run.Service.Session.Manifest.Estate;
        string id = Ulid.NewUlid().Value;
        var writer = new RunDirectory(estate, id)
        {
            BeforeStopRequestSerialization = () => throw new IOException("Interrupted before serialization."),
        };
        var request = new StopRequest("2026-10-15T12:00:00Z", "worker", "stop");

        Assert.Throws<IOException>(() => writer.RequestStop(request));
        Assert.False(File.Exists(writer.StopRequestFile));
        Assert.Null(writer.ReadStop());
        Assert.Empty(Directory.EnumerateFiles(writer.Path, "*.tmp"));
        var retry = new RunDirectory(estate, id);
        Assert.Equal(request, retry.RequestStop(request));
        Assert.Equal(request, retry.ReadStop());
    }

    [Fact]
    public void StateAndStopPublicationCanReplaceHiddenFilesWithoutLeavingStagingFiles()
    {
        using var run = new RunEstate();
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, Ulid.NewUlid().Value);
        directory.Create();
        directory.Write(new RunObservedState { Run = "first" });
        directory.TransmitStop(new StopRequest("2026-10-15T12:00:00Z", "worker", "first"));
        if (OperatingSystem.IsWindows())
        {
            foreach (string path in new[] { directory.StateFile, directory.StopFile })
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.System);
            }
        }

        directory.Write(new RunObservedState { Run = "second" });
        var request = new StopRequest("2026-10-15T12:01:00Z", "worker", "second");
        directory.TransmitStop(request);
        Assert.Equal("second", directory.Read()!.Run);
        Assert.Equal(request, JsonSerializer.Deserialize<StopRequest>(File.ReadAllText(directory.StopFile), ProtocolJson.Options));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void StopPublicationReplacesAnExistingEntryWithoutWritingThroughItsLink()
    {
        using var run = new RunEstate();
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, Ulid.NewUlid().Value);
        directory.Create();
        string outside = Path.Combine(run.Scratch, "untouched");
        File.WriteAllText(outside, "untouched");
        try
        {
            File.CreateSymbolicLink(directory.StopFile, outside);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Assert.Skip("Creating Windows symbolic links requires Developer Mode or the symlink privilege.");
        }

        var request = new StopRequest("2026-10-15T12:00:00Z", "worker", "stop");
        directory.TransmitStop(request);
        Assert.Equal("untouched", File.ReadAllText(outside));
        Assert.Null(new FileInfo(directory.StopFile).LinkTarget);
        Assert.Equal(request, JsonSerializer.Deserialize<StopRequest>(File.ReadAllText(directory.StopFile), ProtocolJson.Options));
    }
}
