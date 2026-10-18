// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class CommandAuditFix24Tests
{
    private static readonly string[] Wrappers = ["", "env ", "/usr/bin/env ", "env -i ", "env -u UNUSED ", "sudo -u worker ", "doas ", "doas -n -u worker ",
        "doas -uworker ", "nohup ", "time -p ", "nice -n 10 ", "timeout -s TERM -k 2s 10s ", "command -- ", "busybox ", "exec ",
        "env MODE=test nohup nice -n 5 timeout 10s doas -n -u worker "];

    private static readonly string[] Transfers = ["cp -r {source} copy", "cp -R {source} copy", "cp -a {source} copy", "cp --recursive {source} copy",
        "cp -R -t copy {source}", "cp -R --target-directory=copy {source}", "tar -cf copy.tar {source}", "tar czf copy.tar.gz {source}",
        "tar --create --file=copy.tar {source}", "tar -rf copy.tar {source}", "tar -uf copy.tar {source}", "rsync -a {source}/ copy/",
        "rsync -r --exclude cache {source} copy", "zip -r copy.zip {source}", "zip -qr copy.zip {source}", "ditto {source} copy",
        "ditto -c -k --keepParent {source} copy.zip", "Copy-Item -Recurse -LiteralPath {source} -Destination copy"];

    public static IEnumerable<object[]> HomeCopyMatrix()
    {
        foreach (string wrapper in Wrappers)
            foreach (string transfer in Transfers)
                yield return [wrapper, transfer];
    }

    [Theory]
    [MemberData(nameof(HomeCopyMatrix))]
    public void RecursiveTransfersStopForHomeAndAncestorsAndPassForAnUnrelatedSource(string wrapper, string template)
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        foreach (string source in new[] { environment["VERICAIRN_HOME"], environment["HOME"], Path.GetDirectoryName(environment["HOME"])! })
        {
            IReadOnlyList<RunSignal> signals = Inspect(wrapper + template.Replace("{source}", "\"" + source + "\"", StringComparison.Ordinal), environment);
            Critical(signals, "credentials");
            Assert.All(signals, s => Assert.DoesNotContain(source, s.Summary, StringComparison.Ordinal));
        }
        Assert.Empty(Inspect(wrapper + template.Replace("{source}", "source", StringComparison.Ordinal), environment));
        Assert.DoesNotContain(Inspect(wrapper + template.Replace("{source}", "\"" + environment["VERICAIRN_HOME"] + "-other\"", StringComparison.Ordinal), environment), s => s.Severity == "critical");
    }

    public static IEnumerable<object[]> ExpansionMatrix()
    {
        foreach (string source in new[] { "$VERICAIRN_HOME", "${VERICAIRN_HOME}", "$env:VERICAIRN_HOME", "%VERICAIRN_HOME%", "$ALIAS", "$HOME/state", "~/state", "$HOME", "~" })
            foreach (string transfer in Transfers) yield return [source, transfer];
    }

    [Theory]
    [MemberData(nameof(ExpansionMatrix))]
    public void RecursiveSourcesResolveEachCapturedExpansion(string source, string template) =>
        Critical(Inspect(template.Replace("{source}", "\"" + source + "\"", StringComparison.Ordinal), EnvironmentForAudit()), "credentials");

    [Theory]
    [InlineData("ALIAS=\"$VERICAIRN_HOME\"; cp -r \"$ALIAS\" copy")]
    [InlineData("export ALIAS=\"$HOME\"; zip -r copy.zip \"$ALIAS\"")]
    [InlineData("cd \"$VERICAIRN_HOME\" && cp -r . copy")]
    [InlineData("cd \"$HOME\" && tar -cf copy.tar state")]
    [InlineData("tar -cf copy.tar -C \"$VERICAIRN_HOME\" .")]
    [InlineData("tar -cf copy.tar -C \"$HOME\" state -C elsewhere other")]
    [InlineData("tar -cf copy.tar --directory=\"$HOME\" state")]
    [InlineData("tar -cf copy.tar -C\"$HOME\" state")]
    [InlineData("env ALIAS=\"$VERICAIRN_HOME\" sh -c 'cp -r \"$ALIAS\" copy'")]
    [InlineData("doas sh -c 'cd \"$HOME\" && (ditto state copy)'")]
    [InlineData("cp -R \"$VERICAIRN_HOME/not-home/..\" copy")]
    [InlineData("scp -r \"$VERICAIRN_HOME\" host:copy")]
    [InlineData("mv \"$VERICAIRN_HOME\" copy")]
    [InlineData("scp -i \"$VERICAIRN_HOME/keys/device.p8\" source host:copy")]
    public void CopySourcesRespectAssignmentsDirectoriesAndChildShells(string command) => Critical(Inspect(command, EnvironmentForAudit()), "credentials");

    [Fact]
    public void DefaultAndRelativeHomeLayoutsAreIncluded()
    {
        Dictionary<string, string> environment = EnvironmentForAudit();
        environment.Remove("VERICAIRN_HOME");
        string home = OperatingSystem.IsWindows() ? environment["LOCALAPPDATA"] + "/Vericairn"
            : OperatingSystem.IsMacOS() ? environment["HOME"] + "/Library/Application Support/Vericairn" : environment["XDG_CONFIG_HOME"] + "/vericairn";
        foreach (string source in new[] { home, Path.GetDirectoryName(home)! })
            Critical(Inspect("cp -r \"" + source + "\" copy", environment), "credentials");
        environment["VERICAIRN_HOME"] = "state";
        Critical(Inspect("cp -r state copy", environment), "credentials");
        Assert.Empty(Inspect("cp -r state-other copy", environment));
    }

    [Theory]
    [InlineData("ls \"$VERICAIRN_HOME\"")]
    [InlineData("ls -R \"$VERICAIRN_HOME\"")]
    [InlineData("cat \"$VERICAIRN_HOME/estates/estate/local/live.db\"")]
    [InlineData("cat \"$VERICAIRN_HOME/estates/estate/0123456789abcdef/live.db\"")]
    [InlineData("cp \"$VERICAIRN_HOME/estates/estate/local/live.db\" copy")]
    [InlineData("rsync -an \"$VERICAIRN_HOME\" copy")]
    [InlineData("rsync -r --dry-run \"$HOME\" copy")]
    [InlineData("tar --no-recursion -cf copy.tar \"$VERICAIRN_HOME\"")]
    [InlineData("tar -tf copy.tar \"$VERICAIRN_HOME\"")]
    [InlineData("cp \"$VERICAIRN_HOME\" copy")]
    [InlineData("cp -tarchive \"$VERICAIRN_HOME\"")]
    [InlineData("zip copy.zip \"$VERICAIRN_HOME\"")]
    public void ListingsLiveDatabaseReadsAndNonRecursiveSourcesOnlyWarn(string command) =>
        Assert.Equal("scope-read", Assert.Single(Inspect(command, EnvironmentForAudit())).Code);

    public static IEnumerable<object[]> HelperMatrix()
    {
        foreach (string wrapper in Wrappers)
            foreach (string helper in new[] { "git credential-store", "git -C inside credential-cache", "git -C .. credential-custom-helper", "git-credential-store" })
                foreach (string options in new[] { "", "--file helper.data ", "--file=helper.data ", "-f helper.data ", "-fhelper.data ", "--socket socket --timeout 30 ", "--file get -- ", "--file erase " })
                    yield return [wrapper + helper + " " + options];
    }

    [Theory]
    [MemberData(nameof(HelperMatrix))]
    public void HelperActionsStopAfterOptionsWhileHelpAndOptionValuesPass(string prefix)
    {
        foreach (string action in new[] { "get", "store", "erase" }) Critical(Inspect(prefix + action), "credentials");
        Assert.DoesNotContain(Inspect(prefix + "--help"), s => s.Severity == "critical");
    }

    [Theory]
    [InlineData("git credential-store --file get")]
    [InlineData("git credential-store --file store")]
    [InlineData("git credential-cache --socket erase")]
    [InlineData("git credential-store --help get")]
    [InlineData("git credential reject")]
    [InlineData("echo 'git credential-store --file helper.data get'")]
    [InlineData("git commit -m 'git credential-store --file helper.data get'")]
    [InlineData("doas -C policy touch ../outside")]
    public void NonExecutingHelperAndWrapperFormsDoNotInventCriticalSignals(string command) => Assert.DoesNotContain(Inspect(command), s => s.Severity == "critical");

    public static IEnumerable<object[]> OutsideWriteMatrix()
    {
        foreach (string wrapper in Wrappers)
            foreach (string template in new[] { "sh -c 'cd {target} && (touch x)'", "sh -c 'cd {target} && { touch x; }'",
                "make install PREFIX={target}", "make install DESTDIR={target}", "make PREFIX={target} install", "gmake install DESTDIR={target}",
                "make -C {target} install PREFIX=.", "defaults write {target}/settings key -string value" })
                yield return [wrapper, template];
    }

    [Theory]
    [MemberData(nameof(OutsideWriteMatrix))]
    public void EachNewWriteStopsOutsideAndPassesInsideThroughWrappers(string wrapper, string template)
    {
        Critical(Inspect(wrapper + template.Replace("{target}", "..", StringComparison.Ordinal)), "scope");
        Assert.Empty(Inspect(wrapper + template.Replace("{target}", "inside", StringComparison.Ordinal)));
    }

    [Fact]
    public void PreferenceWritesToNamedDomainsStopThroughEveryWrapperWhileReadsPass()
    {
        foreach (string wrapper in Wrappers)
        {
            Critical(Inspect(wrapper + "defaults write org.example.app key -bool true"), "scope");
            Assert.Empty(Inspect(wrapper + "defaults read org.example.app key"));
        }
    }

    [Theory]
    [InlineData("cd .. && (touch x)")]
    [InlineData("cd ..; { touch x; }")]
    [InlineData("cd ..\n((touch x))")]
    [InlineData("{ cd ..; }; touch x")]
    [InlineData("{ cd ..; { (touch x); }; }")]
    [InlineData("{ TARGET=..; }; touch \"$TARGET/x\"")]
    [InlineData("(cd ..; touch x)")]
    [InlineData("(echo x) > ../outside")]
    [InlineData("{ echo x; } >> ../outside")]
    [InlineData("PREFIX=.. make install")]
    [InlineData("DESTDIR=.. make install")]
    [InlineData("make -fnoninteractive install PREFIX=..")]
    [InlineData("make install PREFIX=../../outside DESTDIR=inside")]
    [InlineData("defaults write org.example.app key -bool true")]
    [InlineData("defaults -currentHost write -g key -bool true")]
    [InlineData("defaults -host localhost write -app Example key -string value")]
    public void GroupsAndInstallationTargetsKeepConfidentOutsideWrites(string command) => Critical(Inspect(command), "scope");

    [Theory]
    [InlineData("(cd ..); touch inside")]
    [InlineData("{ cd ..; } | cat source; touch inside")]
    [InlineData("{ cd ..; } & touch inside")]
    [InlineData("cd .. || (touch inside)")]
    [InlineData("{ cd .. | cat source; }; touch inside")]
    [InlineData("{ cd ..; } > inside")]
    [InlineData("cd .. && (cd workspace; touch inside)")]
    [InlineData("echo '{ touch ../outside; }'")]
    [InlineData("echo '(touch ../outside)'")]
    [InlineData("make install DESTDIR=inside PREFIX=/usr")]
    [InlineData("make install PREFIX=inside DESTDIR=")]
    [InlineData("make -f install PREFIX=..")]
    [InlineData("make --eval install PREFIX=..")]
    [InlineData("defaults read org.example.app key")]
    [InlineData("defaults write ./settings key -string '../value'")]
    public void GroupScopeTextAndStagedInstallsDoNotInventOutsideWrites(string command) => Assert.DoesNotContain(Inspect(command), s => s.Severity == "critical");

    [Theory]
    [InlineData("cp -r \"$UNKNOWN\" copy")]
    [InlineData("cd $UNKNOWN && (touch x)")]
    [InlineData("make install PREFIX=$UNKNOWN")]
    [InlineData("make install")]
    [InlineData("defaults write $UNKNOWN key value")]
    [InlineData("(touch inside")]
    [InlineData("echo $(compute)")]
    [InlineData("cat <(compute)")]
    public void UnknownTargetsAndIncompleteGroupsWarn(string command) => Assert.Contains(Inspect(command), s => s.Code == "audit-unverified" && s.Severity == "warning");

    public static IEnumerable<object[]> NullMatrix()
    {
        foreach (string wrapper in Wrappers)
            foreach (string target in OperatingSystem.IsWindows() ? new[] { "/dev/null", "NUL", "nul", "$null", "$NULL" } : ["/dev/null"])
                foreach (string redirect in new[] { ">", ">>", "1>", "2>", "2>>", "&>", "&>>", ">|", ">&", "*>", "<", "0<", "<&" })
                    yield return [wrapper, target, redirect];
    }

    [Theory]
    [MemberData(nameof(NullMatrix))]
    public void NullDevicesPassEveryRedirectInWrappersAndGroupsWithoutHidingRealTargets(string wrapper, string target, string redirect)
    {
        Assert.Empty(Inspect(wrapper + "cat source " + redirect + target));
        Assert.Empty(Inspect("cd .. && (" + wrapper + "echo x " + redirect + target + ")"));
        Assert.Empty(Inspect("cd .. && { " + wrapper + "echo x " + redirect + target + "; }"));
        if (!redirect.Contains('<', StringComparison.Ordinal))
            Critical(Inspect("cd .. && (" + wrapper + "echo x " + redirect + "outside)"), "scope");
        Critical(Inspect("cd .. && (" + wrapper + "touch outside " + redirect + target + ")"), "scope");
        Critical(Inspect(wrapper + "git credential-store --file helper.data get " + redirect + target), "credentials");
    }

    [Theory]
    [InlineData("command")]
    [InlineData("cmd")]
    public void CommandArraysAreInspectedWithShellBodiesAndLiteralArgumentsPreserved(string field)
    {
        foreach (string[] command in new[] { new[] { "touch", "../outside" }, ["sh", "-c", "cd .. && (touch x)"], ["doas", "-u", "worker", "touch", "../outside"], ["touch ../outside"] })
            Critical(InspectInput(new Dictionary<string, object>(StringComparer.Ordinal) { [field] = command }), "scope");
        foreach (string[] command in new[] { new[] { "echo", "touch ../outside; (rm ../another)" }, ["touch", "a file"], ["sh", "-c", "printf '%s' 'touch ../outside'"], ["git", "commit", "-m", "document's ../example"] })
            Assert.Empty(InspectInput(new Dictionary<string, object>(StringComparer.Ordinal) { [field] = command }));
        string[] helper = ["git", "credential-store", "--file", "helper.data", "get"];
        Critical(InspectInput(new Dictionary<string, object>(StringComparer.Ordinal) { [field] = helper }), "credentials");
        string[] copy = ["env", "MODE=two words", "cp", "-r", "$VERICAIRN_HOME", "copy"];
        Critical(InspectInput(new Dictionary<string, object>(StringComparer.Ordinal) { [field] = copy }, EnvironmentForAudit()), "credentials");
        string[] wrapped = ["time", "--output=../outside file", "echo", "example"];
        Critical(InspectInput(new Dictionary<string, object>(StringComparer.Ordinal) { [field] = wrapped }), "scope");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("42")]
    [InlineData("{}")]
    public void MalformedCommandFieldsWarn(string value)
    {
        using JsonDocument input = JsonDocument.Parse("{\"command\":" + value + "}");
        Assert.Equal("audit-unverified", Assert.Single(CommandAudit.Inspect(new HarnessAction("malformed", "exec_command", input.RootElement), Workspace(), [])).Code);
    }

    [Fact]
    public void MalformedArrayElementsKeepIndependentVerdicts()
    {
        object?[] command = ["touch", null, "../outside"];
        IReadOnlyList<RunSignal> signals = InspectInput(new { command });
        Critical(signals, "scope");
        Assert.Contains(signals, s => s.Code == "audit-unverified" && s.Severity == "warning");
    }

    [Theory]
    [InlineData("item.started")]
    [InlineData("item.updated")]
    [InlineData("item.completed")]
    public void LauncherLifecycleArraysReachTheSameAudit(string lifecycle)
    {
        string[] command = ["sh", "-c", "cd .. && { touch x; }"];
        string line = JsonSerializer.Serialize(new { type = lifecycle, item = new { type = "command_execution", id = "array", command, aggregated_output = "git credential-store get" } });
        HarnessAction action = Assert.Single(new CodexLauncher().ReadActions(line));
        Critical(CommandAudit.Inspect(action, Workspace(), []), "scope");
        Assert.DoesNotContain(CommandAudit.Inspect(action, Workspace(), []), s => s.Code == "credentials");
        string claude = JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "tool_use", id = "array", name = "Bash", input = new { command } } } } });
        Critical(CommandAudit.Inspect(Assert.Single(new ClaudeCodeLauncher().ReadActions(claude)), Workspace(), []), "scope");
    }

    private static void Critical(IReadOnlyList<RunSignal> signals, string code) => Assert.Equal("critical", Assert.Single(signals, s => s.Code == code).Severity);
    private static string Workspace() => Path.Combine(Path.GetTempPath(), "fix24-audit", "workspace");
    private static Dictionary<string, string> EnvironmentForAudit()
    {
        string home = Path.Combine(Path.GetTempPath(), "fix24-audit", "user space").Replace('\\', '/');
        return new Dictionary<string, string>(WorkerEnvironment.Names)
        {
            ["HOME"] = home, ["USERPROFILE"] = home, ["VERICAIRN_HOME"] = home + "/state", ["ALIAS"] = home + "/state",
            ["LOCALAPPDATA"] = home + "/AppData/Local", ["XDG_CONFIG_HOME"] = home + "/config",
        };
    }
    private static IReadOnlyList<RunSignal> Inspect(string command, IReadOnlyDictionary<string, string>? environment = null) => InspectInput(new { command }, environment);
    private static IReadOnlyList<RunSignal> InspectInput<T>(T input, IReadOnlyDictionary<string, string>? environment = null) =>
        CommandAudit.Inspect(new HarnessAction("fix24", "exec_command", JsonSerializer.SerializeToElement(input)), Workspace(), [], environment ?? new Dictionary<string, string>());
}
