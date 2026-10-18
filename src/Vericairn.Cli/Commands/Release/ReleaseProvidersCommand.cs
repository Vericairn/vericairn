// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ReleaseProvidersCommand : Command
{
    public ReleaseProvidersCommand(CommandContext context)
        : base("providers", "List the provider and adapter implementations available to the runtime, with the settings each declares and its defaults.")
    {
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<ProviderRow> rows = api.Providers();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return Clean;
        }

        if (rows.Count == 0)
        {
            context.Output.WriteLine("No provider or adapter is available to the runtime here.");
            return Clean;
        }

        foreach (ProviderRow row in rows)
        {
            context.Output.WriteLine($"{row.Provider}  {row.Kind}  {row.Implementation}  {row.Contract}  {row.Status}{(row.Locations.Count == 0 ? string.Empty : "  serves " + string.Join(", ", row.Locations))}");
            foreach (ProviderSettingRow setting in row.Settings)
            {
                string marks = (setting.Name == row.Destination ? "destination, " : string.Empty) + (setting.Required ? "required" : setting.Default is null ? "optional" : "default " + setting.Default);
                context.Output.WriteLine($"  {setting.Name,-12} {marks}: {setting.Summary}");
            }

            foreach (string limitation in row.Limitations)
            {
                context.Output.WriteLine($"  limitation: {limitation}");
            }
        }

        return Clean;
    });
}
