// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ReleaseWithdrawCommand : Command
{
    public ReleaseWithdrawCommand(CommandContext context) : base("withdraw", "Record a candidate's withdrawal and draft-deletion intent together; the operator deletes the draft and records dispatch before observation.")
    {
        var release = new Argument<string>("release");
        var by = new Option<string>("--by") { Required = true, Description = "Acting principal id." };
        var expected = new Option<string>("--expected") { Required = true, Description = "Expected current candidate release hash." };
        var provider = new Option<string>("--provider") { Required = true, Description = "Provider responsible for this draft; recorded explicitly." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Why this candidate is withdrawn." };
        var settings = new Option<string[]>("--setting") { Description = "Provider setting as name=value; repeatable. Keeps the candidate's repository, version and product.", AllowMultipleArgumentsPerToken = true };
        Arguments.Add(release);
        Options.Add(by);
        Options.Add(expected);
        Options.Add(provider);
        Options.Add(reason);
        Options.Add(settings);
        SetAction(p =>
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string setting in p.GetValue(settings) ?? [])
            {
                int equals = setting.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0 || !values.TryAdd(setting[..equals], setting[(equals + 1)..]))
                    return context.Fail("Each setting is name=value, with each name supplied once.");
            }
            return context.WithApi(p.GetValue(context.EstateOption)!, api => ReleaseOutput.Write(context,
                api.ReleaseWithdraw(new ReleaseWithdrawRequest(p.GetValue(release)!, p.GetValue(by)!, p.GetValue(expected)!,
                    p.GetValue(provider)!, p.GetValue(reason)!, values)), p.GetValue(context.JsonOption)));
        });
    }
}
