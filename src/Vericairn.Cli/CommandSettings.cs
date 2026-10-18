// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli;

internal static class CommandSettings
{
    public static Dictionary<string, string>? Parse(string[] settings, out string? problem)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string setting in settings)
        {
            int equals = setting.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                problem = $"A setting is name=value; '{setting}' is not.";
                return null;
            }

            parsed[setting[..equals].Trim()] = setting[(equals + 1)..].Trim();
        }

        problem = null;
        return parsed;
    }
}
