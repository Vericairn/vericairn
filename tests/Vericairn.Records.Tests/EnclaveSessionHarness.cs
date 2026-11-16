// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// The enclave helper's test build: compiled once per test process from the product's source with VERICAIRN_SESSION_TESTS, which
/// adds a controlled clock and the other test hooks, and a key without presence made with it, so no test asks anyone for Touch ID
/// or a password. Absent on other platforms, on a Mac without a Secure Enclave and without the Swift compiler; required where
/// VERICAIRN_EXPECT_ENCLAVE is set.
/// </summary>
internal static class EnclaveSessionBuild
{
    private static readonly Lazy<string?> TestBuild = new(() => Compile("tests", ["-D", "VERICAIRN_SESSION_TESTS"]), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<string> WorkDirectoryPath = new(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vericairn-enclave-sessions-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    });

    internal static string Source => Path.Combine(RecordCompanionTests.FindRepository(), "src", "Vericairn.Enclave", "vericairn-enclave.swift");

    internal static string WorkDirectory => WorkDirectoryPath.Value;

    /// <summary>The test build's path, or skips the test where it cannot run.</summary>
    internal static string RequireTestBuild()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper exists on macOS only.");
        string? helper = TestBuild.Value;
        if (helper is null)
        {
            Assert.False(Environment.GetEnvironmentVariable("VERICAIRN_EXPECT_ENCLAVE") == "1", "Missing prerequisite: the Swift compiler and an accessible macOS Secure Enclave, required by VERICAIRN_EXPECT_ENCLAVE.");
            Assert.Skip("The helper's session tests need the Swift compiler and a Mac with a Secure Enclave.");
        }

        return helper;
    }

    /// <summary>Compiles the helper with extra compiler arguments; null when the Swift compiler or the enclave is unavailable.</summary>
    internal static string? Compile(string name, IReadOnlyList<string> extra)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        string output = Path.Combine(WorkDirectory, "vericairn-enclave-" + name);
        var arguments = new List<string> { "-o", output, Source };
        arguments.AddRange(extra);
        ProcessResult compiled;
        try
        {
            compiled = ProcessResult.Run("swiftc", arguments, environment: null, input: null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        Assert.True(compiled.Code == 0, "The helper did not compile: " + compiled.Error);
        return ProcessResult.Run(output, ["available"], environment: null, input: null).Code == 0 ? output : null;
    }
}

/// <summary>A finished process: its exit code and output.</summary>
internal sealed record ProcessResult(int Code, byte[] Output, string Error)
{
    internal string Text => Encoding.UTF8.GetString(Output).Trim();

    internal static ProcessResult Run(string file, IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment, byte[]? input)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            start.Environment.Clear();
            foreach ((string key, string value) in environment) start.Environment[key] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException(file + " did not start.");
        Task<byte[]> output = Task.Factory.StartNew(() =>
        {
            using var buffer = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(buffer);
            return buffer.ToArray();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<string> error = Task.Factory.StartNew(() => process.StandardError.ReadToEnd(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (input is not null) process.StandardInput.BaseStream.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // A process may exit without reading its input, as one refusing its arguments does.
        }
        if (!process.WaitForExit(TestTiming.HangTimeout))
        {
            process.Kill();
            Assert.Fail(file + " did not finish.");
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.Result, error.Result);
    }
}

/// <summary>
/// A key in the Secure Enclave, made with the helper's test build, and removed with its directory: without presence, so no test asks
/// anyone for Touch ID or a password, unless a test asks for one that needs presence to show that nothing it does raises a dialog.
/// </summary>
internal sealed class EnclaveSessionKey : IDisposable
{
    private readonly string directory;

    internal EnclaveSessionKey(string helper, bool presence = false)
    {
        directory = Path.Combine(EnclaveSessionBuild.WorkDirectory, "key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Blob = Path.Combine(directory, "device.se");
        ProcessResult created = ProcessResult.Run(helper, presence ? ["create", Blob, "--presence"] : ["create", Blob], environment: null, input: null);
        Assert.True(created.Code == 0, "The test key could not be made: " + created.Error);
        PublicKey = Base64Url.EncodeToString(Convert.FromBase64String(created.Text));
    }

    internal string Blob { get; }

    /// <summary>The key's public key as a device record states it: SubjectPublicKeyInfo, base64url.</summary>
    internal string PublicKey { get; }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

/// <summary>
/// A running presence session: the helper's process, the runtime's side of the protocol (one compact JSON object per line) and its
/// diagnostics. Every wait is bounded only to detect a hang; each expectation is met by a message the helper writes.
/// </summary>
internal sealed class EnclaveSession : IDisposable
{
    private readonly Process process;
    private readonly BlockingCollection<string> lines = [];
    private readonly BlockingCollection<string> errorLines = [];
    private readonly StringBuilder errors = new();
    private readonly Task errorReader;

    internal EnclaveSession(string file, IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach ((string key, string value) in environment) start.Environment[key] = value;
        process = Process.Start(start) ?? throw new InvalidOperationException("The helper did not start.");
        _ = Task.Factory.StartNew(() =>
        {
            while (process.StandardOutput.ReadLine() is string line) lines.Add(line);
            lines.CompleteAdding();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        errorReader = Task.Factory.StartNew(() =>
        {
            while (process.StandardError.ReadLine() is string line)
            {
                lock (errors) errors.Append(line).Append('\n');
                errorLines.Add(line);
            }

            errorLines.CompleteAdding();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal static EnclaveSession Start(string helper, string blob, IReadOnlyDictionary<string, string> environment) => new(helper, ["session", blob], environment);

    internal int Id => process.Id;

    /// <summary>The helper's diagnostics so far: the dialog's text in a test build, never record content.</summary>
    internal string Errors
    {
        get
        {
            lock (errors) return errors.ToString();
        }
    }

    internal void Send(JsonNode message)
    {
        using JsonDocument document = JsonDocument.Parse(message.ToJsonString());
        byte[] line = [.. CanonicalJson.SerializeUtf8(document.RootElement), (byte)'\n'];
        process.StandardInput.BaseStream.Write(line);
        process.StandardInput.BaseStream.Flush();
    }

    internal void CloseInput() => process.StandardInput.Close();

    /// <summary>Waits for the first diagnostic line that matches.</summary>
    internal string? ErrorLine(Predicate<string> match)
    {
        while (errorLines.TryTake(out string? line, TestTiming.HangTimeout))
        {
            if (match(line)) return line;
        }

        return null;
    }

    /// <summary>Kills the process this session started, by its id, leaving any child it started running.</summary>
    internal void KillShell()
    {
        process.Kill(entireProcessTree: false);
        Assert.True(process.WaitForExit(TestTiming.HangTimeout), "The shell did not exit.");
    }

    /// <summary>Expects the output to end: every process holding it has exited.</summary>
    internal void ExpectOutputEnd()
    {
        Assert.False(lines.TryTake(out string? more, TestTiming.HangTimeout) && more is not null, "The helper wrote after its session ended: " + more);
        Assert.True(lines.IsCompleted, "The helper's output did not end.");
    }

    /// <summary>The helper's next message.</summary>
    internal JsonElement Next()
    {
        if (!lines.TryTake(out string? line, TestTiming.HangTimeout))
        {
            Assert.Fail(lines.IsCompleted ? "The helper ended without answering. " + Diagnostics() : "The helper did not answer. " + Diagnostics());
        }

        using JsonDocument document = JsonDocument.Parse(line!);
        return document.RootElement.Clone();
    }

    /// <summary>Expects the next message to be of a type.</summary>
    internal JsonElement Expect(string type)
    {
        JsonElement message = Next();
        if (message.GetProperty("type").GetString() != type) Assert.Fail($"Expected {type}, the helper answered {message.GetRawText()}. {Diagnostics()}");
        return message;
    }

    internal void ExpectEnd(string reason)
    {
        JsonElement ended = Expect("ended");
        Assert.Equal(reason, ended.GetProperty("reason").GetString());
        Assert.True(process.WaitForExit(TestTiming.HangTimeout), "The helper did not exit after its session ended.");
        Assert.Equal(reason == "closed" ? 0 : 4, process.ExitCode);
        Assert.False(lines.TryTake(out string? more, TestTiming.HangTimeout) && more is not null, "The helper wrote after its session ended: " + more);
    }

    internal void ExpectRefusal(string code, string reason)
    {
        JsonElement refused = Expect("refused");
        Assert.True(code == refused.GetProperty("code").GetString(), $"Expected {code}, the helper refused {refused.GetRawText()}.");
        Assert.InRange(refused.GetProperty("detail").GetString()!.Length, 1, 200);
        ExpectEnd(reason);
    }

    // Read only when a test fails: the diagnostics reader ends with the process, so a live process gets a moment to write first.
    private string Diagnostics()
    {
        errorReader.Wait(TimeSpan.FromSeconds(1));
        return "Diagnostics: " + Errors;
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
        lines.Dispose();
        errorLines.Dispose();
    }
}

/// <summary>
/// The shared vectors of tests/fixtures/presence-sessions/vectors.json, which the helper's tests run and the runtime's checks read:
/// records materialized in their compact canonical form by the runtime's serializer, terms with the device's public key and the
/// records' hashes in place, and each session run against the helper's test build.
/// </summary>
internal sealed class SessionVectors
{
    private static readonly Lazy<SessionVectors> Shared = new(() => new SessionVectors());
    private readonly JsonElement root;

    private SessionVectors()
    {
        string path = Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "presence-sessions", "vectors.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        root = document.RootElement.Clone();
    }

    internal static SessionVectors Load() => Shared.Value;

    internal JsonElement Root => root;

    internal IEnumerable<string> SessionNames => root.GetProperty("sessions").EnumerateArray().Select(s => s.GetProperty("name").GetString()!);

    internal JsonElement Session(string name) => root.GetProperty("sessions").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    internal long Now => DateTimeOffset.ParseExact(root.GetProperty("clock").GetProperty("now").GetString()!, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds();

    /// <summary>A record's bytes: an object in the runtime's compact canonical form, or text byte for byte.</summary>
    internal byte[] RecordBytes(string name, string publicKey)
    {
        JsonElement record = root.GetProperty("records").GetProperty(name);
        if (record.TryGetProperty("text", out JsonElement text)) return Encoding.UTF8.GetBytes(Substitute(text.GetString()!, publicKey));
        using JsonDocument document = JsonDocument.Parse(Substitute(record.GetRawText(), publicKey));
        return CanonicalJson.SerializeUtf8(document.RootElement);
    }

    internal string Hash(string name, string publicKey) => ContentHashOf(RecordBytes(name, publicKey));

    internal static string ContentHashOf(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal byte[] TermsBytes(JsonElement session, string publicKey)
    {
        if (session.TryGetProperty("termsText", out JsonElement text)) return Encoding.UTF8.GetBytes(Substitute(text.GetString()!, publicKey));
        using JsonDocument document = JsonDocument.Parse(Substitute(session.GetProperty("terms").GetRawText(), publicKey));
        return CanonicalJson.SerializeUtf8(document.RootElement);
    }

    /// <summary>Puts the device's public key and the records' hashes in place of their placeholders.</summary>
    internal string Substitute(string text, string publicKey)
    {
        var builder = new StringBuilder();
        int index = 0;
        while (true)
        {
            int start = text.IndexOf("${", index, StringComparison.Ordinal);
            if (start < 0) break;
            int end = text.IndexOf('}', start);
            builder.Append(text, index, start - index);
            string name = text[(start + 2)..end];
            builder.Append(name == "publicKey" ? publicKey : name.StartsWith("hash:", StringComparison.Ordinal) ? Hash(name[5..], publicKey) : "${" + name + "}");
            index = end + 1;
        }

        builder.Append(text, index, text.Length - index);
        return builder.ToString();
    }

    /// <summary>The list fingerprint, computed as a client computes it from the nonce and the terms it received.</summary>
    internal static string Fingerprint(byte[] nonce, byte[] terms)
    {
        byte[] input = [.. "vericairn.presence-session/1"u8, 0, .. nonce, .. SHA256.HashData(terms)];
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(input)) >> 4;
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var builder = new StringBuilder();
        for (int index = 0; index < 12; index++)
        {
            if (index is 4 or 8) builder.Append('-');
            builder.Append(alphabet[(int)((value >> (55 - 5 * index)) & 31)]);
        }

        return builder.ToString();
    }

    /// <summary>Runs one shared session against the helper and checks every answer the vector states.</summary>
    internal void Run(string name, string helper, EnclaveSessionKey key)
    {
        JsonElement vector = Session(name);
        var environment = new Dictionary<string, string>
        {
            ["VERICAIRN_TEST_NOW"] = Now.ToString(CultureInfo.InvariantCulture),
            ["VERICAIRN_TEST_SCREEN"] = vector.TryGetProperty("screen", out JsonElement screen) ? screen.GetString()! : "unlocked",
        };
        if (!vector.TryGetProperty("selfCheck", out JsonElement selfCheck) || !selfCheck.GetBoolean()) environment["VERICAIRN_TEST_SKIP_SELF_CHECK"] = "1";
        if (vector.TryGetProperty("timer", out JsonElement timer) && !timer.GetBoolean()) environment["VERICAIRN_TEST_NO_TIMER"] = "1";
        if (vector.TryGetProperty("check", out JsonElement check)) environment["VERICAIRN_TEST_CHECK"] = check.GetString()!;

        byte[] terms = TermsBytes(vector, key.PublicKey);
        var bytes = new JsonArray();
        foreach (JsonElement item in vector.GetProperty("bytes").EnumerateArray())
        {
            bytes.Add(item.ValueKind == JsonValueKind.String ? Base64Url.EncodeToString(RecordBytes(item.GetString()!, key.PublicKey)) : item.GetProperty("base64url").GetString());
        }

        var open = new JsonObject
        {
            ["type"] = "open",
            ["terms"] = vector.TryGetProperty("termsBase64url", out JsonElement raw) ? raw.GetString() : Base64Url.EncodeToString(terms),
            ["bytes"] = bytes,
        };
        if (vector.TryGetProperty("openExtra", out JsonElement extra))
        {
            foreach (JsonProperty member in extra.EnumerateObject()) open[member.Name] = JsonNode.Parse(member.Value.GetRawText());
        }

        using EnclaveSession session = EnclaveSession.Start(helper, key.Blob, environment);
        session.Send(open);
        JsonElement expected = vector.GetProperty("open");
        if (expected.ValueKind == JsonValueKind.Object)
        {
            string code = expected.GetProperty("refused").GetString()!;
            if (code.StartsWith("check.", StringComparison.Ordinal)) Prompting(session, terms);
            session.ExpectRefusal(code, expected.GetProperty("ended").GetString()!);
            return;
        }

        string fingerprint = Prompting(session, terms);
        if (expected.GetString() == "opened")
        {
            JsonElement opened = session.Expect("opened");
            Assert.Equal(root.GetProperty("clock").GetProperty("now").GetString(), opened.GetProperty("openedAt").GetString());
            using JsonDocument parsed = JsonDocument.Parse(terms);
            Assert.Equal(parsed.RootElement.GetProperty("limitSeconds").GetInt32(), opened.GetProperty("limitSeconds").GetInt32());
        }

        if (vector.TryGetProperty("dialog", out JsonElement dialog))
        {
            string text = dialog.GetString()!.Replace("${fingerprint}", fingerprint, StringComparison.Ordinal);
            Assert.True(text.Length <= 200, "A dialog vector is at most 200 characters.");
            Assert.Contains("dialog: " + text + "\n", session.Errors, StringComparison.Ordinal);
        }

        bool ended = false;
        foreach (JsonElement step in vector.GetProperty("steps").EnumerateArray())
        {
            Assert.False(ended, "A step follows the session's end.");
            ended = Step(session, step, key);
        }

        if (!ended)
        {
            session.Send(new JsonObject { ["type"] = "close" });
            session.ExpectEnd("closed");
        }
    }

    private static string Prompting(EnclaveSession session, byte[] terms)
    {
        JsonElement prompting = session.Expect("prompting");
        byte[] nonce = Base64Url.DecodeFromChars(prompting.GetProperty("nonce").GetString());
        Assert.Equal(16, nonce.Length);
        string fingerprint = prompting.GetProperty("fingerprint").GetString()!;
        Assert.Equal(Fingerprint(nonce, terms), fingerprint);
        return fingerprint;
    }

    /// <summary>One step of a session; true when the session ended.</summary>
    private bool Step(EnclaveSession session, JsonElement step, EnclaveSessionKey key)
    {
        if (step.TryGetProperty("advance", out JsonElement advance))
        {
            session.Send(new JsonObject { ["type"] = "test-advance", ["seconds"] = advance.GetInt64() });
            return false;
        }

        if (step.TryGetProperty("close", out _))
        {
            session.Send(new JsonObject { ["type"] = "close" });
            session.ExpectEnd("closed");
            return true;
        }

        if (step.TryGetProperty("status", out _))
        {
            session.Send(new JsonObject { ["type"] = "status" });
            JsonElement status = session.Expect("status");
            Assert.Equal(step.GetProperty("left").GetInt32(), status.GetProperty("left").GetInt32());
            Assert.Equal(step.GetProperty("seconds").GetInt32(), status.GetProperty("seconds").GetInt32());
            return false;
        }

        if (step.TryGetProperty("signal", out JsonElement signal))
        {
            session.Send(new JsonObject { ["type"] = "test-signal", ["signal"] = signal.GetString() });
            session.ExpectEnd(step.GetProperty("ended").GetString()!);
            return true;
        }

        byte[]? record = null;
        if (step.TryGetProperty("sign", out JsonElement name))
        {
            record = RecordBytes(name.GetString()!, key.PublicKey);
            session.Send(new JsonObject { ["type"] = "sign", ["record"] = Base64Url.EncodeToString(record) });
        }
        else if (step.TryGetProperty("send", out JsonElement message))
        {
            session.Send(JsonNode.Parse(message.GetRawText())!);
        }

        if (step.TryGetProperty("refused", out JsonElement code))
        {
            session.ExpectRefusal(code.GetString()!, step.GetProperty("ended").GetString()!);
            return true;
        }

        if (step.TryGetProperty("ended", out JsonElement reason))
        {
            session.ExpectEnd(reason.GetString()!);
            return true;
        }

        JsonElement signed = session.Expect("signed");
        Assert.Equal(step.GetProperty("entry").GetInt32(), signed.GetProperty("entry").GetInt32());
        Assert.Equal(step.GetProperty("left").GetInt32(), signed.GetProperty("left").GetInt32());
        if (step.TryGetProperty("seconds", out JsonElement seconds)) Assert.Equal(seconds.GetInt32(), signed.GetProperty("seconds").GetInt32());
        string signature = signed.GetProperty("signature").GetString()!;
        Assert.Equal(64, Base64Url.DecodeFromChars(signature).Length);
        Assert.True(RecordSigning.Verify(key.PublicKey, RecordSigning.Digest(ContentHashOf(record!)), signature), "The signature does not verify over the record's content hash.");
        return false;
    }
}
