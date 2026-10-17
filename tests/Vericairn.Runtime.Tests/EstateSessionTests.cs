// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Service;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class EstateSessionTests
{
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
