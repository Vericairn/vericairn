// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpOrientationCommand : Command
{
    public McpOrientationCommand(CommandContext context)
        : base("orientation", "Write AGENTS.md and the pointer files for the common agent harnesses from the estate's identity, its qualifications of agent harnesses and the tool registry, or check existing copies against a fresh generation.")
    {
        var outOption = new Option<string>("--out") { Description = "Directory to write the files into, a directory of their own such as docs/vericairn-orientation; an estate's root is refused.", Required = true };
        var orientationAsOption = new Option<string>("--as") { Description = "Principal id the files describe as the acting principal, if any." };
        var checkOption = new Option<bool>("--check") { Description = "Write nothing; compare the files in --out with a fresh generation and name every one that is missing or differs." };
        Options.Add(outOption);
        Options.Add(orientationAsOption);
        Options.Add(checkOption);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(outOption)!, parseResult.GetValue(orientationAsOption), parseResult.GetValue(checkOption), parseResult.GetValue(context.JsonOption)));
    }

    /// <summary>
    /// Writes the agent orientation files from the estate's identity, its qualifications of agent harnesses and the tool registry;
    /// with <paramref name="check"/>, compares the copies in the directory with a fresh generation instead and names every file
    /// that is missing or differs. From 0.9.3 (CODE-70) it writes only into the directory it is given and refuses an estate's root,
    /// where the owner's own instruction files are, which Vericairn never writes; the comparison is unchanged.
    /// </summary>
    private static int Execute(CommandContext context, string root, string output, string? actingAs, bool check, bool json)
    {
        // A directory that holds an estate manifest is an estate's root, however the path names it.
        if (!check && IsEstate(output))
        {
            return context.Fail($"{Path.GetFullPath(output)} is an estate root, where the owner's own {string.Join(", ", EstateLayout.OwnerInstructionFiles)} belong and Vericairn never writes. Write the generated orientation into a directory of its own, such as docs/vericairn-orientation, and let the owner's files point to it. Nothing was written.");
        }

        return Write(context, root, output, actingAs, check, json);
    }

    private static int Write(CommandContext context, string root, string output, string? actingAs, bool check, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<(string Name, string Content)> files = Orientation.Generate(api, new ToolRegistry(api, actingAs));
        if (check)
        {
            var findings = new List<FindingDto>();
            foreach ((string name, string content) in files)
            {
                string path = Path.Combine(output, name);
                if (!File.Exists(path))
                {
                    findings.Add(new FindingDto("error", "orientation.missing", name, "Missing; write it with `vericairn mcp orientation --out <directory>`."));
                }
                else if (File.ReadAllText(path) != content)
                {
                    findings.Add(new FindingDto("error", "orientation.stale", name, "Differs from a fresh generation; regenerate it with `vericairn mcp orientation --out <directory>` instead of editing it."));
                }
            }

            return context.ReportFindings(new FindingsResponse(findings.Count == 0 ? $"{output}: current" : $"{output}: out of date", findings.Count, 0, findings), json);
        }

        Directory.CreateDirectory(output);
        var written = new List<string>();
        foreach ((string name, string content) in files)
        {
            File.WriteAllText(Path.Combine(output, name), content);
            written.Add(name);
        }

        context.Output.WriteLine(json ? JsonSerializer.Serialize(new { output, files = written }) : $"{output}: {string.Join(", ", written)}");
        return Clean;
    });
}
