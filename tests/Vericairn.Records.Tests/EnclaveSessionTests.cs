// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>The helper's test build and a key without presence, shared by one test class and removed after it.</summary>
public sealed class EnclaveSessionFixture : IDisposable
{
    private readonly Lazy<(string Helper, EnclaveSessionKey Key)> state = new(() =>
    {
        string helper = EnclaveSessionBuild.RequireTestBuild();
        return (helper, new EnclaveSessionKey(helper));
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    internal string Helper => state.Value.Helper;

    internal EnclaveSessionKey Key => state.Value.Key;

    public void Dispose()
    {
        if (state.IsValueCreated) state.Value.Key.Dispose();
    }
}

/// <summary>
/// ID-15 (0.9.4): presence sessions in the enclave helper (design/presence-sessions.md §§ 6 to 9 and 11). One helper process, after
/// one presence check, signs only records its announced list covers, by the protocol's checks, for at most its limit, ending at its
/// first refusal, a lock, sleep, its input's close or its parent's exit, with a dialog whose text is its own. The tests run on macOS
/// with keys without presence, so the session's checks need no prompt; the shared vectors are the ones the runtime reads, and what
/// automated tests cannot reach is the qualification of the release candidate's signed helper (docs/presence-session-qualification.md).
/// </summary>
public class EnclaveSessionTests(EnclaveSessionFixture fixture) : IClassFixture<EnclaveSessionFixture>
{
    private static readonly string[] RefusalCodes =
    [
        "terms.invalid", "terms.not-allowed", "terms.size", "terms.key", "terms.bytes", "helper.unsigned", "check.declined", "check.unavailable",
        "record.form", "record.author", "record.outside", "record.reference", "record.time", "session.ended",
    ];

    // The input's close and the parent's exit are raised by the tests below, outside the shared vectors.
    private static readonly string[] VectorEnds = ["closed", "declined", "limit", "lock", "refusal", "sleep"];

    public static TheoryData<string> Sessions => [.. SessionVectors.Load().SessionNames];

    [Theory]
    [MemberData(nameof(Sessions))]
    public void EachSharedSessionRunsAgainstTheHelper(string name)
    {
        SessionVectors.Load().Run(name, fixture.Helper, fixture.Key);
    }

    [Fact]
    public void TheSharedVectorsCoverEveryRefusalCodeAndEveryEndATestCanRaise()
    {
        SessionVectors vectors = SessionVectors.Load();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var ends = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement session in vectors.Root.GetProperty("sessions").EnumerateArray())
        {
            Assert.True(names.Add(session.GetProperty("name").GetString()!), "Each shared session has its own name.");
            JsonElement open = session.GetProperty("open");
            if (open.ValueKind == JsonValueKind.Object)
            {
                codes.Add(open.GetProperty("refused").GetString()!);
                ends.Add(open.GetProperty("ended").GetString()!);
            }

            foreach (JsonElement step in session.GetProperty("steps").EnumerateArray())
            {
                if (step.TryGetProperty("refused", out JsonElement code)) codes.Add(code.GetString()!);
                if (step.TryGetProperty("ended", out JsonElement end)) ends.Add(end.GetString()!);
                if (step.TryGetProperty("close", out _)) ends.Add("closed");
            }

            if (session.TryGetProperty("dialog", out JsonElement dialog)) Assert.True(dialog.GetString()!.Replace("${fingerprint}", "XXXX-XXXX-XXXX", StringComparison.Ordinal).Length <= 200);
        }

        Assert.Equal(RefusalCodes.Order(StringComparer.Ordinal), codes.Order(StringComparer.Ordinal));
        Assert.Equal(VectorEnds, ends.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheDesignsFingerprintVectorHoldsForTheRuntimeAndTheHelper()
    {
        SessionVectors vectors = SessionVectors.Load();
        foreach (JsonElement vector in vectors.Root.GetProperty("fingerprints").EnumerateArray())
        {
            byte[] terms = Encoding.UTF8.GetBytes(vector.GetProperty("termsText").GetString()!);
            byte[] nonce = Base64Url.DecodeFromChars(vector.GetProperty("nonce").GetString());
            Assert.Equal(1022, terms.Length);
            Assert.Equal(vector.GetProperty("termsHash").GetString(), SessionVectors.ContentHashOf(terms));
            Assert.Equal(vector.GetProperty("fingerprint").GetString(), SessionVectors.Fingerprint(nonce, terms));
            using JsonDocument document = JsonDocument.Parse(terms);
            Assert.Equal(terms, CanonicalJson.SerializeUtf8(document.RootElement));
            if (!OperatingSystem.IsMacOS()) continue;

            string helper = fixture.Helper;
            string path = Path.Combine(EnclaveSessionBuild.WorkDirectory, "fingerprint-terms-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllBytes(path, terms);
            try
            {
                ProcessResult printed = ProcessResult.Run(helper, ["fingerprint", path, vector.GetProperty("nonce").GetString()!], environment: null, input: null);
                Assert.Equal(0, printed.Code);
                Assert.Equal(vector.GetProperty("fingerprint").GetString(), printed.Text);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void TheHelpersCanonicalParserAgreesWithTheRuntimesSerializer()
    {
        SessionVectors vectors = SessionVectors.Load();
        JsonElement canonical = vectors.Root.GetProperty("canonical");
        foreach (JsonElement accepted in canonical.GetProperty("accepted").EnumerateArray())
        {
            byte[] bytes = Bytes(accepted);
            using JsonDocument document = CanonicalJson.Parse(bytes);
            Assert.True(bytes.AsSpan().SequenceEqual(CanonicalJson.SerializeUtf8(document.RootElement)), "The runtime's serializer does not keep " + accepted.GetProperty("why").GetString());
        }

        foreach (JsonElement refused in canonical.GetProperty("refused").EnumerateArray())
        {
            Assert.False(RuntimeKeeps(Bytes(refused)), "The runtime's serializer keeps " + refused.GetProperty("why").GetString());
        }

        if (!OperatingSystem.IsMacOS()) return;
        string helper = fixture.Helper;
        foreach (JsonElement accepted in canonical.GetProperty("accepted").EnumerateArray())
        {
            byte[] bytes = Bytes(accepted);
            ProcessResult read = ProcessResult.Run(helper, ["canonical"], environment: null, input: bytes);
            Assert.True(read.Code == 0, "The helper refuses " + accepted.GetProperty("why").GetString() + ": " + read.Error);
            Assert.Equal("canonical " + SessionVectors.ContentHashOf(bytes), read.Text);
        }

        foreach (JsonElement refused in canonical.GetProperty("refused").EnumerateArray())
        {
            ProcessResult read = ProcessResult.Run(helper, ["canonical"], environment: null, input: Bytes(refused));
            Assert.True(read.Code == 5, "The helper keeps " + refused.GetProperty("why").GetString());
            Assert.StartsWith("not canonical: ", read.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClosingItsInputEndsTheSession()
    {
        string helper = fixture.Helper;
        using EnclaveSession session = EnclaveSession.Start(helper, fixture.Key.Blob, TestEnvironment());
        OpenBestCase(session);
        session.Expect("prompting");
        session.Expect("opened");
        session.CloseInput();
        session.ExpectEnd("input");
    }

    [Fact]
    public void ItsParentsExitEndsTheSessionWhileItsInputStaysOpen()
    {
        string helper = fixture.Helper;
        // The helper runs as the child of a shell that hands it this test's pipes, then exits when killed: the helper's input stays open,
        // so only its watch on its parent can end the session. The shell writes the helper's process id first.
        using var session = new EnclaveSession("/bin/sh", ["-c", "\"$0\" session \"$1\" <&0 & echo \"helper $!\" >&2; wait", helper, fixture.Key.Blob], TestEnvironment());
        int child = session.ErrorLine(line => line.StartsWith("helper ", StringComparison.Ordinal)) is string line
            ? int.Parse(line["helper ".Length..], CultureInfo.InvariantCulture) : 0;
        Assert.NotEqual(0, child);
        try
        {
            OpenBestCase(session);
            session.Expect("prompting");
            session.Expect("opened");
            session.KillShell();
            JsonElement ended = session.Expect("ended");
            Assert.Equal("parent", ended.GetProperty("reason").GetString());
            session.ExpectOutputEnd();
        }
        finally
        {
            try
            {
                using var orphan = System.Diagnostics.Process.GetProcessById(child);
                if (!orphan.HasExited) orphan.Kill();
            }
            catch (ArgumentException)
            {
            }
        }
    }

    [Fact]
    public void OnceItsCheckHasPassedTheSessionNeverShowsADialog()
    {
        // A key that needs presence, and a check the test hook reports as passed without evaluating the context: a session whose
        // context does not cover its key. Its first signature is refused at once, with no dialog, because the helper makes the
        // context non-interactive once its check has passed. The helper's probe of the key raises none either: prompting arrives.
        string helper = fixture.Helper;
        using var presence = new EnclaveSessionKey(helper, presence: true);
        Dictionary<string, string> environment = TestEnvironment();
        environment["VERICAIRN_TEST_CHECK"] = "passed";
        using EnclaveSession session = EnclaveSession.Start(helper, presence.Blob, environment);
        SessionVectors vectors = SessionVectors.Load();
        JsonElement vector = vectors.Session("operator record, the best case");
        var bytes = new JsonArray();
        foreach (JsonElement name in vector.GetProperty("bytes").EnumerateArray()) bytes.Add(Base64Url.EncodeToString(vectors.RecordBytes(name.GetString()!, presence.PublicKey)));
        session.Send(new JsonObject { ["type"] = "open", ["terms"] = Base64Url.EncodeToString(vectors.TermsBytes(vector, presence.PublicKey)), ["bytes"] = bytes });
        session.Expect("prompting");
        session.Expect("opened");
        session.Send(new JsonObject { ["type"] = "sign", ["record"] = Base64Url.EncodeToString(vectors.RecordBytes("accept", presence.PublicKey)) });
        session.ExpectRefusal("check.unavailable", "refusal");
    }

    [Fact]
    public void AReleaseBuildCarriesNoTestHook()
    {
        // The same list tools/presence-session-qualification.py's build command reads in the release candidate's helper.
        string[] markers = ["VERICAIRN_TEST", "VERICAIRN_SESSION_TESTS", "test-advance", "test-signal", "testEnvironment", "testMessage"];
        byte[] tests = File.ReadAllBytes(fixture.Helper);
        Assert.Contains(markers, marker => tests.AsSpan().IndexOf(Encoding.ASCII.GetBytes(marker)) >= 0);
        string release = EnclaveSessionBuild.Compile("release", ["-O"]) ?? throw new InvalidOperationException("The release build did not compile.");
        byte[] content = File.ReadAllBytes(release);
        foreach (string marker in markers) Assert.True(content.AsSpan().IndexOf(Encoding.ASCII.GetBytes(marker)) < 0, "The release build carries " + marker + ".");
        Assert.Equal(2, ProcessResult.Run(release, ["canonical"], environment: null, input: "{}"u8.ToArray()).Code);
        Assert.Equal(2, ProcessResult.Run(release, ["fingerprint", release, "AAAAAAAAAAAAAAAAAAAAAA"], environment: null, input: null).Code);
    }

    // The macOS helper opens a per-signature dialog with the record's label.
    [Trait("Journey", "J135")]
    [Fact]
    public void SignRecordReadsTheRecordAndOpensItsDialogWithTheHelpersLabel()
    {
        string helper = fixture.Helper;
        SessionVectors vectors = SessionVectors.Load();
        byte[] record = vectors.RecordBytes("accept", fixture.Key.PublicKey);
        string hash = SessionVectors.ContentHashOf(record);
        string label = "sign Vericairn record acceptance " + hash[7..19];

        ProcessResult signed = ProcessResult.Run(helper, ["sign-record", fixture.Key.Blob, "Signature 1 of 2: acceptance FIX-1, accepts the result at its exact hash"], environment: null, input: record);
        Assert.True(signed.Code == 0, signed.Error);
        string signature = Base64Url.EncodeToString(Convert.FromBase64String(signed.Text));
        Assert.True(RecordSigning.Verify(fixture.Key.PublicKey, RecordSigning.Digest(hash), signature), "sign-record signs the content hash it computed itself.");
        Assert.Contains("dialog: " + label + ": Signature 1 of 2: acceptance FIX-1, accepts the result at its exact hash\n", signed.Error, StringComparison.Ordinal);

        Assert.Contains("dialog: " + label + "\n", ProcessResult.Run(helper, ["sign-record", fixture.Key.Blob], environment: null, input: record).Error, StringComparison.Ordinal);
        Assert.Contains("dialog: " + label + ": one line only\n", ProcessResult.Run(helper, ["sign-record", fixture.Key.Blob, "one\nline\r\u0007only"], environment: null, input: record).Error, StringComparison.Ordinal);
        string cut = Dialog(ProcessResult.Run(helper, ["sign-record", fixture.Key.Blob, "a sentence " + new string('x', 400)], environment: null, input: record).Error);
        Assert.Equal(200, cut.Length);
        Assert.StartsWith(label + ": a sentence xxx", cut, StringComparison.Ordinal);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);

        foreach (string name in new[] { "formUnsorted", "formFraction", "formSpace", "notRecord", "withHash" })
        {
            ProcessResult refused = ProcessResult.Run(helper, ["sign-record", fixture.Key.Blob, "a sentence"], environment: null, input: vectors.RecordBytes(name, fixture.Key.PublicKey));
            Assert.Equal(5, refused.Code);
            Assert.StartsWith("record.form: ", refused.Error, StringComparison.Ordinal);
            Assert.Empty(refused.Output);
        }
    }

    [Fact]
    public void TheDigestSignatureSaysTheHelperCannotReadWhatItSigns()
    {
        string helper = fixture.Helper;
        string hash = "sha256:" + new string('d', 64);
        ProcessResult signed = ProcessResult.Run(helper, ["sign", fixture.Key.Blob, hash[7..], "sign the pairing proof"], environment: null, input: null);
        Assert.True(signed.Code == 0, signed.Error);
        Assert.True(RecordSigning.Verify(fixture.Key.PublicKey, RecordSigning.Digest(hash), Base64Url.EncodeToString(Convert.FromBase64String(signed.Text))));
        Assert.Equal("sign an unread Vericairn digest: sign the pairing proof", Dialog(signed.Error));
        Assert.Equal("sign an unread Vericairn digest", Dialog(ProcessResult.Run(helper, ["sign", fixture.Key.Blob, hash[7..]], environment: null, input: null).Error));
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void OnlyAHelperSignedForTheReleaseOpensASession()
    {
        string helper = fixture.Helper;
        string identity = Path.Combine(EnclaveSessionBuild.WorkDirectory, "release-identity.json");
        File.WriteAllText(identity, """{"identifier":"site.vericairn.enclave","team":"ABCDE12345"}""");
        string[] section = ["-Xlinker", "-sectcreate", "-Xlinker", "__TEXT", "-Xlinker", "__vericairn", "-Xlinker", identity];
        string tests = EnclaveSessionBuild.Compile("release-identity-tests", ["-D", "VERICAIRN_SESSION_TESTS", .. section]) ?? throw new InvalidOperationException("The test build did not compile.");
        string release = EnclaveSessionBuild.Compile("release-identity", section) ?? throw new InvalidOperationException("The release build did not compile.");
        string entitlements = Path.Combine(EnclaveSessionBuild.WorkDirectory, "get-task-allow.plist");
        File.WriteAllText(entitlements, """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict><key>com.apple.security.get-task-allow</key><true/></dict></plist>
            """);

        // The test build's own build (no release identity) is a development build; the shared vector "a development build" shows it.
        // With the release identity, only the release's Developer ID anchor passes, which no test holds: the linker's signature fails.
        Assert.Contains("Developer ID", SelfCheck(tests, ad: false), StringComparison.Ordinal);
        // An ad hoc signature stands in for the anchor in the test build, so the checks after the requirement are exercised.
        Assert.Contains("hardened runtime", SelfCheck(Signed(tests, "plain", "-i", "site.vericairn.enclave"), ad: true), StringComparison.Ordinal);
        Assert.Contains("entitlement", SelfCheck(Signed(tests, "debuggable", "-o", "runtime", "-i", "site.vericairn.enclave", "--entitlements", entitlements), ad: true), StringComparison.Ordinal);
        Assert.Contains("Developer ID", SelfCheck(Signed(tests, "other", "-o", "runtime", "-i", "site.vericairn.other"), ad: true), StringComparison.Ordinal);
        Assert.Null(SelfCheck(Signed(tests, "hardened", "-o", "runtime", "-i", "site.vericairn.enclave"), ad: true));

        // The release build has none of the test hooks: the ad hoc stand-in and the skipped self-check are ignored, and it reads no
        // test-only command.
        string hardened = Signed(release, "release-hardened", "-o", "runtime", "-i", "site.vericairn.enclave");
        Assert.Contains("Developer ID", SelfCheck(hardened, ad: true, skip: true), StringComparison.Ordinal);
        Assert.Equal(2, ProcessResult.Run(hardened, ["canonical"], environment: null, input: "{}"u8.ToArray()).Code);
    }

    /// <summary>The detail of the helper.unsigned refusal an open gets, or null when the self-check passed and the terms were read.</summary>
    private string? SelfCheck(string helper, bool ad, bool skip = false)
    {
        var environment = new Dictionary<string, string>();
        if (ad) environment["VERICAIRN_TEST_ANCHOR"] = "ad-hoc";
        if (skip) environment["VERICAIRN_TEST_SKIP_SELF_CHECK"] = "1";
        using EnclaveSession session = EnclaveSession.Start(helper, fixture.Key.Blob, environment);
        session.Send(new JsonObject { ["type"] = "open", ["terms"] = "e30", ["bytes"] = new JsonArray() });
        JsonElement refused = session.Expect("refused");
        string code = refused.GetProperty("code").GetString()!;
        session.ExpectEnd("refusal");
        if (code == "terms.invalid") return null;
        Assert.Equal("helper.unsigned", code);
        return refused.GetProperty("detail").GetString();
    }

    [SupportedOSPlatform("macos")]
    private static string Signed(string helper, string name, params string[] options)
    {
        string copy = helper + "-" + name;
        File.Copy(helper, copy, overwrite: true);
        File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        ProcessResult signed = ProcessResult.Run("codesign", ["-s", "-", "-f", .. options, copy], environment: null, input: null);
        Assert.True(signed.Code == 0, "codesign failed: " + signed.Error);
        return copy;
    }

    private static string Dialog(string diagnostics) =>
        diagnostics.Split('\n').Single(l => l.StartsWith("dialog: ", StringComparison.Ordinal))["dialog: ".Length..];

    private static Dictionary<string, string> TestEnvironment() => new()
    {
        ["VERICAIRN_TEST_NOW"] = SessionVectors.Load().Now.ToString(CultureInfo.InvariantCulture),
        ["VERICAIRN_TEST_SKIP_SELF_CHECK"] = "1",
        ["VERICAIRN_TEST_SCREEN"] = "unlocked",
    };

    private void OpenBestCase(EnclaveSession session)
    {
        SessionVectors vectors = SessionVectors.Load();
        JsonElement vector = vectors.Session("operator record, the best case");
        var bytes = new JsonArray();
        foreach (JsonElement name in vector.GetProperty("bytes").EnumerateArray()) bytes.Add(Base64Url.EncodeToString(vectors.RecordBytes(name.GetString()!, fixture.Key.PublicKey)));
        session.Send(new JsonObject { ["type"] = "open", ["terms"] = Base64Url.EncodeToString(vectors.TermsBytes(vector, fixture.Key.PublicKey)), ["bytes"] = bytes });
    }

    private static byte[] Bytes(JsonElement vector) =>
        vector.TryGetProperty("hex", out JsonElement hex) ? Convert.FromHexString(hex.GetString()!) : Encoding.UTF8.GetBytes(vector.GetProperty("text").GetString()!);

    private static bool RuntimeKeeps(byte[] bytes)
    {
        try
        {
            using JsonDocument document = CanonicalJson.Parse(bytes);
            return bytes.AsSpan().SequenceEqual(CanonicalJson.SerializeUtf8(document.RootElement));
        }
        catch (Exception exception) when (exception is CanonicalJsonException or InvalidOperationException or ArgumentException or JsonException)
        {
            return false;
        }
    }
}
