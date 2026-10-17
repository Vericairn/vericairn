// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Vericairn.Records.Tests;

namespace Vericairn.Daemon.Tests;

public class RuntimeCertificateConcurrencyTests
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
        const int readers = 16;
        const int rounds = 256;
        using var estate = new TestEstate();
        string directory = Path.Combine(estate.Root, "certificates");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var barrier = new Barrier(readers + 1);
        var failures = new ConcurrentQueue<string>();
        X509Certificate2? current = null;
        Task[] checks = Enumerable.Range(0, readers).Select(_ => Task.Factory.StartNew(() =>
        {
            try
            {
                for (int round = 0; round < rounds; round++)
                {
                    barrier.SignalAndWait(deadline.Token);
                    try
                    {
                        if (!allowed(current!))
                        {
                            failures.Enqueue($"Kestrel rejected Server Authentication in round {round}.");
                        }
                    }
                    catch (Exception exception)
                    {
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

        Assert.True(failures.IsEmpty, $"{failures.Count} failed first reads: {string.Join("; ", failures.Take(5))}");
    }
}
