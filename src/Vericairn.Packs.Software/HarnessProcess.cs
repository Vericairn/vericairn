// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Providers;

namespace Vericairn.Packs.Software;

/// <summary>The command a launcher runs, and the short commands it runs to verify a harness, with nothing but the environment given.</summary>
internal static class HarnessProcess
{
    /// <summary>
    /// The command setting: one executable, or a JSON array of the executable and leading arguments; the harness's own executable when
    /// absent. Throws <see cref="ProviderRefusedException"/> when the setting is malformed, or absent where nothing stands in for it.
    /// </summary>
    public static List<string> Command(IReadOnlyDictionary<string, string> settings, string? fallback)
    {
        if (!settings.TryGetValue("command", out string? command) || string.IsNullOrWhiteSpace(command))
        {
            return fallback is null ? throw new ProviderRefusedException("The command setting names the harness: its executable, or a JSON array of it and its leading arguments.") : [fallback];
        }

        return Strings(command.Trim(), "command") ?? [command.Trim()];
    }

    /// <summary>A JSON array of strings in a setting; null when the setting is not one, and refused when it is a malformed or empty one.</summary>
    public static List<string>? Strings(string value, string setting)
    {
        if (!value.StartsWith('['))
        {
            return null;
        }

        try
        {
            List<string>? items = JsonSerializer.Deserialize<List<string>>(value);
            if (items is { Count: > 0 } && items.All(i => !string.IsNullOrEmpty(i)))
            {
                return items;
            }
        }
        catch (JsonException)
        {
            // Refused below.
        }

        throw new ProviderRefusedException($"The {setting} setting is a JSON array of one or more non-empty strings.");
    }

    /// <summary>The problem with a launcher's command setting, or null.</summary>
    public static string? Problem(IReadOnlyDictionary<string, string> settings, string? fallback)
    {
        try
        {
            Command(settings, fallback);
            return null;
        }
        catch (ProviderRefusedException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>Runs a short command with only the environment given and waits for it; throws <see cref="ProviderRefusedException"/> when it cannot start or does not answer in time.</summary>
    public static (int Code, string Output, string Error) Run(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        foreach ((string name, string value) in environment)
        {
            info.Environment[name] = value;
        }

        try
        {
            using Process process = Process.Start(info) ?? throw new ProviderRefusedException($"{command[0]} did not start.");
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new ProviderRefusedException($"{string.Join(' ', command.Skip(1))} did not answer within {timeout.TotalSeconds} seconds.");
            }

            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Win32Exception exception)
        {
            throw new ProviderRefusedException($"{command[0]} could not be started here: {exception.Message}", exception);
        }
    }
}
