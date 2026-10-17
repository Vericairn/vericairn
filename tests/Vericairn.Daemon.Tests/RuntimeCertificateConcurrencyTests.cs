// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Vericairn.Records.Tests;

namespace Vericairn.Daemon.Tests;

// In the daemon collection, so its key generation and reader threads never run beside the timing-sensitive daemon tests.
[Collection("daemon")]
public class RuntimeCertificateConcurrencyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentFirstServerAuthenticationChecksSucceed(bool replace)
    {
        // Call the installed Kestrel's actual check, including its treatment of an absent EKU extension.
        Type loader = typeof(KestrelServerOptions).Assembly.GetType("Microsoft.AspNetCore.Server.Kestrel.Https.CertificateLoader", throwOnError: true)!;
        var allowed = loader.GetMethod("IsCertificateAllowedForServerAuth", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<X509Certificate2, bool>>();
        const int readers = 8;
        // Unfixed macOS calibration: 57/256 replacement rounds and 73/256 reload rounds failed with eight readers.
        // The lower end of the lower rate's 95% Wilson interval is 0.176. Assuming independent rounds,
        // 36 rounds give detection probability 1 - (1 - 0.176)^36 > 0.999, without repeating expensive key loads hundreds of times.
        const int rounds = 36;
        using var estate = new TestEstate();
        string directory = Path.Combine(estate.Root, "certificates");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        long startAt = 0;
        using var barrier = new Barrier(readers + 1, phase =>
        {
            if (phase.CurrentPhaseNumber % 2 == 0)
            {
                // Let the dedicated readers wake before a shared start time; barrier wake-up order alone hides the race.
                startAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
            }
        });
        var failures = new ConcurrentQueue<string>();
        var failedReads = new int[rounds];
        X509Certificate2? current = null;
        Task[] checks = Enumerable.Range(0, readers).Select(_ => Task.Factory.StartNew(() =>
        {
            try
            {
                for (int round = 0; round < rounds; round++)
                {
                    barrier.SignalAndWait(deadline.Token);
                    while (Stopwatch.GetTimestamp() < startAt)
                    {
                        Thread.SpinWait(1);
                    }

                    try
                    {
                        if (!allowed(current!))
                        {
                            Interlocked.Increment(ref failedReads[round]);
                            failures.Enqueue($"Kestrel rejected Server Authentication in round {round}.");
                        }
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref failedReads[round]);
                        failures.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                    }

                    barrier.SignalAndWait(deadline.Token);
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                // Release every worker if loading or a barrier fails.
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        try
        {
            for (int round = 0; round < rounds; round++)
            {
                using X509Certificate2 certificate = replace ? RuntimeCertificate.Replace(directory) : RuntimeCertificate.Load(directory);
                // Materialize the collection, but do not read any usage here. All readers share the same still-cold EKU
                // without a fix, as connections do once one has materialized the certificate's extension collection.
                Assert.Single(certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>());
                current = certificate;
                barrier.SignalAndWait(deadline.Token);
                barrier.SignalAndWait(deadline.Token);
            }
        }
        finally
        {
            await deadline.CancelAsync();
            await Task.WhenAll(checks);
        }

        int failedRounds = failedReads.Count(count => count > 0);
        string measurement = $"{failedRounds}/{rounds} failed rounds; {failures.Count}/{rounds * readers} failed first reads ({readers} readers).";
        output.WriteLine(measurement);
        Assert.True(failures.IsEmpty, $"{measurement} {string.Join("; ", failures.Take(5))}");
    }
}
