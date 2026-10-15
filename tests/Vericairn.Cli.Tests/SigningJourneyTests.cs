// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;
using System.Text.Json;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>Signed records, devices and admission, headless through the CLI: a clone without an admitted key is read-only.</summary>
[Collection("cli")]
public class SigningJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void AdmittedDevicesSignAndACloneWithoutKeysIsReadOnly()
    {
        string a = CliHarness.TempDirectory("sign-a");
        string homeA = CliHarness.TempDirectory("sign-home-a");
        string homeB = CliHarness.TempDirectory("sign-home-b");
        string scratch = CliHarness.TempDirectory("sign-scratch");
        string b = string.Empty;
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            CliHarness.Outcome init = CliHarness.Ok("--estate", a, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Signing journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            string op = init.Json.GetProperty("operator").GetString()!;
            string estateId = init.Json.GetProperty("estate").GetString()!;

            // The operator creates a device on this machine and admits it (root self-admission); then admits the builder's device.
            string operatorDevice = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The operator's own laptop.");
            string builderPath = CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
            string builder = CliHarness.Ok("--estate", a, "--json", "records", "new", "principal", "--by", op, "--body", builderPath).Id;
            string builderDevice = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", builder, "--name", "Build machine", "--custody", "file", "--profile", "embedded", "local-socket").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "The builder's machine.");
            string listing = CliHarness.Ok("--estate", a, "devices", "list").Out;
            Assert.Contains("admitted  key held on this machine", listing, StringComparison.Ordinal);

            // The operator switches the policy to required; the charter revision is signed by the operator's device.
            string charterPath = CliHarness.WriteJson(scratch, "charter.json", $$$"""{"name":"Example","slug":"example","purpose":"Signing journey.","operator":"{{{op}}}","recordFormat":1,"policies":{"signatures":"required","releaseAcceptance":["operator"]},"organization":{"name":"Example"}}""");
            CliHarness.Ok("--estate", a, "--json", "records", "new", "estate", "--by", op, "--body", charterPath, "--supersedes", estateId);

            // The builder authors a record: signed automatically by its admitted device.
            string orderPath = CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Signed work","intent":"Prove signing.","scope":["x"],"exitQuestion":"Signed?","inputs":[],"deliverables":["y"]}""");
            CliHarness.Outcome order = CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", orderPath);
            string orderFile = File.ReadAllText(Path.Combine(a, order.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("\"signatures\"", orderFile, StringComparison.Ordinal);
            Assert.Contains(builderDevice, orderFile, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);

            // A fresh clone on a machine without the keys verifies everything but cannot author.
            b = CliHarness.Clone(a, "sign-b");
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            Assert.Equal(0, CliHarness.Run("--estate", b, "estate", "check").Code);
            CliHarness.Outcome refused = CliHarness.Run("--estate", b, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order2.json", """{"key":"W-2","kind":"ordinary","title":"Unsigned","intent":"Should be refused.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}"""));
            Assert.Equal(2, refused.Code);
            Assert.Contains("read-only", refused.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(b, "records", "work-order"), "*.json", SearchOption.AllDirectories), f => !f.Contains(order.Id, StringComparison.Ordinal));

            // Revocation from the operator's machine: the builder's later records are refused and earlier ones stay valid.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            Thread.Sleep(1100); // revocation applies from its own second; the earlier records must sit in an earlier one
            CliHarness.Ok("--estate", a, "--json", "devices", "revoke", builderDevice, "--by", op, "--reason", "Machine retired.");
            Assert.Contains("read-only", CliHarness.Run("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", orderPath).Err, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);
            JsonElement devices = CliHarness.Ok("--estate", a, "--json", "devices", "list").Json;
            Assert.Equal("revoked", devices.EnumerateArray().Single(d => d.GetProperty("id").GetString() == builderDevice).GetProperty("admission").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, b, homeA, homeB, scratch);
        }
    }

    [Fact]
    public void AnAdmissionIsSignedWhateverTheSignaturePolicy()
    {
        string a = CliHarness.TempDirectory("sign-admission");
        string home = CliHarness.TempDirectory("sign-admission-home");
        string scratch = CliHarness.TempDirectory("sign-admission-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = CliHarness.Ok("--estate", a, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Admissions.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", a, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string builderDevice = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", builder, "--name", "Build machine", "--custody", "file").Id;

            // The charter leaves signatures optional, yet with no device of its own the operator cannot admit another one unsigned.
            int before = Directory.EnumerateFiles(Path.Combine(a, "records"), "*.json", SearchOption.AllDirectories).Count();
            CliHarness.Outcome refused = CliHarness.Run("--estate", a, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "Too early.");
            Assert.Equal(2, refused.Code);
            Assert.Contains("first authority", refused.Err, StringComparison.Ordinal);
            Assert.Equal(before, Directory.EnumerateFiles(Path.Combine(a, "records"), "*.json", SearchOption.AllDirectories).Count());

            // The operator's own device admits itself, once, and then signs the builder's admission.
            string operatorDevice = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", operatorDevice, "--by", op, "--note", "The first authority.");
            CliHarness.Outcome admitted = CliHarness.Ok("--estate", a, "--json", "devices", "admit", builderDevice, "--by", op, "--note", "The builder's machine.");
            string admission = File.ReadAllText(Path.Combine(a, admitted.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains(operatorDevice, admission, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, home, scratch);
        }
    }

    [Fact]
    public void ADeviceIsRenamedAndRotatedAndKeepsItsIdentityAdmissionAndHistory()
    {
        string a = CliHarness.TempDirectory("rotate-a");
        string home = CliHarness.TempDirectory("rotate-home");
        string elsewhere = CliHarness.TempDirectory("rotate-elsewhere");
        string scratch = CliHarness.TempDirectory("rotate-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = Init(a);
            string device = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", device, "--by", op, "--note", "The operator's own laptop.");
            CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", op, "--body", Order(scratch, "W-1"));
            string keys = Path.Combine(home, "keys");
            byte[] firstKey = File.ReadAllBytes(Path.Combine(keys, device + ".p8"));

            // A rename keeps the device's identity, its key and its admission.
            CliHarness.Ok("--estate", a, "--json", "devices", "rename", device, "--name", "Operator workstation");
            JsonElement row = CliHarness.Ok("--estate", a, "--json", "devices", "list").Json.EnumerateArray().Single();
            Assert.Equal(device, row.GetProperty("id").GetString());
            Assert.Equal("Operator workstation", row.GetProperty("name").GetString());
            Assert.Equal("admitted", row.GetProperty("admission").GetString());
            Assert.Equal(firstKey, File.ReadAllBytes(Path.Combine(keys, device + ".p8")));

            // A file key cannot enforce presence: the rotation is refused and nothing changes.
            CliHarness.Outcome refused = CliHarness.Run("--estate", a, "--json", "devices", "rotate", device, "--presence");
            Assert.Equal(2, refused.Code);
            Assert.Contains("cannot enforce", refused.Err, StringComparison.Ordinal);
            Assert.Equal([device + ".p8"], Directory.EnumerateFiles(keys).Select(Path.GetFileName));

            // A rotation: a new key in the device's name, signed by the previous key and the new one, replaces the old key here.
            CliHarness.Outcome rotation = CliHarness.Ok("--estate", a, "--json", "devices", "rotate", device);
            Assert.Equal([device + ".p8"], Directory.EnumerateFiles(keys).Select(Path.GetFileName));
            Assert.NotEqual(firstKey, File.ReadAllBytes(Path.Combine(keys, device + ".p8")));
            using (JsonDocument rotated = JsonDocument.Parse(File.ReadAllText(Path.Combine(a, rotation.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)))))
            {
                JsonElement signatures = rotated.RootElement.GetProperty("signatures");
                Assert.Equal(2, signatures.GetArrayLength());
                Assert.All(signatures.EnumerateArray(), s => Assert.Equal(device, s.GetProperty("device").GetString()));
            }

            // The device signs on with its new key, the records the old key signed stay valid, and the admission stands.
            CliHarness.Outcome after = CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", op, "--body", Order(scratch, "W-2"));
            Assert.Contains(device, File.ReadAllText(Path.Combine(a, after.Json.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))), StringComparison.Ordinal);
            CliHarness.Outcome check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
            Assert.DoesNotContain("subject-stale", check.Out, StringComparison.Ordinal);
            Assert.Equal("admitted", CliHarness.Ok("--estate", a, "--json", "devices", "list").Json.EnumerateArray().Single().GetProperty("admission").GetString());

            // A machine that does not hold the key can neither rename nor rotate the device.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, elsewhere);
            CliHarness.Outcome notHere = CliHarness.Run("--estate", a, "--json", "devices", "rename", device, "--name", "Elsewhere");
            Assert.Equal(2, notHere.Code);
            Assert.Contains("holds no key", notHere.Err, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, home, elsewhere, scratch);
        }
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void AFileKeyRotatesIntoTheSecureEnclave()
    {
        if (!OperatingSystem.IsMacOS() || !SecureEnclaveKeyStore.Available())
        {
            Assert.Skip("Secure Enclave custody needs a Mac with an enclave and the vericairn-enclave helper (VERICAIRN_ENCLAVE_HELPER).");
        }

        string a = CliHarness.TempDirectory("enclave-a");
        string home = CliHarness.TempDirectory("enclave-home");
        string scratch = CliHarness.TempDirectory("enclave-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = Init(a);
            string device = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", device, "--by", op, "--note", "The operator's own laptop.");

            // Without presence, so the test never asks anyone for Touch ID; the file key is gone once the enclave key takes its place.
            CliHarness.Ok("--estate", a, "--json", "devices", "rotate", device, "--custody", "secure-enclave");
            Assert.Equal([device + ".se"], Directory.EnumerateFiles(Path.Combine(home, "keys")).Select(Path.GetFileName));
            JsonElement row = CliHarness.Ok("--estate", a, "--json", "devices", "list").Json.EnumerateArray().Single();
            Assert.Equal(SecureEnclaveCustody.Kind, row.GetProperty("custody").GetString());
            Assert.Equal("none", row.GetProperty("presence").GetString());
            Assert.True(row.GetProperty("heldHere").GetBoolean());
            CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", op, "--body", Order(scratch, "W-1"));
            CliHarness.Outcome check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, home, scratch);
        }
    }

    [Fact]
    public void TheOperatorsRecoveryKeyAdmitsAReplacementWhenEveryDeviceIsLost()
    {
        const string Passphrase = "correct horse battery staple";
        string a = CliHarness.TempDirectory("recovery-a");
        string home = CliHarness.TempDirectory("recovery-home");
        string newHome = CliHarness.TempDirectory("recovery-new-home");
        string safe = CliHarness.TempDirectory("recovery-safe");
        string scratch = CliHarness.TempDirectory("recovery-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            CliHarness.Outcome init = CliHarness.Ok("--estate", a, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Recovery journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            string op = init.Json.GetProperty("operator").GetString()!;
            string laptop = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", a, "--json", "devices", "admit", laptop, "--by", op, "--note", "The operator's own laptop.");
            string charterPath = CliHarness.WriteJson(scratch, "charter.json", $$$"""{"name":"Example","slug":"example","purpose":"Recovery journey.","operator":"{{{op}}}","recordFormat":2,"policies":{"signatures":"required","releaseAcceptance":["operator"]},"organization":{"name":"Example"}}""");
            CliHarness.Ok("--estate", a, "--json", "records", "new", "estate", "--by", op, "--body", charterPath, "--supersedes", init.Json.GetProperty("estate").GetString()!);

            // The recovery key goes to a new file outside the repository, under a passphrase of twelve characters or more.
            string keyFile = Path.Combine(safe, "recovery.key");
            Assert.Equal(2, CliHarness.RunWithInput(Passphrase + "\n", "--estate", a, "--json", "devices", "recovery", "--principal", op, "--name", "Recovery key", "--output", Path.Combine(a, "recovery.key"), "--passphrase-stdin").Code);
            Assert.Equal(2, CliHarness.RunWithInput("too short\n", "--estate", a, "--json", "devices", "recovery", "--principal", op, "--name", "Recovery key", "--output", keyFile, "--passphrase-stdin").Code);
            Assert.False(File.Exists(Path.Combine(a, "recovery.key")));
            Assert.False(File.Exists(keyFile));

            // Made once, encrypted, admitted by the laptop, and never held by this machine.
            string recovery = CliHarness.OkWithInput(Passphrase + "\n", "--estate", a, "--json", "devices", "recovery", "--principal", op, "--name", "Recovery key in the safe", "--output", keyFile, "--passphrase-stdin").Id;
            string text = File.ReadAllText(keyFile);
            Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", text, StringComparison.Ordinal);
            Assert.Contains(recovery, text, StringComparison.Ordinal);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyFile));
            }

            Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(home, "keys")), f => f.Contains(recovery, StringComparison.Ordinal));
            JsonElement row = CliHarness.Ok("--estate", a, "--json", "devices", "list").Json.EnumerateArray().Single(d => d.GetProperty("id").GetString() == recovery);
            Assert.Equal("offline", row.GetProperty("custody").GetString());
            Assert.Equal("recovery", row.GetProperty("purpose").GetString());
            Assert.Equal("admitted", row.GetProperty("admission").GetString());
            Assert.False(row.GetProperty("heldHere").GetBoolean());
            CliHarness.Outcome check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
            Assert.DoesNotContain("recovery.unreviewed", check.Out, StringComparison.Ordinal);

            // Every device is lost. On a new machine nothing admits the replacement, except the recovery key with its passphrase.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, newHome);
            string replacement = CliHarness.Ok("--estate", a, "--json", "devices", "create", "--principal", op, "--name", "Replacement laptop", "--custody", "file").Id;
            Assert.Equal(2, CliHarness.Run("--estate", a, "--json", "devices", "admit", replacement, "--by", op, "--note", "Replaces the lost laptop.").Code);
            CliHarness.Outcome wrong = CliHarness.RunWithInput("not the passphrase at all\n", "--estate", a, "--json", "devices", "admit", replacement, "--by", op, "--note", "Replaces the lost laptop.", "--recovery", keyFile, "--passphrase-stdin");
            Assert.Equal(2, wrong.Code);
            Assert.Contains("does not open", wrong.Err, StringComparison.Ordinal);
            CliHarness.Outcome admitted = CliHarness.OkWithInput(Passphrase + "\n", "--estate", a, "--json", "devices", "admit", replacement, "--by", op, "--note", "Replaces the lost laptop.", "--recovery", keyFile, "--passphrase-stdin");
            Thread.Sleep(1100); // revocation applies from its own second; the laptop's records must sit in an earlier one
            CliHarness.Outcome revoked = CliHarness.OkWithInput(Passphrase + "\n", "--estate", a, "--json", "devices", "revoke", laptop, "--by", op, "--reason", "Laptop lost.", "--recovery", keyFile, "--passphrase-stdin");

            // The replacement signs as the operator again, and both uses stay in view until the operator reviews them.
            CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", op, "--body", Order(scratch, "W-1"));
            check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
            Assert.Equal(2, check.Out.Split("recovery.unreviewed").Length - 1);
            Assert.Contains("recovery.unreviewed", CliHarness.Ok("--estate", a, "status").Out, StringComparison.Ordinal);
            string review = CliHarness.WriteJson(scratch, "review.json", """{"independence":"self","outcome":"pass","summary":"My own recovery after the laptop was lost.","findings":[]}""");
            foreach (CliHarness.Outcome use in new[] { admitted, revoked })
            {
                CliHarness.Ok("--estate", a, "--json", "records", "new", "review", "--by", op, "--subject", $"admission:{use.Id}:{use.Hash}", "--body", review);
            }

            check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
            Assert.DoesNotContain("recovery.unreviewed", check.Out, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(a, home, newHome, safe, scratch);
        }
    }

    private static string Init(string root) =>
        CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Device journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;

    private static string Order(string scratch, string key) =>
        CliHarness.WriteJson(scratch, key + ".json", $$"""{"key":"{{key}}","kind":"ordinary","title":"Signed work","intent":"Prove signing.","scope":["x"],"exitQuestion":"Signed?","inputs":[],"deliverables":["y"]}""");
}
