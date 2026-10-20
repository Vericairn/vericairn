// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>The same isolated adoption scenario is driven by embedded, CLI and API surfaces.</summary>
public static class PolicyAdoptionJourney
{
    public class Surface(IEstateApi api) : IDisposable
    {
        public virtual WriteOutcome DeclarePolicy(PolicyChangeRequest request) => api.PolicyDeclare(request);
        public virtual WriteOutcome Except(string key, PolicyChangeRequest request) => api.PolicyExcept(key, request);
        public virtual WriteOutcome WritingAdd(PolicyChangeRequest request) => api.WritingExceptionAdd(request);
        public virtual WriteOutcome Result(ResultRequest request) => api.Result(request);
        public virtual WriteOutcome Review(ResultReviewRequest request) => api.ReviewResult(request);
        public virtual PolicyResponse Evaluate(string? at = null, string? release = null) => api.PolicyEvaluate(at, release);
        public virtual WritingExceptionsResponse Writing() => api.WritingExceptions();
        public virtual ReleaseResponse Submit(ReleaseSubmitRequest request) => api.ReleaseSubmit(request);
        public virtual FindingsResponse Check() => api.Check();
        public virtual StatusResponse Status() => api.Status();
        public virtual ProgressResponse Progress() => api.Progress();
        public virtual void Dispose() { GC.SuppressFinalize(this); }
    }

    public static void Run(Func<EstateService, Surface> connect, int recheckSeconds = 0)
    {
        using var f = new Fixture();
        using Surface surface = connect(f.Api);
        f.Surface = surface;
        f.Adopt();
        f.InitialViewsAndWriting();
        RecordEnvelope publication = f.PublicationAndDevlog(recheckSeconds);
        f.CompleteInitialDuties(publication);
        f.CarriedDebt();
        f.Records.AssertValid();
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";
        private static readonly DateTimeOffset Start = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        internal PolicyFixture Records { get; } = new();
        internal EstateService Api { get; }
        internal Surface Surface { get; set; } = null!;
        private Clock Time { get; } = new();
        private readonly IPolicyRepositorySource? previousSource = PolicySources.RepositorySource;
        private readonly Dictionary<string, RecordEnvelope> policies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> checks = new(StringComparer.Ordinal)
        {
            ["dependency-review"] = ["inventory-complete", "versions-reviewed", "findings-recorded"],
            ["advisory-review"] = ["sources-consulted", "inventory-matched", "findings-recorded"],
            ["workflow-pin-review"] = ["workflows-covered", "pins-reviewed", "findings-recorded"],
            ["export-restore-verification"] = ["export-verified", "restore-verified", "authority-not-restored"],
            ["release-export-restore"] = ["published-build-used", "export-verified", "restore-verified"],
            ["scratch-cleanup"] = ["inventory-reviewed", "removals-confirmed"],
            ["withdrawn-artifact-cleanup"] = ["withdrawals-covered", "removals-confirmed"],
        };
        private readonly RecordEnvelope product;
        private readonly RecordEnvelope repository;
        private readonly RecordEnvelope reviewer;
        private readonly RecordEnvelope independent;
        private readonly RecordEnvelope spellingReview;
        private readonly JourneyProvider provider;
        private string Operator => Records.Estate.OperatorId;
        private EstateModel Model => Api.Session.Load();

        internal Fixture()
        {
            product = Records.Add("product", new()
            {
                ["name"] = "Fixture", ["slug"] = "fixture", ["summary"] = "A synthetic product.",
                ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
                ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture interface." }),
                ["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = "github-releases", ["settings"] = new JsonObject { ["repository"] = "Example/fixture" } } },
            });
            repository = Records.Add("resource", new() { ["kind"] = "repository", ["name"] = "Fixture product source", ["role"] = "Retain exact inventory bytes.", ["location"] = new JsonObject { ["github"] = "Example/fixture" }, ["visibility"] = "public" }, subject: product.Reference);
            reviewer = Agent("Security reviewer");
            independent = Agent("Independent reviewer");
            Api = EstateService.Open(Records.Estate.Root);
            foreach (string principal in new[] { Operator, Records.Estate.BuilderId, reviewer.Id, independent.Id })
            {
                RecordWritten device = Ok(Api.DeviceCreate(new(principal, "Fixture signer", "file")));
                RecordEnvelope held = Model.ById[device.Id];
                Records.Estate.SetClock(held.AtTime.AddSeconds(1));
                Records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Admit an isolated fixture signer." }, subject: held.Reference);
            }
            // All scenario events use explicit time after the signer bootstrap; no deadline depends on wall time.
            At(Start.AddDays(-1));
            Api.PolicyClock = Time;
            provider = new JourneyProvider(Time);
            var providers = new ProviderRegistry();
            providers.Register(provider);
            Api.Providers = providers;
            PolicySources.Register(ReferenceCoverage.Registration(PolicySources.Digest(typeof(ReferenceCoverage).Assembly)));
            PolicySources.RepositorySource = new Sources(repository.Id);
            Records.Estate.WriteFile("design/catalog.md", "Rapid7\n");
            Records.Estate.WriteFile("design/catalog/information-technology-services-and-operations.md", "Rapid7\n");
            Records.Estate.WriteFile("design/catalog/public-sector.md", "Open311 Open311 Open311 Open311\n");
            Records.Estate.WriteFile("design/catalog/transportation-and-logistics.md", "project44\n");
            Records.Estate.WriteFile("design/vocabulary.md", """
                # Vocabulary

                | Term | Meaning | Not to be confused with | Owning document and section | Status |
                | --- | --- | --- | --- | --- |
                | Release | A product version. | A candidate | `design/releases.md` § 1 | current |

                ## Generated reference coverage

                | Category | Reference | Term |
                | --- | --- | --- |
                | command | `vericairn status` | Undefined status |
                | record-type | `release` | Release |
                | api-operation | `GET /v1/status` | Undefined status |
                """ + "\n");
            RecordEnvelope result = Records.Result();
            spellingReview = Records.Add("review", new() { ["independence"] = "operator", ["outcome"] = "pass", ["summary"] = "The historical abbreviation efea482 is retained.", ["findings"] = new JsonArray() }, subject: result.Reference);
            Advance();
        }

        private RecordEnvelope Agent(string name) => Records.Add("principal", new() { ["kind"] = "agent", ["name"] = name, ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Example", ["product"] = "Fixture agent" } });
        private void At(DateTimeOffset at) { Time.Now = at; Records.Estate.SetClock(at); }
        private void Advance() => At(Model.Records.Max(r => r.AtTime).AddSeconds(1));
        private static string Stamp(DateTimeOffset at) => RecordParser.FormatTimestamp(at);
        private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        private JsonObject Definition(string key, string kind, JsonObject parameters, string level = "warn")
        {
            JsonObject body = Records.PolicyBody(key, duty: false);
            body["owner"] = key is "ask-age" or "obligation-gates" ? Operator : Records.Estate.BuilderId;
            body["level"] = level;
            body["startsAt"] = Stamp(Start);
            body["rules"] = new JsonArray(new JsonObject { ["key"] = "rule", ["kind"] = kind, ["version"] = 1, ["parameters"] = parameters });
            return body;
        }
        private void Declare(JsonObject body)
        {
            RecordEnvelope approval = Records.Approve(body);
            body["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
            Advance();
            JsonElement json = PolicyFixture.Element(body);
            RecordWritten written = Ok(Surface.DeclarePolicy(new(Operator, json, PolicyBasis.Hash(json))));
            policies.Add(body["key"]!.GetValue<string>(), Model.ById[written.Id]);
            Advance();
        }

        internal void Adopt()
        {
            Declare(Definition("release-devlog", "vericairn.estate/release-entry", new() { ["product"] = product.Id, ["entryKind"] = "devlog", ["source"] = "estate-draft/1", ["directory"] = "docs/knowledge/devlog", ["fromPublication"] = "after-declaration", ["review"] = "independent" }, "refuse"));
            Declare(Definition("register-coverage", "vericairn.software/reference-coverage", new() { ["product"] = product.Id, ["repository"] = repository.Id, ["referenceManifest"] = "reference-inventory.json", ["registerRepository"] = Records.Estate.Estate.Id, ["registerPath"] = "design/vocabulary.md", ["categories"] = new JsonArray("command", "record-type", "api-operation") }));
            Declare(Definition("planned-placement", "vericairn.estate/placement", new() { ["backlogs"] = new JsonArray(new JsonObject { ["key"] = "future-work", ["title"] = "Future work", ["owner"] = Records.Estate.BuilderId, ["reason"] = "Await a release plan." }, new JsonObject { ["key"] = "awaiting-decision", ["title"] = "Awaiting decision", ["owner"] = Operator, ["reason"] = "Await the operator's choice." }) }));
            foreach (var (key, kind, days, level) in new[] { ("planned-age", "planned-without-release", 30, "remind"), ("review-wait", "result-awaiting-review", 7, "warn"), ("assignment-start", "assignment-without-attempt", 2, "remind"), ("ask-age", "open-ask", 7, "remind"), ("obligation-gates", "obligation-past-gate", 0, "warn"), ("retirement-follow-through", "retirement-past-condition", 7, "remind") })
            {
                JsonObject p = new() { ["class"] = kind, ["maxAgeDays"] = days };
                if (key == "review-wait") p["reviewRequirement"] = "independent";
                if (key is "obligation-gates" or "retirement-follow-through") p["gates"] = new JsonArray();
                Declare(Definition(key, "vericairn.estate/staleness", p, level));
            }
            Declare(Definition("work-key-families", "vericairn.estate/work-key-family", new() { ["createdFrom"] = Stamp(Start), ["families"] = new JsonArray(new JsonObject { ["prefix"] = "TASK", ["separator"] = "-", ["digits"] = 1, ["suffixes"] = new JsonArray("") }) }));
            JsonObject revision = Definition("design-revisions", "vericairn.estate/revision-line", new() { ["selectors"] = new JsonArray("design/*.md"), ["style"] = "estate-design/1" });
            revision["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = Records.Estate.Estate.Id, ["selectors"] = new JsonArray("design/*.md") };
            Declare(revision);
            Declare(Definition("work-state-consistency", "vericairn.estate/work-state-consistency", new() { ["cases"] = new JsonArray("accepted-still-open", "published-plan-still-open") }));
            foreach (var (key, names) in checks)
            {
                bool release = key == "release-export-restore";
                bool productSubject = release || key is "dependency-review" or "advisory-review";
                JsonArray clauses = new();
                if (key is "export-restore-verification" or "release-export-restore")
                {
                    JsonObject clause = new() { ["key"] = "restore", ["kind"] = "verification", ["method"] = "estate-export-restore", ["subject"] = release ? "duty-subject" : "result" };
                    if (release) clause["commitSource"] = "publication";
                    clauses.Add(clause);
                }
                JsonObject duty = Definition(key, "vericairn.estate/recurring-duty", new()
                {
                    ["subject"] = PolicyFixture.Ref(productSubject ? product : Records.Estate.Estate, false),
                    ["schedule"] = release ? new JsonObject { ["kind"] = "release", ["product"] = product.Id } : new JsonObject { ["kind"] = "interval", ["unit"] = "months", ["every"] = 1, ["firstDueAt"] = Stamp(Start) },
                    ["graceDays"] = key is "advisory-review" or "scratch-cleanup" or "withdrawn-artifact-cleanup" ? 2 : 7,
                    ["renewal"] = "none", ["discharge"] = new JsonObject { ["checks"] = Strings(names), ["review"] = "independent", ["records"] = clauses },
                }, key == "scratch-cleanup" ? "remind" : "warn");
                if (productSubject) duty["appliesTo"] = new JsonObject { ["kind"] = "product", ["product"] = product.Id };
                Declare(duty);
            }
            Assert.Equal(19, policies.Count);
            At(Start);
            var initial = Surface.Evaluate(Stamp(Time.Now));
            Assert.Equal(3, Model.Manifest!.RecordFormat);
            Assert.Equal(6, initial.Findings.Count(f => f.Duty is { Completed: false } d && d.Occurrence.TryGetProperty("number", out var n) && n.GetInt32() == 0));
            Assert.All(initial.Findings.Where(f => f.Duty is not null), f => Assert.False(f.ReleaseHold));
        }

        internal void InitialViewsAndWriting()
        {
            PolicyResponse preview = Surface.Evaluate(Stamp(Time.Now));
            Assert.Contains(preview.Findings, f => f.PolicyKey == "register-coverage" && f.Item == "command:vericairn status" && f.Verdict == "unmet" && f.Level == "warn");
            Assert.Contains(preview.Findings, f => f.PolicyKey == "planned-placement" && f.Verdict == "unmet");
            Assert.Equal(8, Surface.Writing().Warnings.Count);
            RecordEnvelope grant = Records.Grant("manage-writing-exceptions", new() { ["scope"] = "manage-writing-exceptions", ["actions"] = new JsonArray("add", "remove"), ["tokens"] = new JsonArray("Rapid7", "Open311", "project44", "efea482") }, "2101-01-01T00:00:00Z");
            var scopes = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
            {
                ["Rapid7"] = Paths("design/catalog.md", "design/catalog/information-technology-services-and-operations.md"),
                ["Open311"] = Paths("design/catalog/public-sector.md"), ["project44"] = Paths("design/catalog/transportation-and-logistics.md"),
                ["efea482"] = new() { ["records"] = new JsonArray(new JsonObject { ["record"] = spellingReview.Id, ["field"] = "summary" }) },
            };
            foreach (var (token, scope) in scopes)
            {
                JsonObject body = PolicyFixture.WritingBody(grant); body["token"] = token; body["scope"] = scope;
                Advance();
                JsonElement json = PolicyFixture.Element(body);
                Ok(Surface.WritingAdd(new(Records.Estate.BuilderId, json, PolicyBasis.Hash(json))));
            }
            Assert.Empty(Surface.Writing().Warnings);
            Assert.Equal(8, Surface.Writing().ExceptedOccurrences.Count);
            Records.Estate.WriteFile("docs/outside.md", "Open311 open311 `Open311`\n");
            Assert.Equal(2, Surface.Writing().Warnings.Count);
            int before = Model.Records.Count;
            PolicyResponse recorded = Surface.Evaluate();
            Assert.Equal(recorded.Basis.Digest, Surface.Check().Policies!.Basis.Digest);
            Assert.Equal(recorded.Basis.Digest, Surface.Status().Json.GetProperty("policies").GetProperty("basis").GetProperty("digest").GetString());
            Assert.Equal(recorded.Basis.Digest, Surface.Progress().Json.GetProperty("policies").GetProperty("basis").GetProperty("digest").GetString());
            Assert.Equal(before, Model.Records.Count);
        }
        private JsonObject Paths(params string[] paths) => new() { ["paths"] = new JsonArray(new JsonObject { ["repository"] = Records.Estate.Estate.Id, ["selectors"] = Strings(paths) }) };

        internal RecordEnvelope PublicationAndDevlog(int recheckSeconds)
        {
            At(Start.AddHours(1));
            RecordEnvelope candidate = Candidate("0.8.0");
            Records.Add("acceptance", new() { ["statement"] = "Accept only this fixture candidate.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
            JsonObject published = PolicyFixture.Body(candidate); published["status"] = "published"; published["publishedAt"] = Stamp(Records.Estate.Clock); published["url"] = "https://example.invalid/fixture";
            RecordEnvelope publication = Records.Add("release", published, subject: candidate.Subject, supersedes: candidate.Id);
            RecordEnvelope next = Candidate("0.8.1");
            Advance();
            ReleaseSubmitRequest request = new(product.Id, "0.8.1", "Example/fixture", Operator, Commit: Commit, Expected: Model.Current("product").Single().ComputedHash);
            ReleaseResponse held = Surface.Submit(request);
            Assert.False(held.Ok);
            Assert.True(held.Policies is not null, JsonSerializer.Serialize(held, ProtocolJson.Options));
            Assert.Contains(held.Policies!.Findings, f => f.PolicyKey == "release-devlog" && f.Verdict == "unmet" && f.ReleaseHold);
            Assert.Equal(0, provider.Calls);
            Assert.Empty(Model.OfType("operation"));
            DateTimeOffset expiry = Time.Now.AddDays(30);
            JsonObject exception = new()
            {
                ["rule"] = "rule", ["reason"] = "A bounded fixture delay.", ["expiresAt"] = Stamp(expiry), ["state"] = "granted", ["basis"] = new JsonObject { ["decision"] = PolicyFixture.Ref(Records.Decision) },
                ["targets"] = new JsonArray(new JsonObject { ["kind"] = "publication", ["publication"] = PolicyFixture.Ref(publication), ["submission"] = new JsonObject { ["release"] = PolicyFixture.Ref(next), ["product"] = product.Id, ["version"] = "0.8.1" } }),
            };
            Ok(Surface.Except("release-devlog", new(Operator, PolicyFixture.Element(exception), policies["release-devlog"].ComputedHash)));
            PolicyFindingDto excepted = Assert.Single(Surface.Evaluate(Stamp(expiry.AddSeconds(-1)), next.Id).Findings, f => f.PolicyKey == "release-devlog");
            Assert.Equal("unmet", excepted.Verdict); Assert.Equal("excepted", excepted.Disposition); Assert.False(excepted.ReleaseHold);
            Assert.True(Assert.Single(Surface.Evaluate(Stamp(expiry), next.Id).Findings, f => f.PolicyKey == "release-devlog").ReleaseHold);
            Advance();
            string text = $$$"""
                ---
                kind: devlog
                id: fixture-entry
                title: Fixture publication
                summary: The fixture build was observed.
                audience: internal
                applicability: {versions: "0.8.0"}
                release: {product: {{{product.Id}}}, version: "0.8.0", publication: {id: {{{publication.Id}}}, hash: {{{publication.ComputedHash}}} } }
                sources: [{type: release, id: {{{publication.Id}}}, hash: {{{publication.ComputedHash}}} }]
                status: draft
                ---
                An account of the exact fixture publication.
                """ + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            string hash = Api.Session.Objects.Put(bytes);
            Records.Estate.WriteFile("docs/knowledge/devlog/fixture.md", text);
            RecordEnvelope pin = Records.Add("document-revision", new() { ["path"] = "docs/knowledge/devlog/fixture.md", ["title"] = "Fixture publication", ["revision"] = 1, ["hash"] = hash, ["bytes"] = bytes.Length, ["mediaType"] = "text/markdown", ["summary"] = "Retain the first exact draft." }, builder: true);
            Assert.True(Assert.Single(Surface.Evaluate(Stamp(expiry), next.Id).Findings, f => f.PolicyKey == "release-devlog").ReleaseHold);
            Records.Add("review", new() { ["independence"] = "operator", ["outcome"] = "pass", ["summary"] = "Reviewed the exact Internal draft.", ["findings"] = new JsonArray() }, subject: pin.Reference);
            Advance();
            Assert.Equal("satisfied", Assert.Single(Surface.Evaluate(Stamp(Time.Now), next.Id).Findings, f => f.PolicyKey == "release-devlog").Verdict);
            Api.ReleaseBoundary = _ => Time.Now = Time.Now.AddSeconds(recheckSeconds);
            provider.OnDispatch = () =>
            {
                Assert.Equal(0, provider.Calls);
                RecordEnvelope intent = Assert.Single(Model.OfType("operation"));
                Assert.Equal("not-started", EstateModel.BodyString(intent, "status"));
                Assert.Single(intent.Body.GetProperty("evidence").EnumerateArray());
            };
            ReleaseResponse admitted;
            try { admitted = Surface.Submit(request); }
            finally { Api.ReleaseBoundary = null; provider.OnDispatch = null; }
            Assert.True(admitted.Ok, admitted.Message);
            Assert.Equal(1, provider.Calls);
            Assert.Equal(2, Model.OfType("operation").Count());
            ReleaseBasisEvidence retained = Api.ReleaseBasis(Assert.Single(admitted.BasisEvidence!))!.Evidence;
            PolicyFindingDto age = Assert.Single(retained.Policies.Findings, f => f.PolicyKey == "planned-age" && f.Actual.EndsWith(" elapsed seconds.", StringComparison.Ordinal));
            PolicyFindingDto fresh = Assert.Single(admitted.Policies!.Findings, f => f.PolicyKey == age.PolicyKey && f.Item == age.Item && f.Target == age.Target);
            Assert.Equal(age.Verdict, fresh.Verdict);
            Assert.Equal(age.ReleaseHold, fresh.ReleaseHold);
            Assert.Equal(recheckSeconds == 0, age.Actual == fresh.Actual);
            Assert.Equal(retained.Basis.RecordDigest, admitted.Policies.Basis.RecordDigest);
            Assert.Equal(recheckSeconds == 0, retained.Basis.Digest == admitted.Policies.Basis.Digest);
            return publication;
        }
        private RecordEnvelope Candidate(string version)
        {
            JsonObject body = new() { ["version"] = version, ["repository"] = "Example/fixture", ["commit"] = Commit, ["status"] = "candidate", ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "fixture.zip", ["hash"] = PolicyBasis.Hash("fixture"), ["bytes"] = 7, ["platform"] = "any" }), ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "fixture", ["limitations"] = new JsonArray() };
            return Records.Add("release", body, subject: product.Reference);
        }

        internal void CompleteInitialDuties(RecordEnvelope publication)
        {
            At(Start.AddHours(2));
            string evidence = VerifyExport();
            foreach (string key in checks.Keys)
            {
                JsonObject occurrence = key == "release-export-restore" ? new() { ["kind"] = "release", ["publication"] = PolicyFixture.Ref(publication) } : new() { ["kind"] = "interval", ["number"] = 0 };
                Complete(key, [occurrence], evidence);
            }
            Assert.Equal(7, Surface.Evaluate(Stamp(Time.Now)).Findings.Count(f => f.Duty is { Completed: true }));
            Assert.Equal(2, Model.OfType("review").Count(r => r.By == reviewer.Id));
            Assert.Equal(5, Model.OfType("review").Count(r => r.By == independent.Id));
        }
        private string VerifyExport()
        {
            string scratch = Path.Combine(Path.GetTempPath(), "vericairn-adoption-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                string archive = Path.Combine(scratch, "estate.zip");
                string restored = Path.Combine(scratch, "restored");
                ExportSummary exported = EstateExport.Write(Api.Session, archive);
                ExportSummary replay = EstateExport.Restore(archive, restored);
                Assert.Equal(exported.InventoryHash, replay.InventoryHash);
                Assert.Equal(Model.Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal), EstateLoader.Load(restored).Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal));
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                    Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".key", StringComparison.Ordinal) || e.FullName.EndsWith(".pfx", StringComparison.Ordinal) || e.FullName.EndsWith(".db", StringComparison.Ordinal));
                using (var tampered = ZipFile.Open(archive, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry entry = tampered.Entries.First(e => e.FullName.StartsWith("docs/", StringComparison.Ordinal));
                    string name = entry.FullName; entry.Delete();
                    using var writer = new StreamWriter(tampered.CreateEntry(name).Open()); writer.Write("Changed bytes.");
                }
                Assert.Throws<InvalidOperationException>(() => EstateExport.Restore(archive, Path.Combine(scratch, "tampered")));
                return Api.Session.Objects.Put(Encoding.UTF8.GetBytes("Fixture export and isolated restore matched inventory " + exported.InventoryHash + "; changed bytes were rejected; no private key or runtime store was exported. Published-build behavior is simulated, not a live build qualification."));
            }
            finally { Vericairn.Runtime.Workers.WorkspaceCleanup.Delete(scratch); }
        }
        private void Complete(string key, JsonObject[] occurrences, string evidence)
        {
            RecordEnvelope policy = policies[key];
            JsonElement parameters = policy.Body.GetProperty("rules")[0].GetProperty("parameters");
            RecordEnvelope subject = Model.Current(parameters.GetProperty("subject").GetProperty("type").GetString()!).Single(r => Model.Chain(r).Contains(parameters.GetProperty("subject").GetProperty("id").GetString()!, StringComparer.Ordinal));
            JsonArray duties = new();
            JsonArray read = new();
            foreach (JsonObject occurrence in occurrences)
            {
                JsonArray proofs = new();
                if (key == "export-restore-verification") proofs.Add(new JsonObject { ["clause"] = "restore", ["kind"] = "result-verification" });
                if (key == "release-export-restore")
                {
                    RecordEnvelope proof = Verification(subject.Reference);
                    proofs.Add(new JsonObject { ["clause"] = "restore", ["records"] = new JsonArray(PolicyFixture.Ref(proof)) }); read.Add(PolicyFixture.Ref(proof));
                }
                duties.Add(new JsonObject { ["policy"] = PolicyFixture.Ref(policy), ["rule"] = "rule", ["subject"] = PolicyFixture.Ref(subject), ["occurrence"] = occurrence.DeepClone(), ["scope"] = new JsonObject { ["repositories"] = new JsonArray(new JsonObject { ["record"] = PolicyFixture.Ref(repository), ["commit"] = Commit }), ["objects"] = new JsonArray(evidence), ["records"] = new JsonArray() }, ["coverage"] = "Complete synthetic inventory; empty external-removal inventory; no live provider qualification.", ["proofs"] = proofs });
            }
            RecordEnvelope attempt = Records.BeginAttempt();
            JsonObject body = PolicyFixture.ResultBody(); body["duties"] = duties;
            body["outputs"] = new JsonArray(new JsonObject { ["name"] = "fixture-evidence", ["hash"] = evidence });
            body["checks"] = new JsonArray(checks[key].Select(name => (JsonNode)new JsonObject { ["name"] = name, ["outcome"] = "passed", ["summary"] = "Checked retained synthetic evidence." }).ToArray());
            Advance();
            RecordWritten written = Ok(Surface.Result(new(attempt.Id, attempt.By, PolicyFixture.Element(body), attempt.ComputedHash)));
            RecordEnvelope result = Model.ById[written.Id];
            Assert.DoesNotContain(Surface.Evaluate(Stamp(Time.Now)).Findings, f => f.PolicyKey == key && f.Duty is { Completed: true } d && occurrences.Any(o => o.ToJsonString() == JsonNode.Parse(d.Occurrence.GetRawText())!.ToJsonString()));
            if (key == "export-restore-verification")
            {
                Records.Estate.SetClock(Time.Now.AddSeconds(1));
                read.Add(PolicyFixture.Ref(Verification(result.Reference)));
            }
            Advance();
            string by = key is "advisory-review" or "workflow-pin-review" ? reviewer.Id : independent.Id;
            JsonObject review = new() { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Reviewed every named occurrence, exact result and proof.", ["findings"] = new JsonArray(), ["read"] = read };
            Ok(Surface.Review(new(result.Id, by, PolicyFixture.Element(review), result.ComputedHash)));
            Advance();
            foreach (JsonObject occurrence in occurrences)
            {
                DutyFindingDto completed = Assert.Single(Surface.Evaluate(Stamp(Time.Now)).Findings, f => f.PolicyKey == key && f.Duty is not null && PolicyRecordsEqual(f.Duty.Occurrence, occurrence)).Duty!;
                Assert.True(completed.Completed);
                Assert.Contains(completed.Completions, c => c.Result.Hash == result.ComputedHash);
            }
        }
        private static bool PolicyRecordsEqual(JsonElement value, JsonObject node) => JsonNode.DeepEquals(JsonNode.Parse(value.GetRawText()), node);
        private RecordEnvelope Verification(RecordReference subject)
        {
            JsonObject body = DutyProofEvaluationTests.VerificationBody("estate-export-restore"); body["commit"] = Commit;
            return Records.Add("verification", body, subject: subject);
        }
        internal void CarriedDebt()
        {
            At(Start.AddMonths(2).AddDays(8));
            PolicyFindingDto[] debt = Surface.Evaluate(Stamp(Time.Now)).Findings.Where(f => f.PolicyKey == "dependency-review" && f.Duty is { Missed: true, Completed: false }).ToArray();
            Assert.Equal([1, 2], debt.Select(f => f.Duty!.Occurrence.GetProperty("number").GetInt32()));
            string evidence = Api.Session.Objects.Put(Encoding.UTF8.GetBytes("Synthetic catch-up inventory covers both overdue intervals explicitly."));
            Complete("dependency-review", [new() { ["kind"] = "interval", ["number"] = 1 }, new() { ["kind"] = "interval", ["number"] = 2 }], evidence);
            var replay = Surface.Evaluate(Stamp(Time.Now));
            Assert.Equal(2, replay.Findings.Count(f => f.PolicyKey == "dependency-review" && f.Duty is { Missed: true, Completed: true, LateSeconds: > 0 }));
            Assert.Contains(replay.Findings, f => f.PolicyKey == "advisory-review" && f.Duty is { Missed: true, Completed: false });
            Assert.Contains(replay.Findings, f => f.PolicyKey == "dependency-review" && f.Duty?.DueAt == Stamp(Start.AddMonths(3)) && !f.Duty.Completed);
        }
        public void Dispose() { PolicySources.RepositorySource = previousSource; Api.Dispose(); Records.Dispose(); }
    }

    private sealed class Sources(string repository) : IPolicyRepositorySource
    {
        public PolicyRepository Read(EstateModel model, RecordEnvelope resource, string? commit) => ReferenceInventoryTests.Repository(commit: commit ?? "0123456789abcdef0123456789abcdef01234567") with { Identity = repository };
    }
    private sealed class JourneyProvider(Clock clock) : IReleaseProvider
    {
        internal int Calls { get; private set; }
        internal Action? OnDispatch { get; set; }
        public string Provider => "github-releases";
        public ProviderDescriptor Descriptor => new FakeReleaseProvider().Descriptor;
        public string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) => throw new InvalidOperationException("No provider resolution is allowed in this journey.");
        public DispatchReceipt Dispatch(DispatchRequest request) { OnDispatch?.Invoke(); Calls++; return new("fixture-run", "https://example.invalid/run", "Synthetic dispatch only.", RecordParser.FormatTimestamp(clock.Now)); }
        public RunObservation? ObserveRun(string destination, string runId) => throw new InvalidOperationException("No provider observations are allowed.");
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) => throw new InvalidOperationException("No provider observations are allowed.");
        public string ManualPublicationCommand(string destination, string tag) => throw new InvalidOperationException("No publication is allowed.");
    }
    private static RecordWritten Ok(WriteOutcome outcome) { Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome, ProtocolJson.Options)); return outcome.Written!; }
}

[Collection("policy-sources")]
public sealed class PolicyAdoptionJourneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InitialAdoptionRetainsEveryWarningReviewAndMissedOccurrence(int recheckSeconds) => PolicyAdoptionJourney.Run(api => new(api), recheckSeconds);
}
