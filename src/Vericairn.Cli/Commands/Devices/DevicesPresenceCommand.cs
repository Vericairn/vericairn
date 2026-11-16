// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DevicesPresenceCommand : Command
{
    public DevicesPresenceCommand(CommandContext context)
        : base("presence", "From 0.9.4: choose, on the machine holding a person's device key, whether its signatures ask one presence check per signature or one per operator action's announced list (a presence session), and the session's limit; kept owner-only in this user's machine state, never in a record. Without a choice it shows the device's setting. The estate's charter (policies.presence) may ask per-signature for every device, and the stricter of the two wins.")
    {
        var operations = new DeviceOperations(context);
        var deviceArgument = new Argument<string>("device") { Description = "Device record id: a device whose key this machine holds." };
        var perSignatureOption = new Option<bool>("--per-signature") { Description = "Every signature asks its own presence check, as before 0.9.4." };
        var perActionOption = new Option<bool>("--per-action") { Description = "An operator action may ask one presence check for the list it announces (the default)." };
        var limitOption = new Option<int?>("--limit") { Description = "With --per-action, the longest a presence session lasts after its check, in seconds: 60 to 300 (default 300)." };
        Arguments.Add(deviceArgument);
        Options.Add(perSignatureOption);
        Options.Add(perActionOption);
        Options.Add(limitOption);
        SetAction(parseResult => operations.DevicesPresence(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(perSignatureOption),
            parseResult.GetValue(perActionOption), parseResult.GetValue(limitOption), parseResult.GetValue(context.JsonOption)));
    }
}
