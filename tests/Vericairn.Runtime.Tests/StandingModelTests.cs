// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A device's standing in a local-only estate comes from a model kept for the live store's snapshot: asking again reads nothing while
/// the store is unchanged, and a change committed by another runtime's connection, or written here, counts from the next call.
/// </summary>
public class StandingModelTests
{
    [Fact]
    public void ALocalOnlyEstatesStandingFollowsItsLiveStoreWithoutReadingItForEveryCall()
    {
        string root = Directory.CreateTempSubdirectory("vericairn-standing-local-").FullName;
        try
        {
            var initialization = new EstateInitialization("Local", "local", "Local-only profile.", "Operator", "Example", null, "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), "local-only");
            PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
            string manifest = Path.Combine(root, EstateLayout.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.WriteAllBytes(manifest, prepared.ManifestBytes);
            using (EstateSession session = EstateSession.Open(root))
            {
                session.Write(prepared.Operator);
                session.Write(prepared.Estate);
            }

            string op = prepared.Operator.Envelope.Id;
            using EstateService service = EstateService.Open(root);
            string first = Written(service.DeviceCreate(new DeviceCreateRequest(op, "First", "file")));
            Written(service.DeviceAdmit(new AdmitRequest(first, op, "The first authority.")));
            string second = Written(service.DeviceCreate(new DeviceCreateRequest(op, "Second", "file")));
            Written(service.DeviceAdmit(new AdmitRequest(second, op, "A second device.")));

            Assert.Equal("admitted", service.Standing(second)!.Admission);
            long loads = service.StandingLoads;
            long read = service.Session.Live.FilesRead;
            for (int i = 0; i < 25; i++)
            {
                Assert.Null(service.Standing(Ulid.NewUlid().Value));
                Assert.Equal("admitted", service.Standing(second)!.Admission);
            }

            Assert.Equal(loads, service.StandingLoads);
            Assert.Equal(read, service.Session.Live.FilesRead);

            // Another runtime on the same live store commits the revocation over its own connection.
            using (EstateService other = EstateService.Open(root))
            {
                Written(other.DeviceRevoke(new RevokeRequest(second, op, "Revoked by another runtime.")));
            }

            Assert.Equal("revoked", service.Standing(second)!.Admission);
            Assert.Equal(loads + 1, service.StandingLoads);

            // This runtime's own writes count from the next call too.
            string third = Written(service.DeviceCreate(new DeviceCreateRequest(op, "Third", "file")));
            Written(service.DeviceAdmit(new AdmitRequest(third, op, "A third device.")));
            Assert.Equal("admitted", service.Standing(third)!.Admission);
            Assert.Equal("revoked", service.Standing(second)!.Admission);
            Assert.Equal(loads + 2, service.StandingLoads);
        }
        finally
        {
            try
            {
                WorkspaceCleanup.Delete(root);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private static string Written(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message + "\n" + string.Join('\n', outcome.Problem?.Findings.Select(f => $"{f.Code}: {f.Message}") ?? []));
        return outcome.Written!.Id;
    }
}
