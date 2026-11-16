// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace Vericairn.Records.Signing;

/// <summary>
/// From 0.9.4 (ID-16): what the build says about presence sessions (design/presence-sessions.md § 11). A release build carries Vericairn's
/// Developer ID team identifier, the release helper's code-signing identifier and the macOS major versions its candidate's helper was
/// qualified on; a build without a team identifier, such as a build from source, is a development build, which opens no session and
/// honors <see cref="SecureEnclaveCustody.HelperVariable"/>. The values are set by the release build (DIST-21) in Directory.Build.props.
/// </summary>
/// <param name="Team">The Developer ID team identifier, ten upper-case letters and digits; null in a development build.</param>
/// <param name="HelperIdentifier">The release helper's code-signing identifier.</param>
/// <param name="MacOS">The macOS major versions the build opens sessions on; empty opens none.</param>
internal sealed record PresenceSessionBuild(string? Team, string HelperIdentifier, IReadOnlyList<int> MacOS)
{
    /// <summary>The assembly metadata that carries the team identifier.</summary>
    internal const string TeamMetadata = "VericairnTeamIdentifier";

    /// <summary>The assembly metadata that carries the release helper's code-signing identifier.</summary>
    internal const string HelperMetadata = "VericairnEnclaveHelperIdentifier";

    /// <summary>The assembly metadata that carries the qualified macOS major versions, separated by semicolons.</summary>
    internal const string MacOSMetadata = "VericairnPresenceSessionMacOS";

    /// <summary>This build's values, read from its assembly metadata.</summary>
    internal static PresenceSessionBuild Current { get; } = Read(typeof(PresenceSessionBuild).Assembly);

    /// <summary>Whether this is a release build.</summary>
    internal bool Release => Team is not null;

    /// <summary>The environment variable .NET reads its diagnostics channel's switch from, and only at a process's start.</summary>
    internal const string DiagnosticsVariable = "DOTNET_EnableDiagnostics";

    /// <summary>
    /// What a release build adds to the environment of every runtime process the command line starts (the daemon, its service): the .NET
    /// diagnostics channel off, which narrows, without closing, injection into the runtime by a process of the same user. .NET 10 reads
    /// the switch only from a process's environment at its start, not from its runtime configuration file. Empty in a development build.
    /// </summary>
    internal IReadOnlyDictionary<string, string> RuntimeEnvironment => Release
        ? new Dictionary<string, string>(StringComparer.Ordinal) { [DiagnosticsVariable] = "0" }
        : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether this process's .NET diagnostics channel is off.</summary>
    internal static bool DiagnosticsOff => Environment.GetEnvironmentVariable(DiagnosticsVariable) == "0";

    /// <summary>
    /// The release helper's designated requirement: anchored in Apple's Developer ID certificates (the Developer ID intermediate and a
    /// Developer ID Application leaf), the release team's identifier and the helper's own identifier. The hardened runtime and the
    /// absence of get-task-allow are checked from its signing information beside it.
    /// </summary>
    internal string? Requirement => Team is null ? null
        : $"anchor apple generic and identifier \"{HelperIdentifier}\" and certificate leaf[subject.OU] = \"{Team}\""
          + " and certificate 1[field.1.2.840.113635.100.6.2.6] exists and certificate leaf[field.1.2.840.113635.100.6.1.13] exists";

    /// <summary>Reads the values an assembly's metadata states; anything malformed reads as absent.</summary>
    internal static PresenceSessionBuild Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string? Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value?.Trim();
        return Parse(Metadata(TeamMetadata), Metadata(HelperMetadata), Metadata(MacOSMetadata));
    }

    /// <summary>The values from their texts: a team of ten upper-case letters and digits, an identifier of letters, digits, dots and hyphens.</summary>
    internal static PresenceSessionBuild Parse(string? team, string? helper, string? macOS)
    {
        string? validTeam = team is { Length: 10 } && team.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'Z') ? team : null;
        string identifier = helper is { Length: >= 1 and <= 100 } && helper.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? helper : SecureEnclaveCustody.HelperName;
        List<int> versions = [];
        foreach (string part in (macOS ?? string.Empty).Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int version) && version is > 0 and < 1000 && !versions.Contains(version)) versions.Add(version);
        }

        return new PresenceSessionBuild(validTeam, identifier, versions);
    }
}

/// <summary>Why a helper does not meet the release requirement, or null when it does (design/presence-sessions.md § 11).</summary>
internal interface IPresenceHelperRequirement
{
    /// <summary>The problem, in a sentence without a machine path, or null when the helper at the path meets the requirement.</summary>
    string? Problem(string helperPath, string requirement);
}

/// <summary>
/// The Security framework's static code validity check against the release helper's designated requirement, with its signing information:
/// the hardened runtime, and no get-task-allow, no permission for dyld environment variables and no disabled library validation.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class SecurityFrameworkRequirement : IPresenceHelperRequirement
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const uint StrictValidate = 1 << 4;
    private const uint CheckAllArchitectures = 1 << 0;
    private const uint SigningInformation = 1 << 1;
    private const long HardenedRuntime = 0x10000;
    private const nint SInt64 = 4;

    private static readonly string[] Unwanted =
        ["com.apple.security.get-task-allow", "com.apple.security.cs.allow-dyld-environment-variables", "com.apple.security.cs.disable-library-validation"];

    /// <inheritdoc />
    public string? Problem(string helperPath, string requirement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helperPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        IntPtr url = IntPtr.Zero, code = IntPtr.Zero, text = IntPtr.Zero, required = IntPtr.Zero, information = IntPtr.Zero;
        try
        {
            byte[] path = Encoding.UTF8.GetBytes(helperPath);
            url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, false);
            if (url == IntPtr.Zero || SecStaticCodeCreateWithPath(url, 0, out code) != 0 || code == IntPtr.Zero) return "The helper's code signature cannot be read.";
            text = CFStringCreateWithCString(IntPtr.Zero, requirement, Utf8);
            if (text == IntPtr.Zero || SecRequirementCreateWithString(text, 0, out required) != 0 || required == IntPtr.Zero) return "The release requirement cannot be formed.";
            if (SecStaticCodeCheckValidity(code, StrictValidate | CheckAllArchitectures, required) != 0)
                return "The helper is not signed with the release's Developer ID, team and identifier.";
            if (SecCodeCopySigningInformation(code, SigningInformation, out information) != 0 || information == IntPtr.Zero) return "The helper's signing information cannot be read.";
            IntPtr flags = CFDictionaryGetValue(information, Constant("kSecCodeInfoFlags"));
            if (flags == IntPtr.Zero || CFGetTypeID(flags) != CFNumberGetTypeID() || !CFNumberGetValue(flags, SInt64, out long value) || (value & HardenedRuntime) == 0)
                return "The helper is not signed with the hardened runtime.";
            IntPtr entitlements = CFDictionaryGetValue(information, Constant("kSecCodeInfoEntitlementsDict"));
            if (entitlements != IntPtr.Zero && CFGetTypeID(entitlements) == CFDictionaryGetTypeID())
            {
                foreach (string name in Unwanted)
                {
                    IntPtr key = CFStringCreateWithCString(IntPtr.Zero, name, Utf8);
                    try
                    {
                        IntPtr granted = CFDictionaryGetValue(entitlements, key);
                        if (granted != IntPtr.Zero && CFGetTypeID(granted) == CFBooleanGetTypeID() && CFBooleanGetValue(granted))
                            return "The helper carries an entitlement that lets another process attach to it or load code into it.";
                    }
                    finally
                    {
                        if (key != IntPtr.Zero) CFRelease(key);
                    }
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return "The Security framework is unavailable to check the helper.";
        }
        finally
        {
            foreach (IntPtr held in new[] { information, required, text, code, url })
            {
                if (held != IntPtr.Zero) CFRelease(held);
            }
        }
    }

    private static IntPtr Constant(string name)
    {
        IntPtr library = NativeLibrary.Load(Security);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling avoids requiring unsafe code in the project.
#pragma warning disable CA2101 // Strings are marshalled explicitly as UTF-8.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] buffer, nint length, [MarshalAs(UnmanagedType.U1)] bool directory);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(IntPtr number, nint type, out long value);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFBooleanGetValue(IntPtr boolean);

    [DllImport(CoreFoundation)]
    private static extern nuint CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern nuint CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFDictionaryGetTypeID();

    [DllImport(Security)]
    private static extern int SecStaticCodeCreateWithPath(IntPtr path, uint flags, out IntPtr code);

    [DllImport(Security)]
    private static extern int SecRequirementCreateWithString(IntPtr text, uint flags, out IntPtr requirement);

    [DllImport(Security)]
    private static extern int SecStaticCodeCheckValidity(IntPtr code, uint flags, IntPtr requirement);

    [DllImport(Security)]
    private static extern int SecCodeCopySigningInformation(IntPtr code, uint flags, out IntPtr information);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}

/// <summary>
/// The release helper's process for one presence session (design/presence-sessions.md § 7): started with posix_spawn, with an empty
/// environment and no inherited handle but its three standard streams (every other descriptor is closed in the child, whatever its
/// flags), its output read one line at a time, its diagnostics drained and kept out of every message. Killed by its id, and reaped.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class PresenceHelperProcess : IPresenceHelperChannel
{
    private const short CloseOnExecDefault = 0x4000;
    private const short SetSignalMask = 0x0008;
    private const short SetSignalDefault = 0x0004;
    private const int Kill9 = 9;

    private readonly int pid;
    private readonly AnonymousPipeServerStream input;
    private readonly AnonymousPipeServerStream output;
    private readonly AnonymousPipeServerStream errors;
    private readonly BlockingCollection<JsonElement> lines = [];
    private readonly StringBuilder diagnostics = new();
    private int killed;
    private int disposed;

    private PresenceHelperProcess(int pid, AnonymousPipeServerStream input, AnonymousPipeServerStream output, AnonymousPipeServerStream errors)
    {
        this.pid = pid;
        this.input = input;
        this.output = output;
        this.errors = errors;
        new Thread(ReadOutput) { IsBackground = true, Name = "presence helper output" }.Start();
        new Thread(ReadErrors) { IsBackground = true, Name = "presence helper diagnostics" }.Start();
    }

    /// <summary>The helper's process id.</summary>
    internal int ProcessId => pid;

    /// <summary>The helper's diagnostics so far, for tests; never record content, and never shown to a person or written to a record.</summary>
    internal string Diagnostics
    {
        get
        {
            lock (diagnostics) return diagnostics.ToString();
        }
    }

    /// <inheritdoc />
    public bool Ended => lines.IsCompleted;

    /// <summary>
    /// Starts the helper. <paramref name="environment"/> is empty in the product; only tests of the helper's test build pass its hooks.
    /// </summary>
    internal static PresenceHelperProcess Start(string helper, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helper);
        ArgumentNullException.ThrowIfNull(arguments);
        // Each pipe is made close-on-exec at both ends, so no other child of this process inherits either end; the helper gets its
        // ends as descriptors 0, 1 and 2 by duplication, and posix_spawn closes every other descriptor in it.
        var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var errors = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        IntPtr actions = IntPtr.Zero, attributes = IntPtr.Zero;
        bool actionsReady = false, attributesReady = false;
        try
        {
            Check(posix_spawn_file_actions_init(ref actions), "posix_spawn_file_actions_init");
            actionsReady = true;
            Check(posix_spawn_file_actions_adddup2(ref actions, (int)input.ClientSafePipeHandle.DangerousGetHandle(), 0), "posix_spawn_file_actions_adddup2");
            Check(posix_spawn_file_actions_adddup2(ref actions, (int)output.ClientSafePipeHandle.DangerousGetHandle(), 1), "posix_spawn_file_actions_adddup2");
            Check(posix_spawn_file_actions_adddup2(ref actions, (int)errors.ClientSafePipeHandle.DangerousGetHandle(), 2), "posix_spawn_file_actions_adddup2");
            Check(posix_spawnattr_init(ref attributes), "posix_spawnattr_init");
            attributesReady = true;
            uint none = 0;
            uint defaults = Bit(1) | Bit(2) | Bit(3) | Bit(13) | Bit(15) | Bit(20);
            Check(posix_spawnattr_setsigmask(ref attributes, ref none), "posix_spawnattr_setsigmask");
            Check(posix_spawnattr_setsigdefault(ref attributes, ref defaults), "posix_spawnattr_setsigdefault");
            Check(posix_spawnattr_setflags(ref attributes, CloseOnExecDefault | SetSignalMask | SetSignalDefault), "posix_spawnattr_setflags");
            IntPtr[] argv = Strings([helper, .. arguments]);
            IntPtr[] envp = Strings([.. (environment ?? new Dictionary<string, string>()).Select(p => p.Key + "=" + p.Value)]);
            int pid;
            try
            {
                Check(posix_spawn(out pid, helper, ref actions, ref attributes, argv, envp), "posix_spawn");
            }
            finally
            {
                foreach (IntPtr held in argv.Concat(envp).Where(p => p != IntPtr.Zero)) Marshal.FreeCoTaskMem(held);
            }

            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();
            errors.DisposeLocalCopyOfClientHandle();
            return new PresenceHelperProcess(pid, input, output, errors);
        }
        catch
        {
            input.Dispose();
            output.Dispose();
            errors.Dispose();
            throw;
        }
        finally
        {
            if (actionsReady) _ = posix_spawn_file_actions_destroy(ref actions);
            if (attributesReady) _ = posix_spawnattr_destroy(ref attributes);
        }

        static uint Bit(int signal) => 1u << (signal - 1);

        // A NULL-terminated array of UTF-8 strings, each allocated here and freed after the spawn.
        static IntPtr[] Strings(IReadOnlyList<string> values) => [.. values.Select(Marshal.StringToCoTaskMemUTF8), IntPtr.Zero];
    }

    /// <inheritdoc />
    public void Send(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);
        input.Write([.. message, (byte)'\n']);
        input.Flush();
    }

    /// <inheritdoc />
    public JsonElement? Receive(TimeSpan wait)
    {
        try
        {
            return lines.TryTake(out JsonElement message, wait < TimeSpan.Zero ? TimeSpan.Zero : wait) ? message : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (Interlocked.Exchange(ref killed, 1) == 0) _ = kill(pid, Kill9);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Kill();
        try
        {
            input.Dispose();
        }
        catch (IOException)
        {
            // A helper that exited no longer reads its input.
        }

        _ = waitpid(pid, out _, 0);
        output.Dispose();
        errors.Dispose();
    }

    private void ReadOutput()
    {
        try
        {
            using var reader = new StreamReader(output, new UTF8Encoding(false, true), false, 65536, leaveOpen: true);
            while (reader.ReadLine() is string line)
            {
                using JsonDocument document = JsonDocument.Parse(line);
                lines.Add(document.RootElement.Clone());
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or JsonException or DecoderFallbackException or InvalidOperationException)
        {
            // A line that is not one JSON object ends what the runtime reads from this helper; the session is then over.
        }
        finally
        {
            lines.CompleteAdding();
        }
    }

    private void ReadErrors()
    {
        try
        {
            using var reader = new StreamReader(errors, Encoding.UTF8, false, 4096, leaveOpen: true);
            while (reader.ReadLine() is string line)
            {
                lock (diagnostics)
                {
                    if (diagnostics.Length < 65536) diagnostics.Append(line).Append('\n');
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Diagnostics end with the helper.
        }
    }

    private static void Check(int result, string call)
    {
        if (result != 0) throw new IOException($"The helper could not be started ({call} failed with {result.ToString(CultureInfo.InvariantCulture)}).");
    }

#pragma warning disable SYSLIB1054 // Runtime marshalling avoids requiring unsafe code in the project.
#pragma warning disable CA2101 // Strings are marshalled explicitly as UTF-8.
    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_init(ref IntPtr actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_adddup2(ref IntPtr actions, int descriptor, int target);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_destroy(ref IntPtr actions);

    [DllImport("libc")]
    private static extern int posix_spawnattr_init(ref IntPtr attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setflags(ref IntPtr attributes, short flags);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setsigmask(ref IntPtr attributes, ref uint mask);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setsigdefault(ref IntPtr attributes, ref uint signals);

    [DllImport("libc")]
    private static extern int posix_spawnattr_destroy(ref IntPtr attributes);

    [DllImport("libc")]
    private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref IntPtr actions, ref IntPtr attributes, IntPtr[] argv, IntPtr[] envp);

    [DllImport("libc")]
    private static extern int kill(int pid, int signal);

    [DllImport("libc")]
    private static extern int waitpid(int pid, out int status, int options);
#pragma warning restore CA2101
#pragma warning restore SYSLIB1054
}
