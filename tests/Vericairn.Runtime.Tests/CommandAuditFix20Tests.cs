// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class CommandAuditFix20Tests
{
    private static readonly string[] Wrappers = ["", "env ", "env -i ", "env -u UNUSED ", "sudo -u worker ", "nohup ", "time -p ", "nice -n 10 ",
        "timeout -s TERM -k 2s 10s ", "command -- ", "busybox ", "exec ", "/usr/bin/env ", "env MODE=test nohup nice -n 5 timeout 10s busybox "];

    private static readonly string[] Writes = [
        "sed -i 's/a/b/' {target}", "sed -ri 's/a/b/' {target}", "sed -Ei 's/a/b/' {target}", "sed -i '' 's/a/b/' {target}", "sed -i.bak -e 's/a/b/' {target}", "sed --in-place=.bak 's/a/b/' {target}",
        "perl -pi -e 's/a/b/' {target}", "perl -i.bak -pe 's/a/b/' {target}",
        "rsync -a source/ {target}", "rsync -t source/ {target}", "rsync -av --exclude cache source/ {target}",
        "tar -xf archive.tar -C {target}", "tar xf archive.tar --directory={target}", "tar -cf {target} source",
        "unzip archive.zip -d {target}", "unzip -q archive.zip -d{target}",
        "install -m 644 source {target}", "install -d {target}", "install -t {target} source", "install -t{target} source",
        "patch {target} -i changes.patch", "patch -d {target} -p1 < changes.patch", "patch -o {target} source changes.patch", "patch -d{target} -p1 < changes.patch",
        "patch source -r {target} -i changes.patch", "patch source --reject-file={target} -i changes.patch",
        "dd if=source of={target}", "dd if=source \"of={target}\"", "dd if=source 'of={target}'", "git worktree add {target} HEAD", "git worktree add -b topic {target} HEAD",
        "git init {target}", "git init --initial-branch topic {target}", "git clone https://example.invalid/repo {target}",
        "git clone --depth 1 --branch topic https://example.invalid/repo {target}", "git clone -c core.autocrlf=false source {target}",
        "git clone --separate-git-dir={target} source inside", "rm -f {target}", "cp source {target}",
    ];

    public static IEnumerable<object[]> WriteMatrix()
    {
        foreach (string wrapper in Wrappers)
            foreach (string command in Writes)
                yield return [wrapper, command];
    }

    [Theory]
    [MemberData(nameof(WriteMatrix))]
    public void ListedWritesStopOutsideAndPassInsideThroughEveryWrapper(string wrapper, string template)
    {
        string outside = wrapper + template.Replace("{target}", "../outside", StringComparison.Ordinal);
        IReadOnlyList<RunSignal> stopped = Inspect(outside);
        Assert.Equal("critical", Assert.Single(stopped, s => s.Code == "scope").Severity);
        Assert.DoesNotContain(stopped, s => s.Code == "credentials");
        string inside = wrapper + template.Replace("{target}", "inside", StringComparison.Ordinal);
        IReadOnlyList<RunSignal> passed = Inspect(inside);
        if (template.Contains("git clone", StringComparison.Ordinal)) Assert.Equal("network", Assert.Single(passed).Code);
        else Assert.Empty(passed);
    }

    private static readonly string[] Stores = ["keys", "keys/device.p8", "keys/*.p8", "keys/device.cng", "keys/device.se", "keys/device.secret",
        "connections", "connections/connection.json", "connections/*.json", "estates/estate/local", "estates/estate/0123456789abcdef", "estates/estate/local/runtime-certificate.p12",
        "estates/estate/clone/runtime-identity.json", "estates/estate/clone/runtime-identity.json.tmp"];

    public static IEnumerable<object[]> CredentialMatrix()
    {
        foreach (string store in Stores)
            foreach (string prefix in new[] { "absolute", "$VERICAIRN_HOME", "${VERICAIRN_HOME}", "$env:VERICAIRN_HOME", "%VERICAIRN_HOME%", "$ALIAS", "$HOME/state", "~/state" })
                foreach (string operation in new[] { "cat {path}", "cp -R {path} copy", "ls {path}" })
                    yield return [store, prefix, operation];
    }

    [Theory]
    [MemberData(nameof(CredentialMatrix))]
    public void CredentialReadsCopiesAndListingsStopForEveryResolvedSpelling(string store, string prefix, string operation)
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        string path = (prefix == "absolute" ? environment["VERICAIRN_HOME"] : prefix) + "/" + store;
        string command = operation.Replace("{path}", "\"" + path + "\"", StringComparison.Ordinal);
        IReadOnlyList<RunSignal> signals = Inspect(command, environment);
        Assert.Equal("critical", Assert.Single(signals, s => s.Code == "credentials").Severity);
        Assert.All(signals, signal => Assert.DoesNotContain(path, signal.Summary, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("env gh auth token")]
    [InlineData("sudo -u worker gh auth token --hostname example.invalid")]
    [InlineData("nohup gh auth token")]
    [InlineData("time gh auth token")]
    [InlineData("nice gh auth token")]
    [InlineData("timeout 10s gh auth token")]
    [InlineData("command gh auth token")]
    [InlineData("busybox gh auth token")]
    [InlineData("gh auth token")]
    public void GitHubTokenAccessStops(string command) => Assert.Equal("critical", Assert.Single(Inspect(command), s => s.Code == "credentials").Severity);

    [Theory]
    [InlineData("cat $UNKNOWN/keys/device")]
    [InlineData("ls ${UNKNOWN}/connections")]
    [InlineData("cp ~/state/connections copy")]
    [InlineData("cat %UNKNOWN%/connections/connection.json")]
    [InlineData("ls $(state-home)/keys")]
    public void UnresolvedCredentialLocationsAreNeverSilent(string command) =>
        Assert.Contains(Inspect(command), s => s.Code == "audit-unverified" && s.Severity == "warning");

    [Theory]
    [InlineData("keys/readme.txt")]
    [InlineData("connections/example.json")]
    [InlineData("estates/estate/local/runtime-identity.json")]
    [InlineData("state-not-home/keys/device")]
    public void OrdinaryWorkspacePathsDoNotBecomeHomeCredentialStores(string path) => Assert.Empty(Inspect("cat " + path, EnvironmentForAudit()));

    [Fact]
    public void DefaultHomeLayoutAndNormalizedAliasesAreRecognizedWithoutAnOverride()
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        environment.Remove("VERICAIRN_HOME");
        string root = OperatingSystem.IsWindows() ? environment["LOCALAPPDATA"] + "/Vericairn"
            : OperatingSystem.IsMacOS() ? environment["HOME"] + "/Library/Application Support/Vericairn" : environment["XDG_CONFIG_HOME"] + "/vericairn";
        Assert.Contains(Inspect($"ls \"{root}/keys\"", environment), s => s.Code == "credentials");
        environment = EnvironmentForAudit();
        Assert.Contains(Inspect("ALIAS=\"$VERICAIRN_HOME\"; ls \"$ALIAS/keys\"", environment), s => s.Code == "credentials");
        Assert.Contains(Inspect("export ALIAS=\"$VERICAIRN_HOME\"; ls \"$ALIAS/keys\"", environment), s => s.Code == "credentials");
        Assert.Contains(Inspect("cd \"$VERICAIRN_HOME/keys\" && ls .", environment), s => s.Code == "credentials");
        Assert.Contains(Inspect("ls \"$VERICAIRN_HOME/not-keys/../keys\"", environment), s => s.Code == "credentials");
    }

    [Fact]
    public void WrappersDoNotChangeExpansionsAlreadyPerformedByTheCallingShell()
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        foreach (string wrapper in Wrappers)
            Assert.Contains(Inspect(wrapper + "ls \"$VERICAIRN_HOME/connections\"", environment), s => s.Code == "credentials");
        Assert.Contains(Inspect("env VERICAIRN_HOME=inside ls \"$VERICAIRN_HOME/connections\"", environment), s => s.Code == "credentials");
        Assert.Contains(Inspect("env ALIAS=\"$VERICAIRN_HOME\" sh -c 'ls \"$ALIAS/connections\"'", environment), s => s.Code == "credentials");
        Assert.DoesNotContain(Inspect("env -i sh -c 'ls \"$VERICAIRN_HOME/connections\"'", environment), s => s.Code == "credentials");
    }

    [Fact]
    public void ARelativeHomeOverrideStillIdentifiesItsStores()
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        environment["VERICAIRN_HOME"] = "state";
        Assert.Contains(Inspect("ls \"$VERICAIRN_HOME/keys\"", environment), s => s.Code == "credentials");
        Assert.Contains(Inspect("cat state/connections/connection.json", environment), s => s.Code == "credentials");
    }

    [Theory]
    [InlineData("keys")]
    [InlineData("connections")]
    [InlineData("estates/estate/local")]
    [InlineData("estates/estate/0123456789abcdef")]
    public void ImplicitDirectoryListingsAndSearchesAlsoAccessTheStore(string store)
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        foreach (string listing in new[] { "ls", "ls -la", "rg --files", "find .", "Get-ChildItem" })
        {
            Assert.Contains(Inspect($"cd \"$VERICAIRN_HOME/{store}\" && {listing}", environment), s => s.Code == "credentials");
            var action = new HarnessAction("cwd-store", "exec_command", JsonSerializer.SerializeToElement(new { command = listing, cwd = environment["VERICAIRN_HOME"] + "/" + store }));
            Assert.Contains(CommandAudit.Inspect(action, Workspace(), [], environment), s => s.Code == "credentials");
        }
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Glob")]
    [InlineData("Grep")]
    [InlineData("unknown-tool")]
    public void StructuredToolsUseTheSameCredentialLocations(string tool)
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        foreach (string store in Stores)
        {
            var action = new HarnessAction("structured", tool, JsonSerializer.SerializeToElement(new { path = environment["VERICAIRN_HOME"] + "/" + store }));
            Assert.Contains(CommandAudit.Inspect(action, Workspace(), [], environment), s => s.Code == "credentials" && s.Severity == "critical");
        }
    }

    [Theory]
    [InlineData("cd .. && rm outside")]
    [InlineData("cd ..; rm outside")]
    [InlineData("cd ..\nrm outside")]
    [InlineData("cd sub && rm ../../outside")]
    [InlineData("bash -lc 'cd .. && rm outside'")]
    [InlineData("env -C .. rm outside")]
    [InlineData("env --chdir=.. rm outside")]
    [InlineData("sudo -D .. rm outside")]
    [InlineData("git -C .. init outside")]
    public void ResolvableDirectoryChangesApplyToFollowingWrites(string command) => Assert.Contains(Inspect(command), s => s.Code == "scope" && s.Severity == "critical");

    [Theory]
    [InlineData("cd sub && rm inside")]
    [InlineData("cd sub; cd ..; rm inside")]
    [InlineData("cd .. || rm inside")]
    [InlineData("cd .. | cat README.md; rm inside")]
    [InlineData("bash -c 'cd sub'; rm inside")]
    [InlineData("env -C sub rm inside; rm inside")]
    [InlineData("sudo -D sub rm inside")]
    public void DirectoryChangesRespectWorkspaceAndChildScope(string command) => Assert.Empty(Inspect(command));

    [Fact]
    public void AnUnknownDirectoryDoesNotEraseIndependentCredentialAndNetworkVerdicts()
    {
        IReadOnlyList<RunSignal> signals = Inspect("cd invalid\0directory && curl https://example.invalid; cat .aws/credentials");
        Assert.Equal(["audit-unverified", "credentials", "network"], signals.Select(s => s.Code));
    }

    [Theory]
    [InlineData("git -C .. status")]
    [InlineData("git --work-tree=.. diff")]
    [InlineData("cp ../source inside")]
    [InlineData("rsync ../source inside")]
    [InlineData("install ../source inside")]
    [InlineData("tar -cf inside -C .. source")]
    [InlineData("rsync --dry-run source ../outside")]
    [InlineData("rsync -avn source ../outside")]
    [InlineData("patch --dry-run ../outside -i changes.patch")]
    public void OutsideSourcesAreReadsRatherThanWriteTargets(string command) => Assert.Equal("scope-read", Assert.Single(Inspect(command)).Code);

    [Theory]
    [InlineData("cd $UNKNOWN && rm target")]
    [InlineData("osascript -e 'do shell script \"touch /etc/x\"'")]
    [InlineData("cat list | xargs rm")]
    [InlineData("cat list | xargs -0 -I {} rm {}")]
    [InlineData("cat list | xargs env nohup rm")]
    [InlineData("python3 -c 'target.write_text(value)'")]
    public void OpaqueInputsAndUnknownDirectoriesWarn(string command) => Assert.Contains(Inspect(command), s => s.Code == "audit-unverified" && s.Severity == "warning");

    [Theory]
    [InlineData("python3 -c \"open('{target}', 'w').write('x')\"")]
    [InlineData("python3 -c \"open(\\\"{target}\\\", \\\"w\\\").write(\\\"x\\\")\"")]
    [InlineData("python3 -c \"Path('{target}').write_text('x')\"")]
    [InlineData("node -e \"fs.writeFileSync('{target}', 'x')\"")]
    [InlineData("ruby -e \"File.write('{target}', 'x')\"")]
    [InlineData("python3 - <<'PY'\nopen('{target}', 'a')\nPY")]
    public void VisibleInterpreterWritesKeepScopeAlongsideTheOpaqueWarning(string template)
    {
        foreach (string wrapper in Wrappers)
        {
            IReadOnlyList<RunSignal> outside = Inspect(wrapper + template.Replace("{target}", "../outside", StringComparison.Ordinal));
            Assert.Contains(outside, s => s.Code == "scope" && s.Severity == "critical");
            Assert.Contains(outside, s => s.Code == "audit-unverified" && s.Severity == "warning");
            Assert.Equal("audit-unverified", Assert.Single(Inspect(wrapper + template.Replace("{target}", "inside", StringComparison.Ordinal))).Code);
        }
    }

    [Theory]
    [InlineData("python3 -c \"print(\\\"open('/etc/x', 'w')\\\")\"")]
    [InlineData("python3 - <<'PY'\n# open('/etc/x', 'w')\nprint(1)\nPY")]
    [InlineData("python3 - <<'PY'\nexample = '''open('/etc/x', 'w')'''\nprint(example)\nPY")]
    [InlineData("node -e 'console.log(\"fs.writeFileSync(\\\"/etc/x\\\", \\\"x\\\")\")'")]
    [InlineData("node -e '// fs.writeFileSync(\"/etc/x\", \"x\")'")]
    [InlineData("node -e '/* fs.writeFileSync(\"/etc/x\", \"x\") */'")]
    public void InterpreterCommentsAndSampleCodeDoNotInventOutsideWrites(string command) => Assert.Equal("audit-unverified", Assert.Single(Inspect(command)).Code);

    [Theory]
    [InlineData("perl -eprint(1)")]
    [InlineData("perl -MFile::Find -e 'print(1)'")]
    [InlineData("perl -e '-i' source")]
    [InlineData("sed -e '-i' ../source")]
    [InlineData("sed -es/i/o/ ../source")]
    public void EditorProgramAndModuleNamesAreNotInPlaceFlags(string command)
    {
        IReadOnlyList<RunSignal> signals = Inspect(command);
        Assert.Contains(signals, s => s.Code == "audit-unverified");
        Assert.DoesNotContain(signals, s => s.Code == "scope");
    }

    public static IEnumerable<object[]> NullDeviceMatrix()
    {
        foreach (string target in OperatingSystem.IsWindows() ? new[] { "/dev/null", "NUL", "nul", "$null", "$NULL" } : ["/dev/null"])
            foreach (string redirect in new[] { "> ", ">>", "1>", "2>", "2>>", "&>", "&>>", ">|", ">&", "*>" })
                yield return [target, redirect];
    }

    [Theory]
    [MemberData(nameof(NullDeviceMatrix))]
    public void NullDeviceRedirectionsPass(string target, string redirect) => Assert.Empty(Inspect("cat source " + redirect + target));

    [Theory]
    [InlineData("dd if=source of={null}")]
    [InlineData("cp source {null}")]
    [InlineData("cat source | tee {null}")]
    [InlineData("Set-Content -Path {null} -Value 'text'")]
    public void DirectWritesToNullPass(string template)
    {
        foreach (string target in OperatingSystem.IsWindows() ? new[] { "/dev/null", "NUL", "$null" } : ["/dev/null"])
            Assert.Empty(Inspect(template.Replace("{null}", target, StringComparison.Ordinal)));
    }

    [Fact]
    public void ResolvedNullTargetsAlsoPass()
    {
        Assert.Empty(Inspect("DEST=/dev/null; echo x > \"$DEST\""));
        if (OperatingSystem.IsWindows()) Assert.Empty(Inspect("echo x > \"" + Workspace().Replace('\\', '/') + "/NUL\""));
        else
        {
            Assert.Empty(Inspect("echo x > /dev/./null"));
            Assert.Empty(Inspect("cd /dev && echo x > null"));
        }
    }

    [Fact]
    public void RedirectDescriptorsAreNotFilesAndSeparatedNumericFilenamesRemainFiles()
    {
        Assert.Contains(Inspect("cd .. && rm 2 > /dev/null"), s => s.Code == "scope");
        Assert.Empty(Inspect($"cd .. && rm \"{Workspace().Replace('\\', '/')}/inside\" 2>/dev/null"));
        if (OperatingSystem.IsWindows()) Assert.Empty(Inspect($"cd .. && Remove-Item \"{Workspace().Replace('\\', '/')}/inside\" *>NUL"));
    }

    [Theory]
    [InlineData("echo x > /dev/zero")]
    [InlineData("dd of=/dev/zero")]
    [InlineData("tee /dev/null/child")]
    public void OtherDevicePathsKeepTheirScopeTreatment(string command) => Assert.Contains(Inspect(command), s => s.Code == "scope");

    [Theory]
    [InlineData("gh auth status", "network")]
    [InlineData("sed -n '1,80p' source", "")]
    [InlineData("tar -tf archive.tar", "")]
    [InlineData("unzip -l archive.zip", "")]
    [InlineData("echo 'gh auth token'", "")]
    [InlineData("cat <<'EOF' > inside\nopen('/etc/x', 'w')\nEOF", "")]
    [InlineData("env TMPDIR=/etc touch /etc/x", "scope")]
    public void ReadTextAndTemporaryBoundaryRegressions(string command, string expected) => Assert.Equal(expected, string.Join(',', Inspect(command).Select(s => s.Code)));

    private static string Workspace() => Path.Combine(Path.GetTempPath(), "fix20-audit", "workspace");
    private static Dictionary<string, string> EnvironmentForAudit()
    {
        string home = Path.Combine(Path.GetTempPath(), "fix20-audit", "user").Replace('\\', '/');
        return new Dictionary<string, string>(WorkerEnvironment.Names)
        {
            ["HOME"] = home, ["USERPROFILE"] = home, ["VERICAIRN_HOME"] = home + "/state", ["ALIAS"] = home + "/state",
            ["LOCALAPPDATA"] = home + "/AppData/Local", ["XDG_CONFIG_HOME"] = home + "/config",
        };
    }
    private static IReadOnlyList<RunSignal> Inspect(string command, IReadOnlyDictionary<string, string>? environment = null) =>
        CommandAudit.Inspect(new HarnessAction("fix20", "exec_command", JsonSerializer.SerializeToElement(new { command })), Workspace(), ["path:inside"], environment ?? new Dictionary<string, string>());
}
