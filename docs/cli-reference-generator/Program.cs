// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

// Enrich a newly generated, unfrozen CLI reference without changing its command descriptions or help.
// Usage: dotnet run --project docs/cli-reference-generator -- <output-directory> <declaration> <outcomes-reference>
if (args.Length != 3) throw new ArgumentException("Name the output directory, document declaration and client outcomes reference.");
foreach (string path in new[] { args[0], args[2] })
{
    string normalized = Path.GetFullPath(path).Replace('\\', '/');
    if (normalized.Contains("/docs/releases/", StringComparison.Ordinal))
        throw new ArgumentException("Generate outside the frozen release documentation sets.");
}

// Build tooling uses an isolated, disposable home just as the test fixtures do. It never inspects the caller's stores.
string testHome = Path.Combine(Path.GetFullPath(args[0]), "reference-home");
Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, testHome);
Directory.CreateDirectory(testHome);
try
{
    // Reuse the established generator without changing its command tree or the frozen documentation sets.
    Type generator = Assembly.Load("vericairn-docs").GetType("Vericairn.DocsTool.ReferenceSet", throwOnError: true)!;
    int result = (int)generator.GetMethod("Generate", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [args[0], args[1], false])!;
    if (result != 0) return result;
}
finally { Directory.Delete(testHome, recursive: true); }
string referencePath = Path.Combine(args[0], "cli-reference.md");

string[] rows = new Exception[] { new ClientTimeoutException(false), new ClientCancelledException(false, default), new ClientUnavailableException(false, new IOException()) }
    .Select(exception => ClientFailure.FromException(exception)!)
    .Select(failure => $"| `{failure.Code}` | `{failure.ExitCode}` | `error: {failure.Message}` |")
    .ToArray();
string uncertain = ClientFailure.FromException(new ClientTimeoutException(true))!.Message;
string plain = ClientFailure.FromException(new ClientTimeoutException(false))!.Message;
string section = """
## Interrupted client requests

An interrupted client request writes its diagnostic to standard error. It does not emit a replacement JSON result on standard output. Existing command names, options, help and successful JSON responses are unchanged.

| Outcome | Exit code | Diagnostic |
| --- | --- | --- |
""" + "\n" + string.Join('\n', rows) + "\n\n" +
    "When an effect may have happened, the diagnostic also says: `" + uncertain[plain.Length..].Trim() + "`\n\n" +
    "Caller cancellation (including Ctrl+C for ordinary requests) and a per-operation deadline are separate outcomes. Cancellation does not undo an effect, and these clients do not retry it. Foreground worker following still interprets Ctrl+C as a request to stop the original run; runtime serving still interprets it as a request to stop the service. Their requests remain subject to deadlines.\n";
string reference = File.ReadAllText(referencePath);
const string marker = "## Root options\n";
if (!reference.Contains(marker, StringComparison.Ordinal) || reference.Contains("## Interrupted client requests", StringComparison.Ordinal))
    throw new InvalidOperationException("Use a fresh CLI reference from vericairn-docs generate.");
File.WriteAllText(referencePath, reference.Replace(marker, section + "\n" + marker, StringComparison.Ordinal));
File.WriteAllText(args[2], "# Generated CLI reference: client outcomes\n\nGenerated from `ClientFailure` by `docs/cli-reference-generator`.\n\n" + section);

return 0;
