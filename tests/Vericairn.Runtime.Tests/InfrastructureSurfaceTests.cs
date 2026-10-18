// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class InfrastructureSurfaceTests
{
    [Fact]
    public void TheReleaseFakeBelongsToTestSupportAndTheReplayingAdapterStaysPublic()
    {
        Assembly runtime = typeof(IReleaseProvider).Assembly;
        Assert.Null(runtime.GetType("Vericairn.Runtime.Providers.FakeReleaseProvider"));
        Assert.DoesNotContain(runtime.GetExportedTypes(), type => type.Name == nameof(FakeReleaseProvider));
        Assert.Same(typeof(InfrastructureSurfaceTests).Assembly, typeof(FakeReleaseProvider).Assembly);
        Assert.False(typeof(FakeReleaseProvider).IsVisible);
        Assert.True(typeof(RecordedSourceAdapter).IsVisible);
        Assert.Same(runtime, typeof(RecordedSourceAdapter).Assembly);
    }

    [Fact]
    public void PublicWorkerOptionsCannotRedefineTheLengthOfACapsMinute()
    {
        Assert.Null(typeof(WorkerOptions).GetProperty(nameof(WorkerOptions.Minute), BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(TimeSpan.FromMinutes(1), WorkerOptions.Default.Minute);
        Assert.Equal(TimeSpan.FromMilliseconds(10), (new WorkerOptions { Minute = TimeSpan.FromMilliseconds(10) }).Minute);
    }
}
