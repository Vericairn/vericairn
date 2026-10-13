// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Cli;

// Output is UTF-8 without a byte-order mark and uses LF line endings on every platform, so
// machine-readable output (status --json, records new --json) is byte-identical everywhere.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";

return CliApp.Build().Parse(args).Invoke();
