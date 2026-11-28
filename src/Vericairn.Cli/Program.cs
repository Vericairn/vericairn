// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Cli;
using Vericairn.Records.Estate;

// Output is UTF-8 without a byte-order mark and uses LF line endings on every platform, so
// machine-readable output (status --json, records new --json) is byte-identical everywhere.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";

if (Vericairn.Adapters.Git.WorkspaceScopeProbe.TryRun(args) is int scopeResult)
{
    return scopeResult;
}

if (Vericairn.Runtime.Workers.HarnessReach.TryRun(args) is int reachResult)
{
    return reachResult;
}

if (Vericairn.Runtime.Workers.StopDrill.TryRun(args) is int drillResult)
{
    return drillResult;
}

var parsed = CliApp.Build().Parse(args);
if (parsed.Errors.Any(error => SecretShapes.RedactOutput(error.Message) != error.Message))
{
    Console.Error.WriteLine("error: Command arguments included credential-shaped text. For a Claude Code setup token, wait for the hidden prompt or use --token-stdin.");
    return 1;
}

return parsed.Invoke();
