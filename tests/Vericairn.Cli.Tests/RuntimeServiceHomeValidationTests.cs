// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Text;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class RuntimeServiceHomeValidationTests
{
    [Theory]
    [InlineData("*invalid*", false, "The service's Vericairn home is invalid.")]
    [InlineData("relative/home", true, "The service's Vericairn home must be an absolute path.")]
    [InlineData("abs\nolute/home", true, "The service's Vericairn home contains a control character.")]
    public void ServeRejectsAnInvalidServiceHome(string input, bool encode, string diagnostic)
    {
        string root = CliHarness.TempDirectory("service-home-validation");
        try
        {
            if (input.Contains('\n', StringComparison.Ordinal))
                input = Path.Combine(root, input);
            string encoded = encode ? Base64Url.EncodeToString(Encoding.UTF8.GetBytes(input)) : input;

            CliHarness.Outcome outcome = CliHarness.Run("--estate", root, "runtime", "serve", "--service-home", encoded);

            Assert.Equal(2, outcome.Code);
            Assert.Equal(string.Empty, outcome.Out);
            Assert.Contains(diagnostic, outcome.Err, StringComparison.Ordinal);
            Assert.Contains("Reinstall the service with 'vericairn runtime install'.", outcome.Err, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }
}
