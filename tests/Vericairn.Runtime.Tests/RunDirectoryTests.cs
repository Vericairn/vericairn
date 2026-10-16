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
