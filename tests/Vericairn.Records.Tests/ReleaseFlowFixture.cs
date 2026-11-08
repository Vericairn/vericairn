// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// A run of the software pack's release flow in a disposable format-3 estate: a product, its release carrier with two placed work
/// orders, an agent reviewer, the roster naming each of the flow's roles, and an active goal whose plan names the shipped flow. Each
/// method records what the step's product command, or the operator's act, records.
/// </summary>
public sealed class ReleaseFlowFixture : IDisposable
{
    public const string Version = "1.2.0";

    public const string Repository = "Example/example";

    public const string Commit = "0123456789abcdef0123456789abcdef01234567";

    public ReleaseFlowFixture(bool firstParty = false, JsonObject? options = null, Func<ReleaseFlowFixture, JsonObject>? policy = null)
    {
        Records = new PolicyFixture();
        RecordEnvelope basis = Add("decision", Records.DecisionBody());
        Add("authority", new JsonObject
        {
            ["grantee"] = Estate.BuilderId, ["scope"] = new JsonArray("local-engineering"), ["reserved"] = new JsonArray("publication"), ["conditions"] = new JsonArray(), ["basis"] = basis.Id,
        });
        Product = Add("product", ReleaseLineFixture.ProductBody());
        Carrier = Add("work-order", Order("REL-1", releasePlan: true));
        First = Add("work-order", Order("WORK-1", placed: true));
        Second = Add("work-order", Order("WORK-2", placed: true));
        Reviewer = Add("principal", JsonNode.Parse("""{"kind":"agent","name":"Test Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Reviewer"}}""")!.AsObject()).Id;
        Add("roster", new JsonObject
        {
            ["roles"] = new JsonArray(
                FlowFixture.Role("release-manager", Estate.BuilderId), FlowFixture.Role("submitter", Estate.OperatorId), FlowFixture.Role("reviewer", Reviewer),
                FlowFixture.Role("decider", Estate.OperatorId), FlowFixture.Role("publisher", Estate.OperatorId)),
        });
        if (policy is not null)
        {
            Records.Policy(policy(this));
        }

        Flow = firstParty ? InstallFirstParty(Estate) : SoftwareFlows.ReleaseFlow;
        Goal = Add("goal", GoalBody(Version, options));
        Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: Goal.Reference);
    }

    public PolicyFixture Records { get; }

    public TestEstate Estate => Records.Estate;

    public TemplateDefinition Flow { get; }

    public RecordEnvelope Product { get; }

    public RecordEnvelope Carrier { get; }

    public RecordEnvelope First { get; }

    public RecordEnvelope Second { get; }

    public string Reviewer { get; }

    public RecordEnvelope Goal { get; private set; }

    public RecordEnvelope Add(string type, JsonObject body, bool builder = false, RecordReference? subject = null, string? supersedes = null) =>
        Records.Add(type, body, builder: builder, subject: subject, supersedes: supersedes);

    /// <summary>A record by an agent principal other than the builder, signed with its own key: here, the reviewer's.</summary>
    public RecordEnvelope ByReviewer(string type, JsonObject body, RecordReference? subject = null) =>
        Estate.Add(type, Reviewer, body.ToJsonString(), subject);

    public JsonObject GoalBody(string version, JsonObject? options = null)
    {
        var plan = new JsonObject
        {
            ["pin"] = FlowFixture.Pin(Flow),
            ["subject"] = new JsonObject
            {
                ["carrier"] = new JsonObject { ["type"] = "work-order", ["id"] = Carrier.Id },
                ["product"] = new JsonObject { ["type"] = "product", ["id"] = Product.Id },
                ["version"] = version,
                ["repository"] = Repository,
            },
        };
        if (options is not null) plan["options"] = options;
        return new JsonObject
        {
            ["title"] = "Release " + version, ["objective"] = "Release the product from its placed work to its closed carrier.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = new JsonObject { ["type"] = "acceptance", ["subject"] = Carrier.Id } }),
            ["constraints"] = new JsonArray("nothing outside the release"), ["kind"] = "finite", ["sponsor"] = Estate.OperatorId,
            ["budget"] = new JsonObject { ["wallMinutes"] = 600, ["attemptsPerStep"] = 3 },
            ["plan"] = new JsonObject { ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(Carrier.Id), ["flow"] = plan },
            ["inputs"] = new JsonArray(),
        };
    }

    /// <summary>The sponsor revises the run for a corrective version, as after a withdrawal.</summary>
    public RecordEnvelope Revise(string version)
    {
        Goal = Add("goal", GoalBody(version), supersedes: Goal.Id);
        return Goal;
    }

    public JsonObject Order(string key, bool releasePlan = false, bool placed = false)
    {
        var body = new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = "Order " + key, ["intent"] = "Carry " + key + ".", ["scope"] = new JsonArray("fixture"),
            ["exitQuestion"] = "Is " + key + " done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("the work"),
        };
        if (releasePlan) body["releasePlan"] = new JsonObject { ["product"] = Product.Id, ["version"] = Version };
        if (placed) body["placement"] = new JsonObject { ["kind"] = "release", ["carrier"] = Carrier.Id, ["product"] = Product.Id, ["version"] = Version };
        return body;
    }

    /// <summary>The operator's acceptance of a record at its exact hash.</summary>
    public RecordEnvelope Accept(RecordEnvelope subject, string? basis = null) => Add("acceptance", new JsonObject
    {
        ["statement"] = "Accept the exact record.", ["basis"] = basis is null ? new JsonArray() : new JsonArray(basis), ["self"] = subject.By == Estate.OperatorId,
    }, subject: subject.Reference);

    /// <summary>docs pass --before --record: one verification of the product per check, for a version, superseding the earlier one.</summary>
    public RecordEnvelope Check(string method, string outcome, string version = Version, RecordEnvelope? supersedes = null, string summary = "Checked the version-tracking guides.", string? previous = null) =>
        Add("verification", new JsonObject
        {
            ["method"] = method, ["commit"] = Commit, ["command"] = $"vericairn docs pass {version} --before --documents docs/releases/documents.json",
            ["environment"] = new JsonObject { ["platform"] = "osx-arm64", ["release"] = version, ["previous"] = previous ?? ReleaseHistory.Joined(ReleaseHistory.Previous(Estate.Load(), Product.Id, version)) },
            ["role"] = "self", ["outcome"] = outcome, ["summary"] = summary,
            ["evidence"] = new JsonArray(), ["limitations"] = new JsonArray(),
        }, builder: true, subject: Product.Reference, supersedes: supersedes?.Id);

    public void GuidesChecked(string version = Version)
    {
        Check("vericairn.software/prior-version-tokens", "passed", version);
        Check("vericairn.software/preparation-wording", "passed", version);
    }

    /// <summary>An assignment of a work step to the builder and its completed result naming its outputs.</summary>
    public RecordEnvelope WorkStep(string step, params (string Name, string Hash, string? MediaType)[] outputs)
    {
        RecordEnvelope assignment = Add("assignment", new JsonObject
        {
            ["contributor"] = Estate.BuilderId, ["step"] = new JsonObject { ["key"] = step },
            ["brief"] = new JsonObject
            {
                ["outcome"] = "Carry step " + step + ".", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("fixture"), ["prohibitedEffects"] = new JsonArray("publication"),
                ["workspace"] = new JsonArray("the product's repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("the step's outputs"),
                ["reviewCriteria"] = new JsonArray("the outputs exist"), ["recovery"] = "Carry it again.",
            },
        }, subject: Carrier.Reference);
        RecordEnvelope attempt = Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(Estate.Clock), ["workspace"] = "the product's repository, a work branch", ["note"] = "Carry the step." }, builder: true, subject: assignment.Reference);
        var named = new JsonArray();
        foreach ((string name, string hash, string? media) in outputs)
        {
            var output = new JsonObject { ["name"] = name, ["hash"] = hash };
            if (media is not null) output["mediaType"] = media;
            named.Add(output);
        }

        return Add("result", new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Carried the step.", ["outputs"] = named, ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Hand it on.",
        }, builder: true, subject: attempt.Reference);
    }

    /// <summary>release submit's intent, recorded before its dispatch.</summary>
    public RecordEnvelope Intent(string version = Version) => Add("operation", new JsonObject
    {
        ["provider"] = "test-releases", ["action"] = "dispatch-release-workflow", ["identity"] = $"test-releases:{Repository}:v{version}",
        ["intent"] = $"Build release {version} into a draft; publication stays the operator's manual step.",
        ["target"] = new JsonObject { ["repository"] = Repository, ["version"] = version, ["product"] = Product.Id }, ["status"] = "not-started", ["evidence"] = new JsonArray(),
    });

    /// <summary>A revision of an operation with another status.</summary>
    public RecordEnvelope Settle(RecordEnvelope operation, string status, bool builder = false)
    {
        JsonObject body = JsonNode.Parse(operation.Body.GetRawText())!.AsObject();
        body["status"] = status;
        if (status is "dispatched" or "uncertain" or "observed-success" or "known-failure") body["dispatchedAt"] ??= RecordParser.FormatTimestamp(Estate.Clock);
        if (status is "observed-success" or "known-failure") body["observed"] = new JsonObject { ["summary"] = "Observed at the provider.", ["at"] = RecordParser.FormatTimestamp(Estate.Clock) };
        if (status == "dispatched" && operation.Body.TryGetProperty("status", out var previous) && previous.GetString() == "uncertain") body["reconciliation"] = "Read the release record: no run started.";
        return Add("operation", body, builder: builder, subject: operation.Subject, supersedes: operation.Id);
    }

    /// <summary>release observe's candidate.</summary>
    public RecordEnvelope Candidate(string version = Version, string? channel = null) =>
        Add("release", ReleaseLineFixture.Body(version, channel: channel), subject: Product.Reference);

    /// <summary>The Reviewer's review of the exact candidate.</summary>
    public RecordEnvelope ReleaseReview(RecordEnvelope candidate, string outcome = "pass") =>
        ByReviewer("review", new JsonObject { ["independence"] = "independent-agent", ["outcome"] = outcome, ["summary"] = "Reviewed the exact candidate.", ["findings"] = new JsonArray() }, candidate.Reference);

    /// <summary>The operator's observation of the publication: the published revision of the candidate.</summary>
    public RecordEnvelope Published(RecordEnvelope candidate)
    {
        JsonObject body = JsonNode.Parse(candidate.Body.GetRawText())!.AsObject();
        body["status"] = "published";
        body["publishedAt"] = RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1));
        body["url"] = "https://example.invalid/releases/" + body["version"]!.GetValue<string>();
        return Add("release", body, subject: Product.Reference, supersedes: candidate.Id);
    }

    /// <summary>release withdraw: the withdrawn revision and the draft's deletion intent, together.</summary>
    public (RecordEnvelope Withdrawn, RecordEnvelope Intent) Withdraw(RecordEnvelope candidate)
    {
        JsonObject body = JsonNode.Parse(candidate.Body.GetRawText())!.AsObject();
        body["status"] = "withdrawn";
        body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "The candidate is replaced.", ["hash"] = candidate.ComputedHash });
        RecordEnvelope withdrawn = Add("release", body, builder: true, subject: Product.Reference, supersedes: candidate.Id);
        string version = body["version"]!.GetValue<string>();
        RecordEnvelope intent = Add("operation", new JsonObject
        {
            ["provider"] = "test-releases", ["action"] = "delete-release-draft", ["identity"] = $"test-releases:{Repository}:v{version}:delete-draft",
            ["intent"] = $"Delete the draft of release {version}; deletion stays the operator's manual step.",
            ["target"] = new JsonObject { ["repository"] = Repository, ["version"] = version, ["product"] = Product.Id }, ["status"] = "not-started",
            ["evidence"] = new JsonArray(new JsonObject { ["summary"] = "The candidate is replaced.", ["hash"] = candidate.ComputedHash }),
        }, builder: true, subject: withdrawn.Reference);
        return (withdrawn, intent);
    }

    /// <summary>goal step's operator request for a reserved step, carrying the command its position gives now.</summary>
    public RecordEnvelope Request(string step, bool builder = false, string? command = null, string? by = null)
    {
        FlowPosition.StepInstance instance = Step(step);
        command ??= instance.Command ?? (builder || by is not null ? "a later request" : throw new InvalidOperationException($"Step {step} has no command now: {string.Join("; ", instance.Reasons)}"));
        string author = by ?? (builder ? Estate.BuilderId : Estate.OperatorId);
        var body = new JsonObject
        {
            ["to"] = Estate.OperatorId, ["askedBy"] = author, ["action"] = "Carry the reserved act of step " + step + ".", ["why"] = "The step is next.",
            ["recommendation"] = "Run the command yourself.", ["consequence"] = "The step waits.", ["kind"] = instance.Step.Reserved == "publish" ? "publication" : "action",
            ["destination"] = "the reserved act of step " + step,
            ["content"] = new JsonArray(new JsonObject { ["kind"] = "command", ["text"] = command, ["hash"] = ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(command)) }),
            ["flowStep"] = new JsonObject { ["goal"] = Estate.Load().Chain(Goal)[0], ["key"] = step },
        };
        return by is null ? Add("operator-request", body, builder: builder, subject: Carrier.Reference) : Estate.Add("operator-request", by, body.ToJsonString(), Carrier.Reference);
    }

    /// <summary>The operator's own close of a request as done, with the observed link of a publication.</summary>
    public RecordEnvelope Done(RecordEnvelope request) => Add("operator-request-close", new JsonObject
    {
        ["outcome"] = "done", ["link"] = "https://example.invalid/observed",
    }, subject: request.Reference);

    /// <summary>An agent's close of a request citing the operator's words: kept, and never the operator's act.</summary>
    public RecordEnvelope DoneByAgent(RecordEnvelope request) => Add("operator-request-close", new JsonObject
    {
        ["outcome"] = "done", ["link"] = "https://example.invalid/observed",
        ["operatorWords"] = new JsonObject { ["by"] = Estate.OperatorId, ["statement"] = "I deleted it.", ["source"] = "Operator conversation, message 2", ["at"] = RecordParser.FormatTimestamp(Estate.Clock) },
    }, builder: true, subject: request.Reference);

    /// <summary>A living document of the product whose release trigger a publication makes due.</summary>
    public RecordEnvelope Guide(string path = "docs/guide.md")
    {
        Estate.WriteFile(path, "# Guide\n");
        return Add("living-document", JsonNode.Parse($$"""{"path":"{{path}}","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"warn","product":"{{Product.Id}}"}],"summary":"Follows the releases."}""")!.AsObject());
    }

    /// <summary>The operator's review clearing a living document as it is.</summary>
    public RecordEnvelope Clear(RecordEnvelope declaration) => Add("document-review", JsonNode.Parse(
        $$"""{"outcome":"current","content":{"hash":"{{ContentHash.Sha256("# Guide\n"u8)}}","bytes":8},"answers":[],"independence":"operator","summary":"Read it.","findings":[],"checks":[]}""")!.AsObject(), subject: declaration.Reference);

    public FlowPosition.Position Position() => FlowPosition.Derive(Estate.Load(), Goal, Estate.Clock);

    public FlowPosition.StepInstance Step(string key) => Assert.Single(Position().Steps, s => s.Name == key);

    public string State(string key) => Step(key).State;

    public void AssertValid()
    {
        EstateModel model = Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(f => f.Severity == Severity.Error)));
    }

    public static string Hash(char c) => FlowFixture.Hash(c);

    /// <summary>The first-party extension's file, which Vericairn's estate keeps under docs/templates, read from the product's fixtures.</summary>
    public static string FirstPartyPath => Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "flows", "vericairn-first-party-release-1.0.0.json");

    /// <summary>Copies the first-party extension byte for byte into an estate's docs/templates, as Vericairn's estate keeps it.</summary>
    public static TemplateDefinition InstallFirstParty(TestEstate estate)
    {
        string text = File.ReadAllText(FirstPartyPath);
        estate.WriteFile("docs/templates/vericairn-first-party-release-1.0.0.json", text);
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(text);
        return TemplateDefinition.Read(json.RootElement);
    }

    public void Dispose() => Records.Dispose();
}
