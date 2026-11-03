// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// A declared flow for tests: a song taken from lyrics to release, with work and review steps, a step for each track, a person's
/// listening review, and reserved acceptance and publication.
/// </summary>
public static class FlowFixture
{
    /// <summary>The song flow: lyrics, their review, the master, a clip per track, a person's listening review, acceptance and publication.</summary>
    public static JsonObject Song() => new()
    {
        ["format"] = "vericairn.template/1", ["kind"] = "flow", ["id"] = "example/song", ["version"] = "1.0.0", ["purpose"] = "Produce a song and release it.",
        ["subject"] = new JsonObject
        {
            ["summary"] = "a recording",
            ["fields"] = new JsonArray(
                new JsonObject { ["name"] = "recording", ["type"] = "record", ["recordType"] = "work-order" },
                new JsonObject { ["name"] = "tracks", ["type"] = "list" }),
        },
        ["roles"] = new JsonArray(
            new JsonObject { ["id"] = "writer", ["holder"] = "either", ["brief"] = "Write and produce the song's material." },
            new JsonObject { ["id"] = "reviewer", ["holder"] = "either", ["brief"] = "Review what the writer made, independently." },
            new JsonObject { ["id"] = "listener", ["holder"] = "person", ["brief"] = "Listen to the master: a judgment reserved to people." },
            new JsonObject { ["id"] = "decider", ["holder"] = "person", ["brief"] = "Accept and publish." }),
        ["options"] = new JsonArray(new JsonObject { ["id"] = "review", ["values"] = new JsonArray("independent", "person", "none"), ["default"] = "none" }),
        ["never"] = new JsonArray("never publish without the operator"),
        ["steps"] = new JsonArray(
            Step("draft", "work", "writer", outputs: new JsonArray(new JsonObject { ["name"] = "lyrics", ["type"] = "file", ["mediaType"] = "text/markdown" })),
            Step("review-lyrics", "review", "reviewer", optional: "on", inputs: new JsonArray(Input("lyrics", "draft", "lyrics")), outputs: new JsonArray(new JsonObject { ["name"] = "verdict", ["type"] = "review" })),
            Step("master", "work", "writer", inputs: new JsonArray(Input("lyrics", "draft", "lyrics")), outputs: new JsonArray(new JsonObject { ["name"] = "master", ["type"] = "file" })),
            Step("shorts", "work", "writer", optional: "on", each: "tracks", inputs: new JsonArray(Input("master", "master", "master")), outputs: new JsonArray(new JsonObject { ["name"] = "clip", ["type"] = "file" })),
            Step("listen", "review", "listener", inputs: new JsonArray(Input("master", "master", "master"))),
            Step("accept", "decision", "decider", reserved: "accept-release", done: new JsonArray(Marker("ACCEPTED"))),
            Step("publish", "operation", "decider", optional: "on", reserved: "publish", done: new JsonArray(Marker("PUBLISHED")))),
    };

    public static JsonObject Step(string key, string kind, string role, string? optional = null, string? reserved = null, string? each = null, JsonArray? inputs = null, JsonArray? outputs = null, JsonArray? done = null)
    {
        var step = new JsonObject
        {
            ["key"] = key, ["title"] = "Step " + key, ["kind"] = kind, ["role"] = role, ["presence"] = optional is null ? "required" : "optional", ["recovery"] = "Carry " + key + " again from its pinned inputs.",
        };
        if (optional is not null) step["default"] = optional;
        if (reserved is not null) step["reserved"] = reserved;
        if (each is not null) step["each"] = each;
        if (inputs is not null) step["inputs"] = inputs;
        if (outputs is not null) step["outputs"] = outputs;
        if (done is not null) step["done"] = done;
        return step;
    }

    public static JsonObject Input(string name, string step, string output) => new() { ["name"] = name, ["from"] = "step", ["step"] = step, ["output"] = output };

    /// <summary>A predicate that holds once a work order with a marker key exists: a stand-in for the record an act leaves.</summary>
    public static JsonObject Marker(string key) => new() { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = key } };

    public static TemplateDefinition Install(TestEstate estate, JsonObject content) => TemplateFixture.Install(estate, content);

    public static JsonObject Pin(TemplateDefinition flow) => TemplateFixture.Pin(flow);

    public static string Hash(char c) => "sha256:" + new string(c, 64);

    public static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
}
