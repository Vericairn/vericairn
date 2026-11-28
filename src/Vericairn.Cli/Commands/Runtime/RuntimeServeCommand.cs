// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.CommandLine;
using System.Text;
using Vericairn.Daemon;
using Vericairn.Records.Signing;

namespace Vericairn.Cli;

internal sealed class RuntimeServeCommand : Command, ITextOutputCommand
{
    public string WhyNotJson => "it serves the estate until stopped and writes the service's log. runtime start --json starts it in the background and runtime status --json reports on it.";

    public RuntimeServeCommand(CommandContext context)
        : base("serve", "Serve the estate in this process until stopped (Ctrl+C or 'runtime stop'); refuses when a daemon already serves it.")
    {
        var operations = new DaemonLifecycle(context);
        var noLoopbackOption = new Option<bool>("--no-loopback") { Description = "Disable the pinned loopback TLS endpoint. The local socket and any configured local-network listener remain available." };
        var detachedOption = new Option<bool>("--detached") { Description = "Write the daemon's output to its log beside the estate's live store instead of the standard streams; 'runtime start' serves this way." };
        var homeOption = new Option<string>(ServiceInstaller.ServiceHomeOption) { Hidden = true };
        Options.Add(noLoopbackOption);
        Options.Add(detachedOption);
        Options.Add(homeOption);
        SetAction(parseResult =>
        {
            int Serve() => operations.RuntimeServe(parseResult.GetValue(context.EstateOption)!, !parseResult.GetValue(noLoopbackOption), parseResult.GetValue(detachedOption));
            if (parseResult.GetValue(homeOption) is not string encodedHome)
                return Serve();

            string home;
            try
            {
                home = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encodedHome));
            }
            catch (FormatException)
            {
                return context.Fail("The service's Vericairn home is invalid. Reinstall the service with 'vericairn runtime install'.");
            }

            if (!Path.IsPathFullyQualified(home) || home.Any(char.IsControl))
                return context.Fail("The service's Vericairn home must be an absolute path. Reinstall the service with 'vericairn runtime install'.");

            string? previous = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            try
            {
                return Serve();
            }
            finally
            {
                Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previous);
            }
        });
    }
}
