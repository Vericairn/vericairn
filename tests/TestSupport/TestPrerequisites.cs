// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.TestSupport;

internal static class TestPrerequisites
{
    private static readonly Lazy<bool> GitAvailable = new(() =>
    {
        RequireExecutable("git", "--version");
        return true;
    });

    private static readonly Lazy<bool> CertificatesAvailable = new(() =>
    {
        try
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=test-prerequisite", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using X509Certificate2 loaded = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
            Assert.True(loaded.HasPrivateKey);
            return true;
        }
        catch (CryptographicException)
        {
            Assert.Fail("Missing prerequisite: the operating system X.509 certificate and private-key provider must permit TLS certificate creation and import.");
            return false;
        }
    });

    private static readonly Lazy<bool> LoopbackAvailable = new(() =>
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            Assert.Fail("Missing prerequisite: permission to bind a local loopback TCP listener for the transport tests.");
            return false;
        }
    });

    private static readonly Lazy<bool> LocalIpcAvailable = new(() =>
    {
        if (OperatingSystem.IsWindows()) return true;
        string directory = Path.Combine(Path.GetTempPath(), "vc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(Path.Combine(directory, "probe.sock")));
            socket.Listen(1);
            return true;
        }
        catch (SocketException)
        {
            Assert.Fail("Missing prerequisite: permission to bind a local Unix domain socket for the daemon transport tests.");
            return false;
        }
        finally { Directory.Delete(directory, recursive: true); }
    });

    internal static void RequireLocalIpc() => Assert.True(LocalIpcAvailable.Value);

    internal static void RequireLoopback() => Assert.True(LoopbackAvailable.Value);

    internal static void RequireTlsCertificates() => Assert.True(CertificatesAvailable.Value);

    internal static void RequireGit() => Assert.True(GitAvailable.Value);

    internal static void RequireExecutable(string name, params string[] arguments)
    {
        var start = new ProcessStartInfo(name)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds),
                    "Missing prerequisite: " + name + " did not answer its availability check.");
                Task.WhenAll(output, error).WaitAsync(TestTiming.HangTimeout).GetAwaiter().GetResult();
                Assert.True(process.ExitCode == 0, "Missing prerequisite: " + name + " failed its availability check.");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            Assert.Fail("Missing prerequisite: " + name + " must be installed and executable before this test runs.");
        }
    }
}
