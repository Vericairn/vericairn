// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class PackagesCommand : Command
{
    public PackagesCommand(CommandContext context) : base("packages", "Record nuget.org observations and account-holder intents for a product's package versions.")
    {
        Subcommands.Add(Observe(context));
        Subcommands.Add(Intent(context, deprecate: false));
        Subcommands.Add(Intent(context, deprecate: true));
    }

    private static Command Observe(CommandContext context)
    {
        var command = new Command("observe", "Record publication, listing and deprecation from nuget.org's public API, or observe an original intent's outcome.");
        var product = new Option<string?>("--product") { Description = "Current product id declaring its NuGet packages; use with --version." };
        var version = new Option<string?>("--version") { Description = "Recorded release version, or declared placeholder with --reserved; use with --product." };
        var reserved = new Option<bool>("--reserved") { Description = "Select a version declared reserved in the current product; use with --product and --version, never --operation." };
        var operation = new Option<string?>("--operation") { Description = "Original unlisting or deprecation operation id; use instead of --product and --version." };
        var by = new Option<string>("--by") { Description = "Observing principal id; intent outcomes require the operator.", Required = true };
        command.Options.Add(product); command.Options.Add(version); command.Options.Add(reserved); command.Options.Add(operation); command.Options.Add(by);
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context,
            api.PackagesObserve(new(parse.GetValue(by)!, parse.GetValue(product), parse.GetValue(version), parse.GetValue(operation)) { Reserved = parse.GetValue(reserved) }), parse.GetValue(context.JsonOption))));
        return command;
    }

    private static Command Intent(CommandContext context, bool deprecate)
    {
        var command = new Command(deprecate ? "deprecate" : "unlist", deprecate
            ? "Record the exact deprecation intent, then print the account holder's nuget.org step; never change the registry."
            : "Record unlisting intent, then print the account holder's nuget.org step; never change the registry.");
        var product = new Option<string>("--product") { Description = "Current product id declaring this NuGet package.", Required = true };
        var version = new Option<string>("--version") { Description = "Recorded release version, or declared placeholder with --reserved.", Required = true };
        var reserved = new Option<bool>("--reserved") { Description = "Select a version declared reserved in the current product without a release record." };
        var package = new Option<string>("--package") { Description = "One NuGet package id from the product declaration.", Required = true };
        var by = new Option<string>("--by") { Description = "Estate operator's principal id.", Required = true };
        var reasons = new Option<string[]>("--reason") { Description = "NuGet deprecation reason: Legacy, CriticalBugs or Other. Repeat for multiple reasons.", Required = true, AllowMultipleArgumentsPerToken = true };
        var message = new Option<string?>("--message") { Description = "Exact deprecation message, at most 500 characters; omitted means no message." };
        var alternate = new Option<string?>("--alternate-package") { Description = "Replacement NuGet package id; omitted means no replacement." };
        var range = new Option<string?>("--alternate-range") { Description = "Replacement version range, at most 256 characters; requires --alternate-package." };
        command.Options.Add(product); command.Options.Add(version); command.Options.Add(reserved); command.Options.Add(package); command.Options.Add(by);
        if (deprecate)
        {
            command.Options.Add(reasons); command.Options.Add(message); command.Options.Add(alternate); command.Options.Add(range);
        }
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context,
            deprecate ? api.PackagesDeprecate(new(parse.GetValue(product)!, parse.GetValue(version)!, parse.GetValue(package)!, parse.GetValue(by)!,
                parse.GetValue(reasons) ?? [], parse.GetValue(message), parse.GetValue(alternate), parse.GetValue(range)) { Reserved = parse.GetValue(reserved) })
                : api.PackagesUnlist(new(parse.GetValue(product)!, parse.GetValue(version)!, parse.GetValue(package)!, parse.GetValue(by)!) { Reserved = parse.GetValue(reserved) }),
            parse.GetValue(context.JsonOption))));
        return command;
    }

    private static int Write(CommandContext context, PackagesResponse response, bool json)
    {
        if (json) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        else
        {
            foreach (RecordWritten record in response.Packages.Select(p => p.Observation)
                .Concat(new[] { response.Intent, response.Operation }.OfType<RecordWritten>()).DistinctBy(r => r.Id))
                context.Output.WriteLine($"{record.Path}  {record.Id}  {record.Hash}");
            foreach (PackageObservationResult package in response.Packages)
                context.Output.WriteLine($"{package.State.Package} {package.State.Version}: {package.State.Publication}, {package.State.Listing}, {package.State.Deprecation}");
            context.Output.WriteLine(response.Message);
            if (response.Instruction is not null) context.Output.WriteLine(response.Instruction);
        }
        return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
    }
}
