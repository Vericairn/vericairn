// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// A declared flow and a run of it in a disposable format-3 estate: a song taken from lyrics to release, with work and review steps,
/// a step for each track, a person's listening review, and reserved acceptance and publication.
/// </summary>
public sealed class FlowFixture : IDisposable
{
    public FlowFixture(JsonObject? flow = null, JsonObject? options = null, string? until = null, JsonArray? tracks = null, bool roster = true, JsonArray? roles = null, bool run = true, Func<FlowFixture, JsonObject>? subject = null)
    {
        Policies = new PolicyFixture();
        Flow = Install(Estate, flow ?? Song());
        Work = Add("work-order", new JsonObject
        {
            ["key"] = "SONG-1", ["kind"] = "ordinary", ["title"] = "Produce the song", ["intent"] = "Produce a song and release it.", ["scope"] = new JsonArray("path:song/"),
            ["exitQuestion"] = "Is the song released?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("the released song"),
        });
        if (roster)
        {
            Roster = Add("roster", new JsonObject
            {
                ["roles"] = new JsonArray(
                    Role("writer", Estate.BuilderId), Role("reviewer", Estate.OperatorId), Role("listener", Estate.OperatorId), Role("decider", Estate.OperatorId)),
            });
        }

        if (run)
        {
            Goal = Add("goal", GoalBody(options, until, tracks, roles, subject: subject?.Invoke(this)));
            Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: Goal.Reference);
        }
    }

    public PolicyFixture Policies { get; }

    public TestEstate Estate => Policies.Estate;

    public TemplateDefinition Flow { get; private set; }

    public RecordEnvelope Work { get; }

    public RecordEnvelope? Roster { get; }

    public RecordEnvelope Goal { get; private set; } = null!;

    /// <summary>A run with a step's assignment pinning its input, for the format-3 round trip of every new field.</summary>
    public static FlowFixture RecordedFixture()
    {
        var run = new FlowFixture();
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", Hash('a'), "text/markdown"));
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(PinOf(lyrics, "lyrics")));
        run.ResultWith(mastering, new JsonObject { ["inputs"] = new JsonArray(PinOf(lyrics, "lyrics")), ["tool"] = "example-tool", ["settings"] = new JsonObject { ["effort"] = "high" } }, "completed", ("master", Hash('b'), null));
        run.Add("operation", new JsonObject
        {
            ["provider"] = "example-provider", ["action"] = "publish-clip", ["identity"] = "example-provider:clip-1", ["intent"] = "Publish the clip.",
            ["target"] = new JsonObject { ["repository"] = "Example/song" }, ["status"] = "not-started", ["evidence"] = new JsonArray(),
            ["provenance"] = new JsonObject { ["provider"] = "example-provider", ["settings"] = new JsonObject { ["region"] = "north" } },
        });

        // A reserved step's operator request names the step it waits on and carries its command.
        string command = $"example-provider accept --recording {run.Work.Id} --goal {run.Goal.Id}";
        run.Add("operator-request", new JsonObject
        {
            ["to"] = run.Estate.OperatorId, ["askedBy"] = run.Estate.OperatorId, ["action"] = "Carry the reserved act accept-release of step accept.", ["why"] = "The step is next.",
            ["recommendation"] = "Run the command yourself.", ["consequence"] = "The step waits.", ["kind"] = "action", ["destination"] = "the reserved act of step accept",
            ["content"] = new JsonArray(new JsonObject { ["kind"] = "command", ["text"] = command, ["hash"] = Hashing.ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(command)) }),
            ["flowStep"] = new JsonObject { ["goal"] = run.Goal.Id, ["key"] = "accept" },
        }, subject: run.Work.Reference);
        return run;
    }

    /// <summary>
    /// A run of the song taken to its acceptance, with the operator's request for it recorded: the builder drafts and masters, the
    /// operator's review of the master is the listening review, and acceptance waits on the operator with its exact command.
    /// </summary>
    public static FlowFixture AtAcceptance()
    {
        var run = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", Hash('a'), "text/markdown"));
        RecordEnvelope master = run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(PinOf(lyrics, "lyrics"))), "completed", ("master", Hash('b'), null));
        run.Review(master);
        string command = run.AcceptCommandNow;
        run.Request = run.Add("operator-request", new JsonObject
        {
            ["to"] = run.Estate.OperatorId, ["askedBy"] = run.Estate.OperatorId, ["action"] = "Carry the reserved act accept-release of step accept.", ["why"] = "The step is next.",
            ["recommendation"] = "Run the command yourself.", ["consequence"] = "The step waits.", ["kind"] = "action", ["destination"] = "the reserved act of step accept",
            ["content"] = new JsonArray(new JsonObject { ["kind"] = "command", ["text"] = command, ["hash"] = Hashing.ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(command)) }),
            ["flowStep"] = new JsonObject { ["goal"] = run.Goal.Id, ["key"] = "accept" },
        }, subject: run.Work.Reference);
        return run;
    }

    /// <summary>The operator's request for acceptance, when <see cref="AtAcceptance"/> recorded one.</summary>
    public RecordEnvelope? Request { get; private set; }

    /// <summary>The song's acceptance command as the run's records bind it.</summary>
    public string AcceptCommandNow => $"example-provider accept --recording {Work.Id} --goal {Goal.Id}";

    public static JsonObject Role(string role, params string[] principals) => new() { ["role"] = role, ["principals"] = new JsonArray(principals.Select(p => (JsonNode)p).ToArray()) };

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
            Step("draft", "work", "writer", outputs: new JsonArray(FileItem("lyrics", "text/markdown"))),
            Step("review-lyrics", "review", "reviewer", optional: "on", inputs: new JsonArray(Input("lyrics", "draft", "lyrics")), outputs: new JsonArray(new JsonObject { ["name"] = "verdict", ["kind"] = "record", ["recordType"] = "review", ["required"] = true })),
            Step("master", "work", "writer", inputs: new JsonArray(Input("lyrics", "draft", "lyrics")), outputs: new JsonArray(FileItem("master"))),
            Step("shorts", "work", "writer", optional: "on", each: "tracks", inputs: new JsonArray(Input("master", "master", "master")), outputs: new JsonArray(FileItem("clip"))),
            Step("listen", "review", "listener", inputs: new JsonArray(Input("master", "master", "master"))),
            Step("accept", "decision", "decider", reserved: "accept-release", done: new JsonArray(Marker("ACCEPTED")), action: AcceptCommand),
            Step("publish", "operation", "decider", optional: "on", reserved: "publish", done: new JsonArray(Marker("PUBLISHED")), action: PublishCommand)),
    };

    /// <summary>The song's acceptance command, bound to the run's recording and goal when the step is next.</summary>
    public const string AcceptCommand = "example-provider accept --recording {subject.recording} --goal {goal}";

    /// <summary>The song's publication command, bound to the run's recording and work order when the step is next.</summary>
    public const string PublishCommand = "example-provider publish --recording {subject.recording} --work {work}";

    public static JsonObject Step(string key, string kind, string role, string? optional = null, string? reserved = null, string? each = null, JsonArray? inputs = null, JsonArray? outputs = null, JsonArray? done = null, string? action = null)
    {
        var step = new JsonObject
        {
            ["key"] = key, ["title"] = "Step " + key, ["kind"] = kind, ["role"] = role, ["recovery"] = "Carry " + key + " again from its pinned inputs.",
        };
        if (optional is null) step["required"] = true;
        else { step["optional"] = true; step["default"] = optional; }
        if (reserved is not null) step["reserved"] = reserved;
        if (each is not null) step["each"] = each;
        if (inputs is not null) step["inputs"] = inputs;
        if (outputs is not null) step["outputs"] = outputs;
        if (done is not null) step["done"] = done;
        if (action is not null) step["action"] = action;
        return step;
    }

    /// <summary>A required file item, by name and hash and, when given, media type.</summary>
    public static JsonObject FileItem(string name, string? mediaType = null)
    {
        var item = new JsonObject { ["name"] = name, ["kind"] = "file", ["required"] = true };
        if (mediaType is not null) item["mediaType"] = mediaType;
        return item;
    }

    public static JsonObject Input(string name, string step, string output) => new() { ["name"] = name, ["from"] = "step", ["step"] = step, ["output"] = output };

    /// <summary>A predicate that holds once a work order with a marker key exists: a stand-in for the record an act leaves.</summary>
    public static JsonObject Marker(string key) => new() { ["type"] = "work-order", ["fields"] = new JsonObject { ["key"] = key } };

    public static TemplateDefinition Install(TestEstate estate, JsonObject content) => TemplateFixture.Install(estate, content);

    public static JsonObject Pin(TemplateDefinition flow) => TemplateFixture.Pin(flow);

    public JsonObject GoalBody(JsonObject? options = null, string? until = null, JsonArray? tracks = null, JsonArray? roles = null, TemplateDefinition? flow = null, JsonObject? subject = null)
    {
        var plan = new JsonObject
        {
            ["pin"] = Pin(flow ?? Flow),
            ["subject"] = subject?.DeepClone() ?? new JsonObject { ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = Work.Id }, ["tracks"] = tracks ?? new JsonArray("intro", "outro") },
        };
        if (options is not null) plan["options"] = options;
        if (until is not null) plan["until"] = until;
        var body = new JsonObject
        {
            ["title"] = "Release the song", ["objective"] = "Produce a song and release it.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = Marker("PUBLISHED") }), ["constraints"] = new JsonArray("nothing outside the song"),
            ["kind"] = "finite", ["sponsor"] = Estate.OperatorId, ["budget"] = new JsonObject { ["wallMinutes"] = 600, ["attemptsPerStep"] = 3 },
            ["plan"] = new JsonObject { ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(Work.Id), ["flow"] = plan },
            ["inputs"] = new JsonArray(),
        };
        if (roles is not null) body["roles"] = roles;
        return body;
    }

    public RecordEnvelope Add(string type, JsonObject body, bool builder = false, RecordReference? subject = null, string? supersedes = null) =>
        Policies.Add(type, body, builder: builder, subject: subject, supersedes: supersedes);

    /// <summary>The operator's assignment of a step to a contributor, pinning the inputs given.</summary>
    public RecordEnvelope Assign(string step, string contributor, string? instance = null, JsonArray? inputs = null)
    {
        var named = new JsonObject { ["key"] = step };
        if (instance is not null) named["instance"] = instance;
        if (inputs is not null) named["inputs"] = inputs;
        return Add("assignment", new JsonObject
        {
            ["contributor"] = contributor, ["step"] = named,
            ["brief"] = new JsonObject
            {
                ["outcome"] = "Carry step " + step + ".", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:song/"), ["prohibitedEffects"] = new JsonArray("publication"),
                ["workspace"] = new JsonArray("song repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("the step's outputs"),
                ["reviewCriteria"] = new JsonArray("the outputs exist"), ["recovery"] = "Carry it again.",
            },
        }, subject: Work.Reference);
    }

    /// <summary>The contributor's attempt and completed result on an assignment, naming outputs by name and hash.</summary>
    public RecordEnvelope Result(RecordEnvelope assignment, string outcome = "completed", params (string Name, string Hash, string? MediaType)[] outputs) =>
        ResultWith(assignment, null, outcome, outputs);

    /// <summary>The contributor's attempt and result on an assignment, with the provenance the result records.</summary>
    public RecordEnvelope ResultWith(RecordEnvelope assignment, JsonObject? provenance, string outcome = "completed", params (string Name, string Hash, string? MediaType)[] outputs)
    {
        bool builder = EstateModel.BodyString(assignment, "contributor") == Estate.BuilderId;
        RecordEnvelope attempt = Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(Estate.Clock), ["workspace"] = "song repository, a work branch", ["note"] = "Carry the step." }, builder: builder, subject: assignment.Reference);
        var named = new JsonArray();
        foreach ((string name, string hash, string? media) in outputs)
        {
            var output = new JsonObject { ["name"] = name, ["hash"] = hash };
            if (media is not null) output["mediaType"] = media;
            named.Add(output);
        }

        var body = new JsonObject
        {
            ["outcome"] = outcome, ["summary"] = "Carried the step.", ["outputs"] = named, ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Review it.",
        };
        if (provenance is not null) body["provenance"] = provenance;
        return Add("result", body, builder: builder, subject: attempt.Reference);
    }

    /// <summary>The operator's review of a result.</summary>
    public RecordEnvelope Review(RecordEnvelope result, string outcome = "pass") =>
        Add("review", new JsonObject { ["independence"] = "operator", ["outcome"] = outcome, ["summary"] = "Reviewed the exact result.", ["findings"] = new JsonArray() }, subject: result.Reference);

    public RecordEnvelope Marked(string key, bool builder = false) => Add("work-order", new JsonObject
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Marker " + key, ["intent"] = "Stand in for an act's record.", ["scope"] = new JsonArray("marker"),
        ["exitQuestion"] = "Recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("marker"),
    }, builder: builder);

    public static string Hash(char c) => "sha256:" + new string(c, 64);

    public FlowPosition.Position Position(DateTimeOffset? at = null) => FlowPosition.Derive(Estate.Load(), Goal, at ?? Estate.Clock);

    public FlowPosition.StepInstance Step(string name, DateTimeOffset? at = null) => Assert.Single(Position(at).Steps, s => s.Name == name);

    public static JsonObject PinOf(RecordEnvelope record, string name) => new() { ["name"] = name, ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash };

    public static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    public void Dispose() => Policies.Dispose();
}
