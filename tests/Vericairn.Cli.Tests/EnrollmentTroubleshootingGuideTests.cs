// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// DOCS-33: the enrollment troubleshooting recipe's examples, run against the enrollment commands. Every command a step runs is matched
/// against the guide's text before it runs, and every line a command prints on standard error is matched against the guide, so the
/// guide quotes exactly what the product prints. The operator's key is a file key held by a recording stand-in for custody that asks
/// presence at each signature (<see cref="RecordingCustody"/>); a declined signature stands in for a prompt that expired, which the
/// runtime cannot tell from a refusal. The real Secure Enclave dialog and the helper's 300-second wait are not exercised here.
/// </summary>
[Collection("cli")]
public sealed partial class EnrollmentTroubleshootingGuideTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void TheFirstDevicesAdmissionStoredWithItsAdoptionExpiredIsReconciledByAdoptingAlone()
    {
        string guide = Guide();
        using Estate e = Estate.Born("guide-first");
        e.Values["<laptop-id>"] = e.Device("Operator's laptop");
        e.Presence(e.Values["<laptop-id>"]);
        e.Custody.DeclineAt = 2;

        CliHarness.Outcome admitted = e.Step(guide, "vericairn devices admit <laptop-id> --by <operator-id> --note \"The first authority.\"");
        e.Values["<admission-id>"] = admitted.Out.Split("  ")[1];
        Quoted(guide, admitted.Err, e.Values, 7);
        Assert.Contains("Stopped at signature 2 of 2, which was not made; 1 was signed.", admitted.Err, StringComparison.Ordinal);
        Assert.Contains("warning: No history checkpoint with trigger adoption was written: The record could not be signed on this machine", admitted.Err, StringComparison.Ordinal);

        // Inspection: the device is admitted, the admission stored and the estate's first authority; the adoption is missing.
        JsonElement device = Listed(e.Step(guide, "vericairn devices list --json"), e.Values["<laptop-id>"]);
        Assert.Equal("admitted", device.GetProperty("admission").GetString());
        Assert.True(device.GetProperty("heldHere").GetBoolean());
        Assert.Contains("\"status\": \"admitted\"", e.Step(guide, "vericairn records show <admission-id>").Out, StringComparison.Ordinal);
        CliHarness.Outcome first = e.Step(guide, "vericairn estate first-authority");
        Assert.Contains("first authority   " + e.Values["<admission-id>"], first.Out, StringComparison.Ordinal);
        Assert.Contains("manifest names    " + e.Values["<admission-id>"], first.Out, StringComparison.Ordinal);
        string notAdopted = Line(e.Step(guide, "vericairn status").Out, "History: ");
        Assert.StartsWith("History: not adopted · no heads · 0 witnessed", notAdopted, StringComparison.Ordinal);
        Assert.Contains("`History: not adopted · no heads · 0 witnessed`", guide, StringComparison.Ordinal);

        // Reconciliation: the adoption alone, with one fresh prompt.
        e.Custody.DeclineAt = 0;
        CliHarness.Outcome check = e.Step(guide, "vericairn estate checkpoint --adopt --check");
        e.Values["<plan>"] = Line(check.Out, "plan: ")["plan: ".Length..];
        CliHarness.Outcome adopted = e.Step(guide, "vericairn estate checkpoint --adopt --expect-plan <plan> --by <operator-id>");
        Quoted(guide, adopted.Err, e.Values, 4);
        Assert.Contains("Signed 1 of 1.", adopted.Err, StringComparison.Ordinal);
        Assert.StartsWith("History: adopted", Line(e.Step(guide, "vericairn status").Out, "History: "), StringComparison.Ordinal);
        e.Step(guide, "vericairn estate check");
        Assert.Single(e.Api.Session.Load().OfType("admission"));
        Assert.Equal(3, e.Custody.Reasons.Count);
    }

    [Fact]
    public void ASecondAdmissionOfTheFirstDeviceWouldNotAdopt()
    {
        string guide = Guide();
        using Estate e = Estate.Born("guide-first-again");
        string laptop = e.Device("Operator's laptop");
        e.Presence(laptop);
        e.Custody.DeclineAt = 2;
        Assert.Equal(0, e.Run("devices", "admit", laptop, "--by", e.Operator, "--note", "The first authority.").Code);
        e.Custody.DeclineAt = 0;

        // What the guide warns against: the rerun writes another admission, signed by the device itself, and adopts nothing.
        Assert.Equal(0, e.Run("devices", "admit", laptop, "--by", e.Operator, "--note", "The first authority.").Code);
        EstateModel model = e.Api.Session.Load();
        Assert.Equal(2, model.Records.Count(r => r.Type == "admission" && r.Subject?.Id == laptop && r.Signatures.Single().Device == laptop));
        Assert.False(SignedHistory.Adopted(model));
        Assert.Contains("A second `devices admit` would not adopt: it would write another admission, signed by the device itself, and leave the estate not adopted.", guide.Replace('\n', ' '), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAdmissionThatExpiredWithNothingWrittenIsReopenedOnceWithoutCreatingTheDeviceAgain()
    {
        string guide = Guide();
        using Estate e = Estate.Born("guide-second");
        e.Admitted("Operator's laptop");
        e.Values["<desktop-id>"] = e.Device("Operator's desktop");
        // The next signature the custody is asked for: the laptop's of the desktop's admission.
        e.Custody.DeclineAt = e.Custody.Reasons.Count + 1;

        CliHarness.Outcome expired = e.Step(guide, "vericairn devices admit <desktop-id> --by <operator-id> --note \"The operator's desktop.\"", refused: true);
        Quoted(guide, expired.Err, e.Values, 5);
        Assert.Contains("Stopped at signature 1 of 1, which was not made; 0 were signed.", expired.Err, StringComparison.Ordinal);
        Assert.Contains("error: The record could not be signed on this machine (the device's key store refused to sign, InvalidOperationException).", expired.Err, StringComparison.Ordinal);
        Assert.Equal(string.Empty, expired.Out);
        JsonElement device = Listed(e.Step(guide, "vericairn devices list --json"), e.Values["<desktop-id>"]);
        Assert.Equal("not admitted", device.GetProperty("admission").GetString());
        Assert.True(device.GetProperty("heldHere").GetBoolean());

        // The one missing step, once, with a fresh prompt.
        e.Custody.DeclineAt = 0;
        CliHarness.Outcome reopened = e.Step(guide, "vericairn devices admit <desktop-id> --by <operator-id> --note \"The operator's desktop.\"");
        Quoted(guide, reopened.Err, e.Values, 4);
        Assert.Contains("Signed 1 of 1.", reopened.Err, StringComparison.Ordinal);
        Assert.Equal("admitted", Listed(e.Step(guide, "vericairn devices list --json"), e.Values["<desktop-id>"]).GetProperty("admission").GetString());
        EstateModel model = e.Api.Session.Load();
        Assert.Single(model.Records, r => r.Type == "admission" && r.Subject?.Id == e.Values["<desktop-id>"]);
        Assert.Single(model.Records, r => r.Type == "device" && r.Id == e.Values["<desktop-id>"]);
    }

    [Fact]
    public void AnAdmissionThatHadCompletedIsFoundByInspectionAndARerunWouldWriteASecond()
    {
        string guide = Guide();
        using Estate e = Estate.Born("guide-completed");
        e.Admitted("Operator's laptop");
        e.Values["<desktop-id>"] = e.Device("Operator's desktop");
        e.Values["<admission-id>"] = e.Run("devices", "admit", e.Values["<desktop-id>"], "--by", e.Operator, "--note", "The operator's desktop.").Out.Split("  ")[1];

        Assert.Equal("admitted", Listed(e.Step(guide, "vericairn devices list --json"), e.Values["<desktop-id>"]).GetProperty("admission").GetString());
        Assert.Contains(e.Values["<desktop-id>"], e.Step(guide, "vericairn records show <admission-id>").Out, StringComparison.Ordinal);

        // Today a rerun is not refused: it writes a second admission of the same device, which is why the guide inspects first.
        Assert.Equal(0, e.Run("devices", "admit", e.Values["<desktop-id>"], "--by", e.Operator, "--note", "The operator's desktop.").Code);
        Assert.Equal(2, e.Api.Session.Load().Records.Count(r => r.Type == "admission" && r.Subject?.Id == e.Values["<desktop-id>"]));
        Assert.Contains("`devices admit` again would not be refused today; it writes a second admission of the same device", guide.Replace('\n', ' '), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuideQuotesTheClientTimeoutAndLinksFromTheEnrollmentGuides()
    {
        string guide = Guide();
        ClientFailure timeout = ClientFailure.FromException(new ClientTimeoutException(true))!;
        Assert.Equal(124, timeout.ExitCode);
        Assert.Contains("error: " + timeout.Message, guide, StringComparison.Ordinal);
        Assert.Contains("exits with code 124", guide, StringComparison.Ordinal);
        Assert.Contains("An authenticated prompt is no completion receipt.", guide, StringComparison.Ordinal);
        Assert.Contains("vericairn estate first-authority", guide, StringComparison.Ordinal);
        // Two first devices: the command only where neither clone's adoption was written; otherwise the refusal's guidance, and the
        // device whose self-admission is removed is admitted from the first.
        string flat = guide.Replace('\n', ' ');
        Assert.Contains("When neither was written", flat, StringComparison.Ordinal);
        Assert.Contains("`estate first-authority` refuses, since witnessed history speaks to the first authority", flat, StringComparison.Ordinal);
        Assert.Contains("the device whose self-admission you removed is then admitted from your first device", flat, StringComparison.Ordinal);
        // Nothing in the recipe lengthens a wait, weakens custody, turns presence off or retries by itself.
        foreach (string avoided in new[] { "--presence false", "--custody file", "pkill", "kill -", "rm -", "sudo" })
            Assert.DoesNotContain(avoided, guide, StringComparison.Ordinal);
        foreach (string linking in new[] { "operator-guide.md", "agent-builders.md", "agents-only.md", "people-and-agents.md", "people-only.md" })
            Assert.Contains("(enrollment-troubleshooting.md)", Doc(linking), StringComparison.Ordinal);
    }

    /// <summary>Asserts that each line a command printed on standard error, its identities put back as the guide's placeholders, is in the guide.</summary>
    private static void Quoted(string guide, string error, Dictionary<string, string> values, int expected)
    {
        string[] lines = [.. error.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => l.Length > 0)];
        Assert.Equal(expected, lines.Length);
        foreach (string line in lines)
        {
            string placed = values.Aggregate(line, (text, pair) => text.Replace(pair.Value, pair.Key, StringComparison.Ordinal));
            Assert.True(guide.Contains("\n" + placed + "\n", StringComparison.Ordinal) || guide.Contains("\n  " + placed + "\n", StringComparison.Ordinal),
                $"The guide does not quote what the command prints: {placed}");
        }
    }

    private static JsonElement Listed(CliHarness.Outcome outcome, string device) =>
        JsonDocument.Parse(outcome.Out).RootElement.EnumerateArray().Single(d => d.GetProperty("id").GetString() == device);

    private static string Line(string output, string start) => output.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith(start, StringComparison.Ordinal));

    private static string Guide() => Doc("enrollment-troubleshooting.md");

    private static string Doc(string name)
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root, "docs", name)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex("<[a-z-]+>")]
    private static partial Regex Placeholder();

    /// <summary>
    /// A new estate, as <c>estate init</c> writes it, served by an embedded runtime whose operator's key is held by a recording stand-in
    /// for custody that asks presence; the commands run through that runtime as a person's would.
    /// </summary>
    private sealed class Estate : IDisposable
    {
        private Estate(string root, string op)
        {
            Root = root;
            Operator = op;
            Values["<operator-id>"] = op;
            Api = EstateService.Open(root);
            Api.KeyStores = [Custody];
        }

        public string Root { get; }

        public string Operator { get; }

        public EstateService Api { get; }

        public RecordingCustody Custody { get; } = new(VericairnHome.Path());

        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        private readonly HashSet<string> presence = new(StringComparer.Ordinal);

        public static Estate Born(string name)
        {
            string root = CliHarness.TempDirectory(name);
            using (var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", ["init", "-q", "-b", "main", root]) { RedirectStandardOutput = true, RedirectStandardError = true }))
                git!.WaitForExit();
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Enroll the operator's devices.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            return new Estate(root, op);
        }

        /// <summary>A device of the operator's on this machine, created in the test's setup with a file key, not admitted.</summary>
        public string Device(string name)
        {
            WriteOutcome created = Api.DeviceCreate(new(Operator, name, "file"));
            Assert.True(created.Ok, created.Problem?.Message);
            return created.Written!.Id;
        }

        /// <summary>The operator's first device, admitted by itself without presence, then asking presence at every signature.</summary>
        public void Admitted(string name)
        {
            string device = Device(name);
            WriteOutcome admitted = Api.DeviceAdmit(new(device, Operator, "The first authority."));
            Assert.True(admitted.Ok, admitted.Problem?.Message);
            Presence(device);
        }

        /// <summary>States that a device asks its owner's presence at every signature.</summary>
        public void Presence(string device)
        {
            presence.Add(device);
            Api.PresenceRequired = presence.Contains;
        }

        public CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(Api, null, ["--estate", Root, .. args]);

        /// <summary>Runs a command the guide shows, word for word, with its placeholders filled.</summary>
        public CliHarness.Outcome Step(string guide, string command, bool refused = false)
        {
            Assert.True(guide.Contains("\n" + command + "\n", StringComparison.Ordinal), $"The guide no longer shows the command: {command}");
            List<string> words = Words(command)[1..];
            CliHarness.Outcome outcome = Run([.. words.Select(Fill)]);
            if (refused) Assert.True(outcome.Code != 0, $"{command} was not refused\n{outcome.Out}");
            else Assert.True(outcome.Code == 0, $"{command} exited {outcome.Code}\n{outcome.Out}\n{outcome.Err}");
            return outcome;
        }

        private string Fill(string word)
        {
            string filled = Placeholder().Replace(word, m => Values.TryGetValue(m.Value, out string? value) ? value : m.Value);
            Match left = Placeholder().Match(filled);
            Assert.False(left.Success, $"No value for the guide's placeholder {left.Value}.");
            return filled;
        }

        // A command line split into words, a double-quoted word kept whole without its quotes.
        private static List<string> Words(string command)
        {
            var words = new List<string>();
            var word = new StringBuilder();
            bool quoted = false;
            foreach (char c in command)
            {
                if (c == '"') quoted = !quoted;
                else if (c == ' ' && !quoted)
                {
                    if (word.Length > 0) words.Add(word.ToString());
                    word.Clear();
                }
                else word.Append(c);
            }

            if (word.Length > 0) words.Add(word.ToString());
            return words;
        }

        public void Dispose()
        {
            Api.Dispose();
            CliHarness.Delete(Root);
        }
    }
}
