// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Records.Identity;

namespace Vericairn.Workbench.Tests;

/// <summary>Seeds test estates through the command line, exactly as a person would.</summary>
internal static class Seed
{
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";

    // Console.Out is process-wide; test classes run in parallel, so every redirection takes this gate.
    private static readonly object ConsoleGate = new();

    public static string Init(string root, string name = "Example Estate")
    {
        string line = Cli("--estate", root, "--json", "estate", "init", "--name", name, "--slug", "example", "--purpose", "Workbench test.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
        return JsonDocument.Parse(line).RootElement.GetProperty("operator").GetString()!;
    }

    public static string Admit(string root, string op, string scratch, string name, string kind, string role)
    {
        string agent = kind == "agent" ? ""","agent":{"vendor":"Example","product":"Example Agent"}""" : string.Empty;
        string path = Write(scratch, $"principal-{Ulid.NewUlid().Value}.json", $$"""{"kind":"{{kind}}","name":"{{name}}","roles":["{{role}}"]{{agent}}}""");
        return Id(Cli("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", path));
    }

    public static string Cli(params string[] args)
    {
        lock (ConsoleGate)
        {
            var output = new StringWriter { NewLine = "\n" };
            TextWriter previous = Console.Out;
            Console.SetOut(output);
            try
            {
                int code = CliApp.Build().Parse(args).Invoke();
                Assert.True(code == 0, $"vericairn {string.Join(' ', args)} exited {code}: {output}");
            }
            finally
            {
                Console.SetOut(previous);
            }

            return output.ToString().Trim().Split('\n').Last();
        }
    }

    public static int Run(params string[] args)
    {
        lock (ConsoleGate)
        {
            TextWriter previous = Console.Out;
            Console.SetOut(new StringWriter());
            try
            {
                return CliApp.Build().Parse(args).Invoke();
            }
            finally
            {
                Console.SetOut(previous);
            }
        }
    }

    public static string Id(string jsonLine) => JsonDocument.Parse(jsonLine).RootElement.GetProperty("id").GetString()!;

    public static string LastId(string root, string type)
    {
        string directory = Directory.EnumerateDirectories(Path.Combine(root, "records", type)).Single();
        return Path.GetFileNameWithoutExtension(Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Last());
    }

    public static string Write(string directory, string name, string json)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    public static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string Temp(string purpose)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vericairn-{purpose}-{Ulid.NewUlid().Value}");
        Directory.CreateDirectory(path);
        return path;
    }
}
