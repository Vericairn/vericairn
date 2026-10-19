// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Portability;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class EstateSessionTests
{
    [Fact]
    public void UnchangedHistoryIsParsedOnceWhileEveryLoadRevalidatesItsReferences()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Cache.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        using EstateSession session = EstateSession.Open(estate.Root);
        EstateModel first = session.Load();
        EstateModel second = session.Load();
        Assert.NotSame(first, second);
        Assert.Same(first.ById[order.Id], second.ById[order.Id]);
        Assert.Equal(first.Findings, second.Findings);

        // Reusing parsed bytes must not reuse semantic verdicts. The author disappears while the order is unchanged.
        File.Delete(Path.Combine(estate.Root, estate.Builder.ExpectedPath));
        EstateModel missingAuthor = session.Load();
        Assert.Same(first.ById[order.Id], missingAuthor.ById[order.Id]);
        Assert.True(missingAuthor.Findings.HasErrors);
        Assert.Equal(estate.Load().Findings, missingAuthor.Findings);
    }

    [Fact]
    public void RewrittenRecordsAndParseFailuresReplaceTheCachedOriginalAndItsFindings()
    {
        using var estate = new TestEstate();
        using EstateSession session = EstateSession.Open(estate.Root);
        EstateModel original = session.Load();
        string path = Path.Combine(estate.Root, estate.Builder.ExpectedPath);
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllText(path, "not JSON");
        EstateModel broken = session.Load();
        Assert.Contains(broken.Findings, f => f.Code == "record.json");
        Assert.Equal(broken.Findings, session.Load().Findings);
        Assert.Equal(4, session.ParsedFiles);
        File.WriteAllBytes(path, bytes);
        EstateModel restored = session.Load();
        Assert.Equal(original.Findings, restored.Findings);
        Assert.Equal(5, session.ParsedFiles);

        // Same-size tampering must be rehashed, not mistaken for the original because it occupies the same path.
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, System.Text.Encoding.UTF8.GetString(bytes).Replace("Test Builder", "Fake Builder", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(path, timestamp.AddSeconds(2));
        EstateModel changed = session.Load();
        Assert.Equal(estate.Load().Findings, changed.Findings);
        Assert.Contains(changed.Findings, f => f.Code == "record.hash-mismatch");
        Assert.Equal(6, session.ParsedFiles);
    }

    [Fact]
    public void CachedParsingStillReadsTheManifestAndChecksCurrentOutputBytes()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Cache.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        const string brief = """{"outcome":"An output.","sources":[],"permittedChanges":["docs"],"prohibitedEffects":[],"workspace":["a local clone"],"limits":[],"expectedOutputs":["output"],"reviewCriteria":["hash checked"],"recovery":"Read the result."}""";
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{brief}}}""", order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"a local clone","note":"Build."}""", assignment.Reference);
        estate.WriteFile("docs/output.txt", "original");
        string path = Path.Combine(estate.Root, "docs", "output.txt");
        string hash = ContentHash.Sha256File(path);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, $$"""{"outcome":"completed","summary":"Built.","outputs":[{"name":"output","hash":"{{hash}}","location":"docs/output.txt"}],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        using EstateSession session = EstateSession.Open(estate.Root);
        EstateModel first = session.Load();
        Assert.False(first.Findings.HasErrors, string.Join('\n', first.Findings));
        Assert.DoesNotContain(first.Findings, f => f.Code == "result.output-changed");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "modified");
        File.SetLastWriteTimeUtc(path, timestamp);
        EstateModel changed = session.Load();
        Assert.Same(first.ById[result.Id], changed.ById[result.Id]);
        Assert.Contains(changed.Findings, f => f.Code == "result.output-changed");
        Assert.Equal(estate.Load().Findings, changed.Findings);
        estate.MoveTo(2);
        Assert.Equal(2, session.Load().Manifest!.RecordFormat);
    }

    [Fact]
    public void LiveSnapshotsNoticeOtherConnectionsAndKeepPublicFileBuffersIndependent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vericairn-snapshot-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(directory);
        try
        {
            using var live = new LiveStore(Path.Combine(directory, "live.db"));
            using var other = new LiveStore(live.Path);
            live.Put("one", [1, 2, 3]);
            LiveStore.FileSnapshot first = live.Snapshot();
            Assert.Same(first, live.Snapshot());
            live.Files()[0].Bytes[0] = 9;
            Assert.Equal(new byte[] { 1, 2, 3 }, first.Files[0].Bytes);
            other.Put("one", [4, 5, 6]);
            LiveStore.FileSnapshot second = live.Snapshot();
            Assert.NotSame(first, second);
            Assert.Equal(new byte[] { 4, 5, 6 }, second.Files[0].Bytes);
            other.Remove("one");
            Assert.Empty(live.Snapshot().Files);
            live.Put("two", [7]);
            Assert.Equal("two", Assert.Single(live.Snapshot().Files).Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RepositorySyncedSessionMirrorsTheRecordFilesAndNeverWritesIntoTheRepository()
    {
        using var estate = new TestEstate();
        using EstateSession session = EstateSession.Open(estate.Root);
        Assert.Equal("repository-synced", session.Profile);
        Assert.StartsWith(VericairnHome.Path(), session.Live.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(estate.Root, session.Live.Path, StringComparison.Ordinal);
        EstateModel first = session.Load();
        Assert.False(first.Findings.HasErrors, string.Join("\n", first.Findings));
        Assert.Equal(3, session.Live.Count());

        // A file added behind the runtime's back (a git pull) is picked up; a deleted one is dropped.
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Sync.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        EstateModel second = session.Load();
        Assert.Equal(4, session.Live.Count());
        Assert.Contains(second.Records, r => r.Id == order.Id);
        File.Delete(Path.Combine(estate.Root, order.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.DoesNotContain(session.Load().Records, r => r.Id == order.Id);
        Assert.Equal(3, session.Live.Count());

        // Writing through the session lands in the repository as a file, and a discarded record leaves no trace.
        using JsonDocument body = JsonDocument.Parse("""{"status":"active","note":"Through the session."}""");
        AuthoredRecord status = RecordAuthor.Create(new RecordDraft("work-status", estate.BuilderId, body.RootElement, new RecordReference("work-order", order.Id, null), null, estate.Clock.AddSeconds(5)));
        session.Write(status);
        Assert.True(File.Exists(Path.Combine(estate.Root, status.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
        session.Discard(status);
        Assert.False(File.Exists(Path.Combine(estate.Root, status.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(session.Live.Has(status.RelativePath));
        Assert.Equal("file", session.Objects.Kind);
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(estate.Root), e => e.EndsWith(".db", StringComparison.Ordinal));
    }

    [Fact]
    public void LocalOnlySessionKeepsRecordsInTheLiveStoreAndNoRecordFileExists()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-local-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(root);
        try
        {
            var initialization = new EstateInitialization("Local", "local", "Local-only profile.", "Operator", "Example", null, "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), "local-only");
            PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
            string manifestPath = Path.Combine(root, EstateLayout.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllBytes(manifestPath, prepared.ManifestBytes);
            using EstateSession session = EstateSession.Open(root);
            Assert.Equal("local-only", session.Profile);
            session.Write(prepared.Operator);
            session.Write(prepared.Estate);
            EstateModel model = session.Load();
            Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
            Assert.Equal(2, model.Records.Count);
            Assert.False(Directory.Exists(Path.Combine(root, EstateLayout.RecordsDirectory)));
            Assert.Equal("sqlite", session.Objects.Kind);
            Assert.Throws<IOException>(() => session.Write(prepared.Estate));
            Assert.Equal(["estate"], Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToArray());
            Assert.False(EstateChecks.Run(model).HasErrors);

            // Hand-over uses the session's original bytes even when there are no repository record files.
            using (EstateService service = EstateService.Open(root))
            {
                HandoffPackage package = service.Handoff().Package!;
                Assert.Equal(2, package.Payload.GetProperty("originals").GetArrayLength());
                Assert.Equal("local-only", package.Payload.GetProperty("pin").GetProperty("profile").GetString());
                foreach (JsonElement source in package.Payload.GetProperty("originals").EnumerateArray())
                {
                    Assert.Equal(Convert.FromBase64String(source.GetProperty("content").GetString()!), service.ContextRead(source.GetProperty("pin").GetString()!).Content);
                }
                Assert.False(service.ResumeHandoff(package).Json!.Value.GetProperty("changed").GetBoolean());
                Assert.False(Directory.Exists(Path.Combine(root, EstateLayout.RecordsDirectory)));
            }

            // Export changes storage, not history. The manifest's profile is an intentional
            // policy-basis input; its changed digest must be the only status difference.
            string archive = root + "-copy.zip", restoredRoot = root + "-restored";
            try
            {
                using EstateService source = EstateService.Open(root);
                StatusResponse before = source.Status();
                PolicyResponse sourcePolicies = source.Policies();
                EstateExport.Write(session, archive);
                EstateExport.Restore(archive, restoredRoot);
                using EstateService restored = EstateService.Open(restoredRoot);
                Assert.Equal("local-only", source.Session.Profile);
                Assert.Equal("repository-synced", restored.Session.Profile);
                PolicyResponse restoredPolicies = restored.Policies();
                Assert.Equal(sourcePolicies.Basis.RecordDigest, restoredPolicies.Basis.RecordDigest);
                Assert.NotEqual(sourcePolicies.Basis.Digest, restoredPolicies.Basis.Digest);
                Assert.Equal(before.Json.GetRawText().Replace(sourcePolicies.Basis.Digest, restoredPolicies.Basis.Digest, StringComparison.Ordinal), restored.Status().Json.GetRawText());
                Assert.Equal(before.Json.GetRawText(), source.Status().Json.GetRawText());
            }
            finally
            {
                File.Delete(archive);
                if (Directory.Exists(restoredRoot)) Directory.Delete(restoredRoot, recursive: true);
            }

            // Local-only roots refer to one canonical store, so they also share its batch journals.
            string alias = root + "-alias";
            try
            {
                Directory.CreateDirectory(Path.Combine(alias, "estate"));
                File.WriteAllBytes(Path.Combine(alias, EstateLayout.ManifestPath), prepared.ManifestBytes);
                using EstateSession other = EstateSession.Open(alias);
                Assert.Equal(BatchJournal.Directory(session), BatchJournal.Directory(other));
                BatchJournal journal = BatchJournal.Begin(session, "Local-only storage identity", null, [prepared.Operator]);
                Assert.Equal(journal.Id, Assert.Single(BatchJournal.All(other).Journals).Id);
                journal.Close();
                Assert.Empty(BatchJournal.All(session).Journals);
            }
            finally
            {
                Directory.Delete(alias, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
