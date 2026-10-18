// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class CommandAuditFix26Tests
{
    [Theory]
    [InlineData("printenv VERICAIRN_HOME")]
    [InlineData("printenv -- VERICAIRN_HOME")]
    [InlineData("printenv -0 VERICAIRN_HOME")]
    [InlineData("printenv --null VERICAIRN_HOME VERICAIRN_HOME")]
    [InlineData("env -i printenv VERICAIRN_HOME")]
    [InlineData("sh -c 'printenv VERICAIRN_HOME'")]
    [InlineData("echo $VERICAIRN_HOME")]
    [InlineData("printf '%s' \"${VERICAIRN_HOME}\"")]
    public void PrintingOnlyTheHomeLocationDoesNotStop(string command) =>
        Assert.DoesNotContain(Inspect(command), signal => signal.Severity == "critical");

    [Theory]
    [InlineData("printenv")]
    [InlineData("printenv -0")]
    [InlineData("printenv VERICAIRN_HOME OTHER")]
    [InlineData("printenv VERICAIRN_HOME --unknown")]
    [InlineData("printenv VERICAIRN_HOME; printenv")]
    [InlineData("printenv VERICAIRN_HOME > ../outside")]
    [InlineData("printenv VERICAIRN_HOME; cat .ssh/id_rsa")]
    public void LocationExceptionsDoNotHideOtherEffects(string command) =>
        Assert.Contains(Inspect(command), signal => signal.Severity == "critical");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternTimeoutsRetryOnceWithAGenerousLimit(bool exhaust)
    {
        var limits = new List<TimeSpan>();
        var pattern = new Regex("x", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        bool Match(Regex regex)
        {
            limits.Add(regex.MatchTimeout);
            if (exhaust || limits.Count == 1) throw new RegexMatchTimeoutException();
            return regex.IsMatch("x");
        }
        if (exhaust) Assert.ThrowsAny<TimeoutException>(() => CommandAudit.MatchWithRetry(pattern, Match));
        else Assert.True(CommandAudit.MatchWithRetry(pattern, Match));
        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)], limits);
    }

    private static IReadOnlyList<RunSignal> Inspect(string command) => CommandAudit.Inspect(
        new HarnessAction("command", "exec_command", JsonSerializer.SerializeToElement(new { command })),
        Path.Combine(Path.GetTempPath(), "audit-workspace"), []);
}
