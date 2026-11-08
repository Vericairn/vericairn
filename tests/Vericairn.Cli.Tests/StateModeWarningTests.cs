// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>A test that changes the process's Vericairn home and how modes are set runs alone.</summary>
[CollectionDefinition("state modes", DisableParallelization = true)]
public sealed class StateModesIsolation;

/// <summary>
/// Per-user state on a filesystem that keeps no Unix modes is used as it is: a command says so once, on standard error and never in its
/// output, and <c>doctor</c> names it.
/// </summary>
[Collection("state modes")]
public sealed class StateModeWarningTests
{
    private const UnixFileMode Wide = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    [Fact]
    public void ACommandAndDoctorSayOnceThatTheHomeCannotBeRestricted()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps per-user state under the profile's own permissions.");
            return;
        }

        string scratch = CliHarness.TempDirectory("state-modes");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            string root = Path.Combine(scratch, "estate");
            Directory.CreateDirectory(root);
            PreparedEstate prepared = EstateInitializer.Prepare(root, new EstateInitialization("Modes", "modes", "Per-user state modes.", "Operator", "Example", null, "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero)));
            string manifest = Path.Combine(root, EstateLayout.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.WriteAllBytes(manifest, prepared.ManifestBytes);
            RecordAuthor.Write(root, prepared.Operator);
            RecordAuthor.Write(root, prepared.Estate);

            string home = Path.Combine(scratch, "home");
            Directory.CreateDirectory(home);
            File.SetUnixFileMode(home, Wide);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            // A filesystem that accepts a mode change that does not hold, as exFAT on macOS and WSL's /mnt do.
            OwnerOnlyState.ModeSetter = (_, _) => { };

            CliHarness.Outcome first = CliHarness.Run("--estate", root, "--json", "status");
            Assert.Equal(0, first.Code);
            string warning = "warning: " + OwnerOnlyState.Warning(home);
            Assert.Equal(1, Occurrences(first.Err, warning));
            Assert.DoesNotContain(home, first.Out, StringComparison.Ordinal);

            // Said once per process, and the canonical status is the same.
            CliHarness.Outcome second = CliHarness.Run("--estate", root, "--json", "status");
            Assert.Equal(0, second.Code);
            Assert.Equal(0, Occurrences(second.Err, warning));
            Assert.Equal(first.Out, second.Out);

            CliHarness.Outcome doctor = CliHarness.Run("--estate", root, "doctor");
            Assert.Equal(0, doctor.Code);
            Assert.Contains($"state     {OwnerOnlyState.Warning(home)}\n", doctor.Out, StringComparison.Ordinal);
            Assert.Equal(1, Occurrences(doctor.Out, "state     "));
            Assert.Equal(Wide, File.GetUnixFileMode(home));

            // Where modes hold, doctor says nothing of the state, and the home is owner-only.
            OwnerOnlyState.ModeSetter = null;
            CliHarness.Outcome restricted = CliHarness.Run("--estate", root, "doctor");
            Assert.DoesNotContain("state     ", restricted.Out, StringComparison.Ordinal);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(home));
        }
        finally
        {
            OwnerOnlyState.ModeSetter = null;
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
