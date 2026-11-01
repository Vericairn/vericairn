// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class SupportBundleCommand : Command
{
    private readonly Option<string?> outOption = new("--out")
    {
        Description = "Also write the bundle, byte for byte as shown, to this new file; an existing file is never replaced.",
    };

    public SupportBundleCommand(CommandContext context)
        : base("bundle", "A diagnostic bundle: version and build, platform, the estate check's codes, status counts, recent runtime errors and the relevant configuration, with secrets, tokens, machine paths and record bodies left out or redacted. Shows everything it holds; sends nothing.")
    {
        Options.Add(outOption);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(outOption)));
    }

    private static int Execute(CommandContext context, string root, string? output)
    {
        if (output is not null && File.Exists(output))
        {
            return context.Fail($"{Path.GetFileName(output)} exists; the bundle never replaces a file. Name a new one.");
        }

        string text = SupportBundle.Render(SupportBundle.Gather(context, root));
        context.Output.Write(text);
        if (output is not null)
        {
            File.WriteAllText(output, text);
            context.Error.WriteLine($"Wrote the bundle shown above to {Path.GetFileName(output)}. Nothing was sent: sharing it is your own act.");
        }

        return Clean;
    }
}
