// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// ID-16 (0.9.4): the runtime's side of presence sessions in the record library (design/presence-sessions.md §§ 6, 8, 10 and 11): the
/// session's checks as the runtime applies them before it sends a record, on the same shared vectors the helper's tests run; the terms
/// it writes; its client of the helper's protocol, against an in-process stand-in on every platform; the build's release identity and
/// qualified macOS versions; the owner's setting; and, on macOS, the helper's process with an empty environment and only its standard
/// streams, and the release requirement check, which no Developer ID signature reaches here.
/// </summary>
public sealed class PresenceSessionRulesTests
{
    // Helper-only vectors: the self-check, a simulated check or screen, protocol messages the runtime never composes, another blob.
    private static readonly string[] ShellOwnVariables = ["PWD", "SHLVL", "_", "OLDPWD"];

    private static readonly string[] HelperOnly = ["selfCheck", "check", "screen", "openExtra", "termsBase64url"];

    public static TheoryData<string> RuntimeSessions => [.. SessionVectors.Load().Root.GetProperty("sessions").EnumerateArray()
        .Where(s => !HelperOnly.Any(k => s.TryGetProperty(k, out _))
            && !(s.GetProperty("open") is { ValueKind: JsonValueKind.Object } open && open.GetProperty("refused").GetString() is "terms.key")
            && !s.GetProperty("bytes").EnumerateArray().Any(b => b.ValueKind == JsonValueKind.Object)
            && !s.GetProperty("steps").EnumerateArray().Any(step => step.TryGetProperty("send", out _) || step.TryGetProperty("signal", out _)))
        .Select(s => s.GetProperty("name").GetString()!)];

    /// <summary>
    /// The runtime applies the helper's checks to every shared session a runtime can compose: each opening answers as the helper does, and
    /// each record is matched to the entry the helper signs it under with the signatures left, or refused with the helper's code.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuntimeSessions))]
    public void TheRuntimesChecksAnswerEachSharedSessionAsTheHelperDoes(string name)
    {
        SessionVectors vectors = SessionVectors.Load();
        JsonElement vector = vectors.Session(name);
        string publicKey = DeviceKey.PublicKey;
        byte[] terms = vectors.TermsBytes(vector, publicKey);
        List<byte[]> bytes = [.. vector.GetProperty("bytes").EnumerateArray().Select(item => vectors.RecordBytes(item.GetString()!, publicKey))];
        long now = vectors.Now;
        JsonElement expected = vector.GetProperty("open");
        PresenceSessionRules rules;
        try
        {
            rules = PresenceSessionRules.Open(terms, bytes, now);
        }
        catch (PresenceSessionRefusal refusal)
        {
            Assert.True(expected.ValueKind == JsonValueKind.Object, $"{name}: the runtime refused an opening the helper opens: {refusal.Code} {refusal.Message}");
            Assert.Equal(expected.GetProperty("refused").GetString(), refusal.Code);
            Assert.InRange(refusal.Message.Length, 1, 200);
            return;
        }

        Assert.True(expected.ValueKind == JsonValueKind.String, $"{name}: the runtime opened what the helper refuses: {expected.GetRawText()}");
        double elapsed = 0;
        foreach (JsonElement step in vector.GetProperty("steps").EnumerateArray())
        {
            if (step.TryGetProperty("advance", out JsonElement advance))
            {
                elapsed += advance.GetInt64();
                continue;
            }

            if (step.TryGetProperty("close", out _)) return;
            if (step.TryGetProperty("status", out _))
            {
                Assert.Equal(step.GetProperty("left").GetInt32(), rules.Left);
                Assert.Equal(step.GetProperty("seconds").GetInt32(), (int)Math.Floor(rules.LimitSeconds - elapsed));
                continue;
            }

            if (!step.TryGetProperty("sign", out JsonElement record))
            {
                // The limit ends the session by itself: the runtime counts the same limit and renews instead of sending.
                Assert.Equal("limit", step.GetProperty("ended").GetString());
                Assert.True(elapsed >= rules.LimitSeconds);
                return;
            }

            byte[] content = vectors.RecordBytes(record.GetString()!, publicKey);
            if (elapsed >= rules.LimitSeconds)
            {
                Assert.Equal("session.ended", step.GetProperty("refused").GetString());
                return;
            }

            try
            {
                int entry = rules.Match(content, now, elapsed, out PresenceRecord parsed);
                Assert.False(step.TryGetProperty("refused", out JsonElement code), $"{name}: the runtime admitted {record.GetString()}, which the helper refuses with {(step.TryGetProperty("refused", out JsonElement c) ? c.GetString() : null)}.");
                rules.Signed(parsed, entry);
                Assert.Equal(step.GetProperty("entry").GetInt32(), entry);
                Assert.Equal(step.GetProperty("left").GetInt32(), rules.Left);
            }
            catch (PresenceSessionRefusal refusal)
            {
                Assert.True(step.TryGetProperty("refused", out JsonElement code), $"{name}: the runtime refused {record.GetString()} ({refusal.Code}: {refusal.Message}), which the helper signs.");
                Assert.Equal(code.GetString(), refusal.Code);
                return;
            }
        }
    }

    [Fact]
    public void EverySharedSessionARuntimeCanComposeIsRunAgainstTheRuntimesChecks()
    {
        // The vectors the runtime leaves to the helper are only those the helper alone can raise.
        Assert.True(RuntimeSessions.Count >= 90, $"Only {RuntimeSessions.Count} shared sessions reached the runtime's checks.");
    }

    [Fact]
    public void TheFingerprintAndTheCanonicalFormMatchTheSharedVectors()
    {
        SessionVectors vectors = SessionVectors.Load();
        foreach (JsonElement item in vectors.Root.GetProperty("fingerprints").EnumerateArray())
        {
            byte[] terms = Encoding.UTF8.GetBytes(item.GetProperty("termsText").GetString()!);
            Assert.Equal(item.GetProperty("termsHash").GetString(), PresenceText.Hash(terms));
            Assert.Equal(item.GetProperty("fingerprint").GetString(), PresenceSessionRules.Fingerprint(PresenceText.Base64Url(item.GetProperty("nonce").GetString())!, terms));
        }

        foreach (JsonElement accepted in vectors.Root.GetProperty("canonical").GetProperty("accepted").EnumerateArray())
            Assert.NotNull(PresenceText.Canonical(Encoding.UTF8.GetBytes(accepted.GetProperty("text").GetString()!), out _));
        foreach (JsonElement refused in vectors.Root.GetProperty("canonical").GetProperty("refused").EnumerateArray())
        {
            byte[] bytes = refused.TryGetProperty("hex", out JsonElement hex) ? Convert.FromHexString(hex.GetString()!) : Encoding.UTF8.GetBytes(refused.GetProperty("text").GetString()!);
            Assert.True(PresenceText.Canonical(bytes, out _) is null, refused.GetProperty("why").GetString());
        }
    }

    [Fact]
    public void TheTermsTheRuntimeWritesAreCanonicalAndOpenUnderTheHelpersChecks()
    {
        SessionVectors vectors = SessionVectors.Load();
        byte[] acceptance = vectors.RecordBytes("accept", DeviceKey.PublicKey);
        byte[] reference = vectors.RecordBytes("pushFailedRef", DeviceKey.PublicKey);
        PresenceListEntry[] entries =
        [
            new() { Exact = true, Type = "acceptance", Hash = PresenceText.Hash(acceptance), Bytes = acceptance },
            new()
            {
                Type = "operation", Action = "change-push", Count = 3, AtMost = true, Statuses = ["uncertain", "canceled", "dispatched", "known-failure", "not-started", "observed-success"],
                Fixed = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["provider"] = "github-pull-requests", ["repository"] = "01J9Z3K4M5N6P7Q8R9S0T1V2W6", ["product"] = "01J9Z3K4M5N6P7Q8R9S0T1V2W7",
                    ["productHash"] = "sha256:4a0ad014f4c0131b74fc73f279421297704859445dd7aca1e530eb4642225d10", ["branch"] = "operator/01J9Z3K4M5N6P7Q8R9S0T1V2W8",
                },
            },
            new() { Type = "operation", Action = "change-observe", Count = 3, AtMost = true, Statuses = ["observed-success"], Observation = "push-head", Observes = 1 },
            new() { Type = "operation", Action = "change-observe", Count = 3, Statuses = ["observed-success"], Observation = "push-head", ObservesReference = "01M7H300000000000000000160" },
        ];
        PresenceListReference[] references = [new("01M7H300000000000000000160", "operation", PresenceText.Hash(reference), reference)];
        byte[] terms = PresenceTermsWriter.Write("operator record", PresenceText.Hash("operator-record:x"u8), "01J9Z3K4M5N6P7Q8R9S0T1V2W3", "01J9Z3K4M5N6P7Q8R9S0T1V2W4",
            DeviceKey.PublicKey, "01J9Z3K4M5N6P7Q8R9S0T1V2W5", 300, entries, references);

        Assert.NotNull(PresenceText.Canonical(terms, out _));
        PresenceSessionRules rules = PresenceSessionRules.Open(terms, PresenceTermsWriter.Bytes(entries, references), vectors.Now);
        Assert.Equal(1 + 3 + 3 + 3, rules.Total);
        Assert.True(rules.UpTo);
        Assert.Equal(["canceled", "dispatched", "known-failure", "not-started", "observed-success", "uncertain"], rules.Entries[1].Statuses);
        Assert.Equal(0, rules.Match(acceptance, vectors.Now, 0, out _));
    }

    [Fact]
    public void TheBuildIsAReleaseOnlyWithATeamIdentifierAndListsTheMacOSVersionsItQualified()
    {
        PresenceSessionBuild development = PresenceSessionBuild.Parse(null, null, null);
        Assert.False(development.Release);
        Assert.Null(development.Requirement);
        Assert.Empty(development.MacOS);
        Assert.Equal("vericairn-enclave", development.HelperIdentifier);
        Assert.Empty(development.RuntimeEnvironment);

        PresenceSessionBuild release = PresenceSessionBuild.Parse("ABCDE12345", "dev.vericairn.enclave", "15, 26;26 x 0");
        Assert.True(release.Release);
        Assert.Equal([15, 26], release.MacOS);
        Assert.Equal("""anchor apple generic and identifier "dev.vericairn.enclave" and certificate leaf[subject.OU] = "ABCDE12345" and certificate 1[field.1.2.840.113635.100.6.2.6] exists and certificate leaf[field.1.2.840.113635.100.6.1.13] exists""", release.Requirement);
        Assert.Equal("0", release.RuntimeEnvironment["DOTNET_EnableDiagnostics"]);

        // A malformed team or identifier reads as absent: a development build, and the helper's file name.
        Assert.False(PresenceSessionBuild.Parse("abcde12345", "id with spaces", "26").Release);
        Assert.Equal("vericairn-enclave", PresenceSessionBuild.Parse("ABCDE12345", "id with spaces", "26").HelperIdentifier);

        // This build, from source, is a development build: it lists no macOS version and names no team.
        Assert.False(PresenceSessionBuild.Current.Release);
        Assert.Empty(PresenceSessionBuild.Current.MacOS);
        Assert.Contains(typeof(PresenceSessionBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), a => a.Key == PresenceSessionBuild.TeamMetadata);
    }

    [Fact]
    public void OnlyADevelopmentBuildHonorsTheHelperVariable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vericairn-helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string stand = Path.Combine(directory, SecureEnclaveCustody.HelperName);
        File.WriteAllText(stand, "stand-in");
        try
        {
            // The variable's value is passed, so no other test sees this process's environment change.
            Assert.Equal(stand, SecureEnclaveCustody.HelperPath(PresenceSessionBuild.Parse(null, null, null), stand));
            // A release build uses the helper beside the command line only, whatever the variable names.
            PresenceSessionBuild release = PresenceSessionBuild.Parse("ABCDE12345", null, "26");
            Assert.NotEqual(stand, SecureEnclaveCustody.HelperPath(release, stand));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"vericairn.presence-settings/0\",\"devices\":{}}")]
    [InlineData("{\"format\":\"vericairn.presence-settings/1\",\"devices\":[]}")]
    public void ASettingIsNeverWrittenOverAFileThatDoesNotReadSoNoOtherDeviceIsLoosened(string damaged)
    {
        // CODE-80, the reviewer's probe: device B per-signature, the file damaged, then device A set per-action with a limit.
        string home = Path.Combine(Path.GetTempPath(), "vericairn-presence-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string a = "01J9Z3K4M5N6P7Q8R9S0T1V2W4", b = "01J9Z3K4M5N6P7Q8R9S0T1V2W9";
            PresenceSettings.Write(b, perSignature: true, home: home);
            File.WriteAllText(PresenceSettings.PathOf(home), damaged);
            Assert.True(PresenceSettings.Read(b, home).PerSignature);

            UnreadableSettingsException refused = Assert.Throws<UnreadableSettingsException>(() => PresenceSettings.Write(a, perSignature: false, limitSeconds: 120, home));

            Assert.Equal(damaged, File.ReadAllText(PresenceSettings.PathOf(home)));
            Assert.Equal(PresenceSettings.PathOf(home), refused.Path);
            Assert.Contains(PresenceSettings.PathOf(home), refused.Message, StringComparison.Ordinal);
            Assert.Contains("remove it to return every device on this machine to the default", refused.Message, StringComparison.Ordinal);
            PresenceSetting stillB = PresenceSettings.Read(b, home), stillA = PresenceSettings.Read(a, home);
            Assert.True(stillB.PerSignature);
            Assert.NotNull(stillB.Problem);
            Assert.True(stillA.PerSignature);
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void TheOwnersSettingIsOwnerOnlyAndAnUnreadableOneAsksOneCheckPerSignature()
    {
        string home = Path.Combine(Path.GetTempPath(), "vericairn-presence-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string device = "01J9Z3K4M5N6P7Q8R9S0T1V2W4";
            Assert.Equal(PresenceSetting.Default, PresenceSettings.Read(device, home));
            PresenceSettings.Write(device, perSignature: false, limitSeconds: 120, home);
            Assert.Equal(new PresenceSetting(false, 120), PresenceSettings.Read(device, home));
            PresenceSettings.Write("01J9Z3K4M5N6P7Q8R9S0T1V2W9", perSignature: true, home: home);
            Assert.Equal(new PresenceSetting(true, null), PresenceSettings.Read("01J9Z3K4M5N6P7Q8R9S0T1V2W9", home));
            Assert.Equal(new PresenceSetting(false, 120), PresenceSettings.Read(device, home));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PresenceSettings.PathOf(home)));
            Assert.Throws<ArgumentOutOfRangeException>(() => PresenceSettings.Write(device, false, 59, home));
            Assert.Throws<ArgumentOutOfRangeException>(() => PresenceSettings.Write(device, false, 301, home));

            File.WriteAllText(PresenceSettings.PathOf(home), "{ not json");
            PresenceSetting damaged = PresenceSettings.Read(device, home);
            Assert.True(damaged.PerSignature);
            Assert.Contains("every signature asks its own check", damaged.Problem, StringComparison.Ordinal);
            File.WriteAllText(PresenceSettings.PathOf(home), "{\"format\":\"" + PresenceSettings.Format + "\",\"devices\":{\"" + device + "\":{\"presence\":\"per-action\",\"limitSeconds\":30}}}");
            Assert.True(PresenceSettings.Read(device, home).PerSignature);
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ASignerThatReadsRecordsIsHandedTheBytesItsSignatureIsOver()
    {
        var signer = new ReadingSigner();
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft("decision", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { title = "T" }), At: DateTimeOffset.UnixEpoch), signer);
        Assert.NotNull(signer.Content);
        Assert.Equal(record.Envelope.ComputedHash, PresenceText.Hash(signer.Content));
        Assert.Equal(signer.Content, RecordAuthor.ContentBytes(record.Bytes));
        // The bytes are the envelope without hash and signatures, in the compact canonical form the helper reads.
        Assert.Equal("decision", PresenceRecord.Read(signer.Content, "record.form").Type);
    }

    [Fact]
    public void TheClientOpensSignsAndClosesOverTheProtocolAndVerifiesEverySignature()
    {
        SessionVectors vectors = SessionVectors.Load();
        JsonElement vector = vectors.Session("release submit");
        byte[] terms = vectors.TermsBytes(vector, DeviceKey.PublicKey);
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(vectors.Now));
        var helper = new PresenceHelperStandIn(DeviceKey.Sign, DeviceKey.PublicKeyBytes, clock);
        using var client = new PresenceSessionClient(helper, DeviceKey.PublicKey);
        string? shown = null;

        PresenceSessionOpening opening = client.Open(terms, [], (nonce, fingerprint) => shown = fingerprint);

        Assert.Equal(PresenceSessionRules.Fingerprint(PresenceText.Base64Url(opening.Nonce)!, terms), shown);
        Assert.Equal(300, opening.LimitSeconds);
        foreach (string name in new[] { "checkpoint", "dispatchIntent", "dispatchDispatched" })
        {
            byte[] record = vectors.RecordBytes(name, DeviceKey.PublicKey);
            PresenceSessionSignature signed = client.Sign(record);
            Assert.True(RecordSigning.Verify(DeviceKey.PublicKey, RecordSigning.Digest(PresenceText.Hash(record)), PresenceText.Base64UrlText(signed.Signature)));
        }

        Assert.Equal((2, 300), client.Status());
        client.Close();
        Assert.True(helper.Closed);
        Assert.True(client.Ended);
        Assert.Throws<PresenceSessionEnded>(() => client.Sign(vectors.RecordBytes("candidate", DeviceKey.PublicKey)));
    }

    [Theory]
    [InlineData("lock", false)]
    [InlineData("sleep", false)]
    [InlineData("limit", false)]
    [InlineData("lock", true)]
    [InlineData("sleep", true)]
    public void AnEndTheHelperWroteBeforeItsInputClosedIsReadAsItsReasonNotAsAClosedPipe(string reason, bool status)
    {
        // The helper ends a session by itself at a lock, a switch of user session (both lock), sleep or its limit, writes ended and exits:
        // the runtime's next message finds its input closed, and the client reads the end it wrote to say why.
        SessionVectors vectors = SessionVectors.Load();
        byte[] terms = vectors.TermsBytes(vectors.Session("release submit"), DeviceKey.PublicKey);
        var helper = new PresenceHelperStandIn(DeviceKey.Sign, DeviceKey.PublicKeyBytes, new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(vectors.Now)));
        using var client = new PresenceSessionClient(helper, DeviceKey.PublicKey);
        client.Open(terms, [], null);
        client.Sign(vectors.RecordBytes("checkpoint", DeviceKey.PublicKey));
        helper.EndSession(reason);

        PresenceSessionEnded ended = Assert.Throws<PresenceSessionEnded>(() =>
        {
            if (status) client.Status();
            else client.Sign(vectors.RecordBytes("dispatchIntent", DeviceKey.PublicKey));
        });

        Assert.Equal(reason, ended.Reason);
        Assert.Equal(reason == "limit" ? "session.ended" : null, ended.Code);
        Assert.Equal(reason, client.EndReason);
        Assert.True(client.Ended);
        Assert.Single(helper.SignedHashes);
    }

    [Fact]
    public void AClosedPipeWithNoEndWrittenIsReadAsTheHelpersInputClosed()
    {
        SessionVectors vectors = SessionVectors.Load();
        byte[] terms = vectors.TermsBytes(vectors.Session("release submit"), DeviceKey.PublicKey);
        var helper = new PresenceHelperStandIn(DeviceKey.Sign, DeviceKey.PublicKeyBytes, new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(vectors.Now)));
        using var client = new PresenceSessionClient(helper, DeviceKey.PublicKey);
        client.Open(terms, [], null);
        helper.Kill();

        Assert.Equal("input", Assert.Throws<PresenceSessionEnded>(() => client.Sign(vectors.RecordBytes("checkpoint", DeviceKey.PublicKey))).Reason);
    }

    [Fact]
    public void TheDiagnosticsChannelIsReadAsDotNetReadsItsSwitches()
    {
        PresenceDiagnostics Read(params (string Name, string Value)[] variables) =>
            PresenceSessionBuild.Diagnostics(name => variables.FirstOrDefault(v => v.Name == name).Value);

        Assert.Equal(new PresenceDiagnostics(false, false, null), Read());
        Assert.Equal(new PresenceDiagnostics(true, true, "DOTNET_EnableDiagnostics=0"), Read(("DOTNET_EnableDiagnostics", "0")));
        Assert.Equal(new PresenceDiagnostics(true, true, "COMPlus_EnableDiagnostics=0"), Read(("COMPlus_EnableDiagnostics", "0")));
        // The IPC switch closes the diagnostic socket and leaves the debugger's pipes.
        Assert.Equal(new PresenceDiagnostics(false, true, "DOTNET_EnableDiagnostics_IPC=0"), Read(("DOTNET_EnableDiagnostics_IPC", "0")));
        Assert.Equal(new PresenceDiagnostics(false, true, "COMPlus_EnableDiagnostics_IPC=0"), Read(("COMPlus_EnableDiagnostics_IPC", "0")));
        // The DOTNET_ form wins over the COMPlus_ one, as .NET reads them.
        Assert.Equal(new PresenceDiagnostics(false, false, null), Read(("DOTNET_EnableDiagnostics", "1"), ("COMPlus_EnableDiagnostics", "0")));
        Assert.Contains("is on; set DOTNET_EnableDiagnostics=0", Read().Describe(), StringComparison.Ordinal);
        Assert.Contains("debugger channel is on", Read(("DOTNET_EnableDiagnostics_IPC", "0")).Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AHomeWhoseSettingsCannotBeReachedAsksOneCheckPerSignature()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("Unix permissions bind only a user other than root.");
            return;
        }

        string home = Path.Combine(Path.GetTempPath(), "vericairn-presence-" + Guid.NewGuid().ToString("N"));
        const string device = "01J9Z3K4M5N6P7Q8R9S0T1V2W4";
        try
        {
            PresenceSettings.Write(device, perSignature: false, limitSeconds: 120, home);
            // A home that cannot be searched hides the file: it never reads as absent, so it never loosens presence.
            File.SetUnixFileMode(home, UnixFileMode.None);
            PresenceSetting hidden = PresenceSettings.Read(device, home);
            Assert.True(hidden.PerSignature);
            Assert.NotNull(hidden.Problem);
            File.SetUnixFileMode(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(PresenceSettings.PathOf(home), UnixFileMode.None);
            Assert.True(PresenceSettings.Read(device, home).PerSignature);
            // A home that is absent keeps the default.
            Assert.Equal(PresenceSetting.Default, PresenceSettings.Read(device, Path.Combine(home, "absent")));
        }
        finally
        {
            if (Directory.Exists(home))
            {
                File.SetUnixFileMode(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                if (File.Exists(PresenceSettings.PathOf(home))) File.SetUnixFileMode(PresenceSettings.PathOf(home), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Directory.Delete(home, recursive: true);
            }
        }
    }

    [Fact]
    public void ARefusalADeclinedCheckASilentHelperAndASignatureThatDoesNotVerifyEachEndTheSession()
    {
        SessionVectors vectors = SessionVectors.Load();
        byte[] terms = vectors.TermsBytes(vectors.Session("release submit"), DeviceKey.PublicKey);
        var clock = new SteppedClock(DateTimeOffset.FromUnixTimeSeconds(vectors.Now));

        // A record the list does not cover: the helper refuses it, and the session is over.
        var refusing = new PresenceHelperStandIn(DeviceKey.Sign, DeviceKey.PublicKeyBytes, clock);
        using (var client = new PresenceSessionClient(refusing, DeviceKey.PublicKey))
        {
            client.Open(terms, [], null);
            PresenceSessionEnded outside = Assert.Throws<PresenceSessionEnded>(() => client.Sign(vectors.RecordBytes("accept", DeviceKey.PublicKey)));
            Assert.Equal("record.outside", outside.Code);
            Assert.Equal("refusal", outside.Reason);
            Assert.True(client.Ended);
        }

        // A declined check signs nothing; it is told from every other end.
        using (var client = new PresenceSessionClient(new PresenceHelperStandIn(DeviceKey.Sign, DeviceKey.PublicKeyBytes, clock) { Check = "declined" }, DeviceKey.PublicKey))
        {
            PresenceSessionEnded declined = Assert.Throws<PresenceSessionEnded>(() => client.Open(terms, [], null));
            Assert.True(declined.Declined);
        }

        // An unanswered check ends at the wait, and the helper is killed.
        var silent = new PresenceHelperStandIn(DeviceKey.Sign, DeviceKey.PublicKeyBytes, clock) { Check = "unanswered" };
        using (var client = new PresenceSessionClient(silent, DeviceKey.PublicKey))
        {
            PresenceSessionEnded unanswered = Assert.Throws<PresenceSessionEnded>(() => client.Open(terms, [], null, TimeSpan.Zero));
            Assert.Equal("silent", unanswered.Reason);
            Assert.True(silent.Killed);
        }

        // A helper that signs with another key: the signature is never used.
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var impostor = new PresenceHelperStandIn(digest => other.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), DeviceKey.PublicKeyBytes, clock);
        using (var client = new PresenceSessionClient(impostor, DeviceKey.PublicKey))
        {
            client.Open(terms, [], null);
            Assert.Throws<PresenceSessionEnded>(() => client.Sign(vectors.RecordBytes("checkpoint", DeviceKey.PublicKey)));
            Assert.True(impostor.Killed);
        }
    }

    [Fact]
    public void OnMacOSTheHelperStartsWithAnEmptyEnvironmentAndNoHandleButItsStandardStreams()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("The release helper's process is started on macOS only.");
            return;
        }

        // A descriptor of this process without close-on-exec, which a plain fork and exec would hand on.
        using FileStream held = File.OpenRead(typeof(PresenceSessionRulesTests).Assembly.Location);
        int inheritable = dup((int)held.SafeFileHandle.DangerousGetHandle());
        Assert.True(inheritable > 4, "The descriptor's number must lie above those ls itself opens.");
        try
        {
            // The shell stands in for the helper: it reports its environment and the descriptors it holds, then echoes one line.
            using PresenceHelperProcess process = PresenceHelperProcess.Start("/bin/sh",
                ["-c", "printf '{\"env\":\"%s\",\"fds\":\"%s\"}\\n' \"$(/usr/bin/env | /usr/bin/cut -d= -f1 | /usr/bin/tr '\\n' ' ')\" \"$(/bin/ls /dev/fd | /usr/bin/tr '\\n' ' ')\"; read line; printf '%s\\n' \"$line\""]);
            JsonElement report = process.Receive(TestTiming.HangTimeout) ?? throw new InvalidOperationException("The stand-in did not answer: " + process.Diagnostics);
            // /bin/sh sets PWD, SHLVL and _ itself; nothing came from this process's environment.
            string[] variables = report.GetProperty("env").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.All(variables, v => Assert.Contains(v, ShellOwnVariables));
            // 0, 1 and 2, and the two descriptors ls opens to list them (as from a clean shell); never the one this process held open.
            int[] descriptors = [.. report.GetProperty("fds").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];
            Assert.Equal([0, 1, 2], descriptors.Where(d => d <= 2).Order());
            Assert.DoesNotContain(inheritable, descriptors);
            Assert.True(descriptors.Length <= 5, "The helper inherited more than its standard streams: " + string.Join(' ', descriptors));
            process.Send("{\"type\":\"close\"}"u8.ToArray());
            Assert.Equal("close", process.Receive(TestTiming.HangTimeout)!.Value.GetProperty("type").GetString());
        }
        finally
        {
            _ = close(inheritable);
        }
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling, as the product's own declarations.
    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int dup(int descriptor);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int descriptor);
#pragma warning restore SYSLIB1054

    [Fact]
    public void OnMacOSTheReleaseRequirementRefusesAHelperNotSignedWithTheReleasesDeveloperID()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("The release requirement is checked by the macOS Security framework.");
            return;
        }

        // No Developer ID signature exists here: an Apple-signed system tool and an unsigned file both fail the requirement, which only
        // the release candidate's signed helper can pass (its qualification, docs/presence-session-qualification.md).
        string requirement = PresenceSessionBuild.Parse("ABCDE12345", "vericairn-enclave", "26").Requirement!;
        var check = new SecurityFrameworkRequirement();
        Assert.Equal("The helper is not signed with the release's Developer ID, team and identifier.", check.Problem("/bin/ls", requirement));
        string unsigned = Path.Combine(Path.GetTempPath(), "vericairn-unsigned-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(unsigned, "#!/bin/sh\n");
        try
        {
            Assert.NotNull(check.Problem(unsigned, requirement));
        }
        finally
        {
            File.Delete(unsigned);
        }
    }

    private sealed class ReadingSigner : IRecordContentSigner
    {
        internal byte[]? Content { get; private set; }

        public string DeviceId => "01J9Z3K4M5N6P7Q8R9S0T1V2W4";

        public RecordSignature Sign(ReadOnlySpan<byte> digest) => throw new InvalidOperationException("A signer that reads records is handed them.");

        public RecordSignature SignRecord(ReadOnlySpan<byte> content, ReadOnlySpan<byte> digest)
        {
            Content = content.ToArray();
            return new RecordSignature(RecordSigning.Algorithm, DeviceId, PresenceText.Base64UrlText(DeviceKey.Sign(digest.ToArray())));
        }
    }

    /// <summary>A P-256 key standing in for the device's enclave key.</summary>
    internal static class DeviceKey
    {
        private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        internal static byte[] PublicKeyBytes { get; } = Key.ExportSubjectPublicKeyInfo();

        internal static string PublicKey { get; } = Base64Url.EncodeToString(PublicKeyBytes);

        internal static byte[] Sign(byte[] digest) => Key.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}

/// <summary>
/// ID-16 (0.9.4): the runtime's client drives the helper's own session, compiled as its test build with a key without presence, through
/// the process the runtime starts for it: the protocol's messages, the runtime's checks and the helper's agree on the shared vectors, and
/// every signature verifies. Skipped where the helper's test build cannot run.
/// </summary>
public sealed class PresenceSessionProcessTests(EnclaveSessionFixture fixture) : IClassFixture<EnclaveSessionFixture>
{
    [Fact]
    public void TheRuntimesClientDrivesTheHelpersSessionThroughItsProcess()
    {
        string helper = fixture.Helper;
        if (!OperatingSystem.IsMacOS()) return;
        SessionVectors vectors = SessionVectors.Load();
        JsonElement vector = vectors.Session("release submit");
        byte[] terms = vectors.TermsBytes(vector, fixture.Key.PublicKey);
        // The test build's hooks: a fixed clock, the self-check skipped (no Developer ID here) and an unlocked screen.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VERICAIRN_TEST_NOW"] = vectors.Now.ToString(CultureInfo.InvariantCulture), ["VERICAIRN_TEST_SKIP_SELF_CHECK"] = "1", ["VERICAIRN_TEST_SCREEN"] = "unlocked",
        };
        using PresenceHelperProcess process = PresenceHelperProcess.Start(helper, ["session", fixture.Key.Blob], environment);
        using var client = new PresenceSessionClient(process, fixture.Key.PublicKey);
        string? shown = null;
        PresenceSessionOpening opening = client.Open(terms, [], (nonce, fingerprint) => shown = fingerprint);
        Assert.Equal(PresenceSessionRules.Fingerprint(PresenceText.Base64Url(opening.Nonce)!, terms), shown);
        PresenceSessionRules rules = PresenceSessionRules.Open(terms, [], vectors.Now);
        foreach (string name in new[] { "checkpoint", "dispatchIntent", "dispatchDispatched", "dispatchSucceeded", "candidate" })
        {
            byte[] record = vectors.RecordBytes(name, fixture.Key.PublicKey);
            int expected = rules.Match(record, opening.OpenedAt, 0, out PresenceRecord parsed);
            PresenceSessionSignature signed = client.Sign(record);
            rules.Signed(parsed, expected);
            Assert.Equal(expected, signed.Entry);
            Assert.Equal(rules.Left, signed.Left);
        }

        client.Close();
        Assert.Contains("dialog: open Vericairn session " + shown + ": release submit, 5 signatures in 300 s", process.Diagnostics, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("sleep")]
    public void WhenTheHelpersSessionEndsAtALockOrSleepTheRuntimeReadsWhy(string signal)
    {
        string helper = fixture.Helper;
        if (!OperatingSystem.IsMacOS()) return;
        SessionVectors vectors = SessionVectors.Load();
        byte[] terms = vectors.TermsBytes(vectors.Session("release submit"), fixture.Key.PublicKey);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VERICAIRN_TEST_NOW"] = vectors.Now.ToString(CultureInfo.InvariantCulture), ["VERICAIRN_TEST_SKIP_SELF_CHECK"] = "1", ["VERICAIRN_TEST_SCREEN"] = "unlocked",
        };
        using PresenceHelperProcess process = PresenceHelperProcess.Start(helper, ["session", fixture.Key.Blob], environment);
        using var client = new PresenceSessionClient(process, fixture.Key.PublicKey);
        client.Open(terms, [], null);
        client.Sign(vectors.RecordBytes("checkpoint", fixture.Key.PublicKey));
        // The test build's signal stands in for the screen locking or the Mac sleeping: the helper writes ended and exits.
        process.Send(Encoding.UTF8.GetBytes("{\"signal\":\"" + signal + "\",\"type\":\"test-signal\"}"));

        PresenceSessionEnded ended = Assert.Throws<PresenceSessionEnded>(() => client.Sign(vectors.RecordBytes("dispatchIntent", fixture.Key.PublicKey)));

        Assert.Equal(signal, ended.Reason);
        Assert.Null(ended.Code);
    }

    [Fact]
    public void ARecordSignedWithItsOwnCheckIsReadAndHashedByTheHelpersSignRecord()
    {
        string helper = fixture.Helper;
        if (!OperatingSystem.IsMacOS()) return;
        string home = Path.Combine(EnclaveSessionBuild.WorkDirectory, "store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SecureEnclaveKeyStore(home, presence: false, helper: helper);
            string device = Identity.Ulid.NewUlid().Value;
            string publicKey = Base64Url.EncodeToString(store.Create(device));
            AuthoredRecord record = RecordAuthor.Create(new RecordDraft("decision", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", JsonSerializer.SerializeToElement(new { title = "T" })), new StoreSigner(store, device));
            Assert.True(RecordSigning.Verify(publicKey, RecordSigning.Digest(record.Envelope.ComputedHash), record.Envelope.Signatures[0].Signature));
            // Bytes that are not a record's compact canonical form are refused by the helper before any prompt.
            IDeviceKeyStore keys = store;
            Assert.Throws<InvalidOperationException>(() => keys.SignRecord(device, "{\"a\": 1}"u8, SHA256.HashData("{\"a\": 1}"u8), "reason"));
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
