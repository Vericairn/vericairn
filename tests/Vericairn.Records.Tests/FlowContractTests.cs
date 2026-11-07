// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// The flow template kind (the estate's design/flow.md § 5 and § 13): what a flow declares, what its content refuses, and how an
/// extension adds steps and turns off only steps its base declares optional, never a required step, a reserved marking or a role's
/// holder.
/// </summary>
public class FlowContractTests
{
    [Fact]
    public void AFlowIsTemplateContentOfKindFlowResolvedInItsOrder()
    {
        TemplateDefinition song = TemplateDefinition.Read(FlowFixture.Element(FlowFixture.Song()));
        Assert.True(song.IsFlow);
        Assert.Equal("flow", song.Kind);
        Assert.Empty(song.Elements);
        FlowDefinition flow = FlowDefinition.Resolve(song, _ => throw new InvalidOperationException("No base."));
        Assert.Equal(["draft", "review-lyrics", "master", "shorts", "listen", "accept", "publish"], flow.Steps.Select(s => s.Key));
        Assert.Equal(["draft"], flow.Step("review-lyrics")!.After);
        Assert.Empty(flow.Step("draft")!.After);
        Assert.Equal(["review", "step.publish", "step.review-lyrics", "step.shorts"], flow.Options.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("on", flow.Options["step.shorts"].GetProperty("default").GetString());
        Assert.Equal("person", flow.Role("listener")!.Holder);
        Assert.Equal(["never publish without the operator"], flow.Never);
        Assert.Equal("document", TemplateDefinition.Read(FlowFixture.Element(TemplateFixture.Contract(TemplateFixture.Section("only")))).Kind);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "optional-without-default", "body.missing" },
        { "step-not-marked", "flow.marking" },
        { "item-not-marked", "flow.marking" },
        { "review-step-item-not-a-review", "flow.output" },
        { "required-with-default", "flow.marking" },
        { "undeclared-role", "flow.role" },
        { "forward-after", "flow.after" },
        { "input-from-later-step", "flow.input" },
        { "review-without-input", "flow.review" },
        { "review-option-on-a-review", "flow.review" },
        { "file-output-of-an-operation", "flow.output" },
        { "record-output-not-counted", "flow.output" },
        { "operation-without-done", "flow.done" },
        { "unknown-subject-binding", "flow.binding" },
        { "item-outside-each", "flow.binding" },
        { "each-over-a-record", "flow.each" },
        { "bad-duration", "flow.duration" },
        { "following-in-a-base", "flow.following" },
        { "off-in-a-base", "flow.off" },
        { "flow-step-without-its-flow", "body.missing" },
        { "unknown-reserved-act", "body.enum" },
        { "watch-on-a-work-step", "flow.when" },
        { "review-of-a-review", "flow.review" },
        { "item-required-with-default", "flow.marking" },
        { "provenance-from-an-unknown-input", "flow.provenance" },
        { "tool-of-a-record-item", "flow.provenance" },
        { "evidence-named-twice", "flow.duplicate" },
        { "review-in-an-undeclared-role", "flow.role" },
        { "reserved-without-its-command", "flow.reserved" },
        { "reserved-without-done", "flow.reserved" },
        { "command-binds-an-unknown-field", "flow.binding" },
        { "command-binds-an-instance-outside-each", "flow.binding" },
        { "command-binds-an-undeclared-input", "flow.binding" },
        { "command-binds-an-input-part-it-lacks", "flow.binding" },
        { "reserved-done-names-a-state", "flow.reserved" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void TheContentRefusesWhatAFlowCannotDeclare(string change, string code)
    {
        JsonObject song = FlowFixture.Song();
        JsonArray steps = song["steps"]!.AsArray();
        JsonObject Step(string key) => steps.Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == key);
        switch (change)
        {
            case "optional-without-default": Step("shorts").Remove("default"); break;
            case "required-with-default": Step("draft")["default"] = "on"; break;
            case "step-not-marked": Step("draft").Remove("required"); break;
            case "item-not-marked": Step("draft")["outputs"]![0]!.AsObject().Remove("required"); break;
            case "review-step-item-not-a-review": Step("review-lyrics")["outputs"] = new JsonArray(FlowFixture.FileItem("notes")); break;
            case "undeclared-role": Step("draft")["role"] = "lyricist"; break;
            case "forward-after": Step("draft")["after"] = new JsonArray("master"); break;
            case "input-from-later-step": Step("draft")["inputs"] = new JsonArray(FlowFixture.Input("master", "master", "master")); break;
            case "review-without-input": Step("review-lyrics").Remove("inputs"); break;
            case "review-option-on-a-review": Step("review-lyrics")["review"] = "independent"; break;
            case "file-output-of-an-operation": Step("publish")["outputs"] = new JsonArray(new JsonObject { ["name"] = "page", ["kind"] = "file", ["required"] = true }); break;
            case "record-output-not-counted": Step("publish")["outputs"] = new JsonArray(new JsonObject { ["name"] = "release", ["kind"] = "record", ["recordType"] = "release", ["required"] = true }); break;
            case "operation-without-done": Step("publish").Remove("done"); break;
            case "unknown-subject-binding": Step("publish")["done"] = new JsonArray(new JsonObject { ["type"] = "release", ["subject"] = "{subject.carrier}" }); break;
            case "item-outside-each": Step("publish")["done"] = new JsonArray(new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = "{instance}" } }); break;
            case "each-over-a-record": Step("shorts")["each"] = "recording"; break;
            case "bad-duration": Step("publish")["when"] = new JsonObject { ["due"] = new JsonObject { ["after"] = "one day", ["from"] = "accept" } }; break;
            case "following-in-a-base": Step("publish")["following"] = "accept"; break;
            case "off-in-a-base": song["off"] = new JsonArray("shorts"); break;
            case "flow-step-without-its-flow": Step("publish")["kind"] = "flow"; break;
            case "unknown-reserved-act": Step("publish")["reserved"] = "deploy-anything"; break;
            case "review-of-a-review": Step("review-lyrics")["outputs"]![0]!["review"] = "person"; break;
            case "item-required-with-default": Step("draft")["outputs"]![0]!["default"] = "on"; break;
            case "provenance-from-an-unknown-input": Step("master")["outputs"]![0]!["provenance"] = new JsonObject { ["inputs"] = new JsonArray("melody") }; break;
            case "tool-of-a-record-item": Step("accept")["outputs"] = new JsonArray(new JsonObject { ["name"] = "minutes", ["kind"] = "decision", ["required"] = true, ["provenance"] = new JsonObject { ["tool"] = "claude-code" } }); break;
            case "evidence-named-twice": Step("draft")["outputs"]![0]!["evidence"] = new JsonArray(new JsonObject { ["name"] = "spell", ["method"] = "spellcheck" }, new JsonObject { ["name"] = "spell", ["method"] = "lint" }); break;
            case "review-in-an-undeclared-role": Step("draft")["outputs"]![0]!["review"] = new JsonObject { ["independence"] = "independent", ["role"] = "editor" }; break;
            case "watch-on-a-work-step": Step("master")["when"] = new JsonObject { ["watch"] = new JsonObject { ["every"] = "PT1H", ["limit"] = "P1D" } }; Step("master")["done"] = new JsonArray(FlowFixture.Marker("MASTERED")); break;
            case "reserved-without-its-command": Step("publish").Remove("action"); break;
            case "reserved-without-done": Step("draft")["reserved"] = "security-review"; Step("draft")["action"] = "example-provider review --goal {goal}"; break;
            case "command-binds-an-unknown-field": Step("publish")["action"] = "example-provider publish --carrier {subject.carrier}"; break;
            case "command-binds-an-instance-outside-each": Step("publish")["action"] = "example-provider publish --track {instance}"; break;
            case "command-binds-an-undeclared-input": Step("publish")["action"] = "example-provider publish --master {input.master.hash}"; break;
            case "command-binds-an-input-part-it-lacks": Step("accept")["inputs"] = new JsonArray(FlowFixture.Input("master", "master", "master")); Step("accept")["action"] = "example-provider accept --size {input.master.size}"; break;
            case "reserved-done-names-a-state": Step("accept")["done"] = new JsonArray(new JsonObject { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = "SONG-1" }, ["state"] = "accepted" }); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        IReadOnlyList<Finding> findings = TemplateDefinition.Validate(FlowFixture.Element(song));
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Code == code);
        Assert.Throws<InvalidOperationException>(() => TemplateDefinition.Read(FlowFixture.Element(song)));
    }

    [Fact]
    public void AnExtensionAddsStepsAndTurnsOffOnlyAStepItsBaseDeclaresOptional()
    {
        TemplateDefinition song = TemplateDefinition.Read(FlowFixture.Element(FlowFixture.Song()));
        JsonObject extension = Extension(song);
        extension["roles"] = new JsonArray(new JsonObject { ["id"] = "mixer", ["holder"] = "agent", ["brief"] = "Mix down." });
        JsonObject mixdown = FlowFixture.Step("mixdown", "work", "mixer", inputs: new JsonArray(FlowFixture.Input("master", "master", "master")), outputs: new JsonArray(FlowFixture.FileItem("mix")));
        mixdown["following"] = "master";
        extension["steps"] = new JsonArray(mixdown);
        extension["off"] = new JsonArray("shorts");
        extension["never"] = new JsonArray("never post a clip");
        FlowDefinition flow = Resolve(extension, song);
        Assert.Equal(["draft", "review-lyrics", "master", "mixdown", "shorts", "listen", "accept", "publish"], flow.Steps.Select(s => s.Key));
        Assert.Equal(["master"], flow.Step("mixdown")!.After);
        Assert.Equal(["master"], flow.Step("shorts")!.After);
        Assert.True(flow.TurnedOff.ContainsKey("shorts"));
        Assert.False(flow.Options.ContainsKey("step.shorts"));
        Assert.Equal(["never publish without the operator", "never post a clip"], flow.Never);
        Assert.Equal(2, flow.Chain.Count);
        Assert.Equal(song.Hash, flow.Chain[0].Hash);
    }

    public static TheoryData<string, string> ExtensionRefusals => new()
    {
        { "off-required", "turns off 'listen', a required step" },
        { "off-unknown", "turns off 'mixing', which no base declares" },
        { "redeclare-reserved", "declares step 'publish' again" },
        { "redeclare-role", "declares role 'listener' again" },
        { "branch-takes-base-step", "a branch never takes in a base's step" },
        { "required-loses-input", "leaves required step 'master' without its input 'verdict'" },
        { "unknown-binding", "binds {subject.carrier}" },
        { "base-is-a-document", "A flow extends only another flow." },
    };

    [Theory]
    [MemberData(nameof(ExtensionRefusals))]
    public void AnExtensionNeverTurnsOffARequiredStepAReservedMarkingOrARolesHolder(string change, string message)
    {
        JsonObject base0 = FlowFixture.Song();
        if (change == "required-loses-input")
        {
            JsonObject master = base0["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "master");
            master["inputs"]!.AsArray().Add(FlowFixture.Input("verdict", "review-lyrics", "verdict"));
        }

        TemplateDefinition song = TemplateDefinition.Read(FlowFixture.Element(base0));
        TemplateDefinition document = TemplateDefinition.Read(FlowFixture.Element(TemplateFixture.Contract(TemplateFixture.Section("only"))));
        JsonObject extension = Extension(change == "base-is-a-document" ? document : song);
        switch (change)
        {
            case "off-required": extension["off"] = new JsonArray("listen"); break;
            case "off-unknown": extension["off"] = new JsonArray("mixing"); break;
            case "redeclare-reserved": extension["steps"] = new JsonArray(FlowFixture.Step("publish", "operation", "decider", optional: "on", done: new JsonArray(FlowFixture.Marker("PUBLISHED")))); break;
            case "redeclare-role": extension["roles"] = new JsonArray(new JsonObject { ["id"] = "listener", ["holder"] = "agent", ["brief"] = "An agent listens." }); break;
            case "branch-takes-base-step": extension["branches"] = new JsonArray(new JsonObject { ["name"] = "withdraw", ["when"] = new JsonArray(FlowFixture.Marker("WITHDRAWN")), ["steps"] = new JsonArray("publish") }); break;
            case "required-loses-input": extension["off"] = new JsonArray("review-lyrics"); break;
            case "unknown-binding": extension["steps"] = new JsonArray(FlowFixture.Step("archive", "operation", "decider", done: new JsonArray(new JsonObject { ["type"] = "release", ["subject"] = "{subject.carrier}" }))); break;
            case "base-is-a-document": break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        TemplateDefinition read = TemplateDefinition.Read(FlowFixture.Element(extension));
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            FlowDefinition.Resolve(read, pin => pin.Hash == song.Hash ? song : pin.Hash == document.Hash ? document : throw new InvalidOperationException("No such base.")));
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    private static JsonObject Extension(TemplateDefinition base0) => new()
    {
        ["format"] = "vericairn.template/1", ["kind"] = "flow", ["id"] = "example/song-extended", ["version"] = "1.0.0", ["purpose"] = "The song, extended.",
        ["extends"] = new JsonObject { ["id"] = base0.Id, ["version"] = base0.Version, ["hash"] = base0.Hash },
    };

    private static FlowDefinition Resolve(JsonObject extension, TemplateDefinition base0) =>
        FlowDefinition.Resolve(TemplateDefinition.Read(FlowFixture.Element(extension)), pin => pin.Hash == base0.Hash ? base0 : throw new InvalidOperationException("No such base."));
}
