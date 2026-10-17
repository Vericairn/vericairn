// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Portability;

/// <summary>Preflight names and their implied directories before restore creates any destination material.</summary>
internal sealed class ExportPaths(FindingList findings)
{
    // Conservatively reject case aliases even on case-sensitive volumes: exports travel between platforms.
    private readonly Dictionary<string, (string Path, bool Directory)> paths = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string path, bool directory)
    {
        string slashes = path.Replace('\\', '/');
        string relative = directory ? slashes[..^1] : slashes;
        string[] parts = relative.Split('/');
        if (string.IsNullOrWhiteSpace(relative) || slashes != path || Path.IsPathRooted(slashes)
            || path.Contains(':', StringComparison.Ordinal) || path.Any(char.IsControl)
            || parts.Any(p => p.Length == 0 || p is "." or "..")
            || OperatingSystem.IsWindows() && parts.Any(InvalidWindowsPart))
        {
            findings.Error("export.entry-path", path, $"Refusing entry '{path}': names must be safe relative paths with '/' separators and no empty, '.' or '..' components.");
        }

        // Register aliases too, so a separator or Windows spelling collision is reported as such. Unsafe
        // names already refuse the archive and can never become extraction targets.
        string prefix = "";
        string key = "";
        for (int i = 0; i < parts.Length; i++)
        {
            prefix = i == 0 ? parts[i] : prefix + "/" + parts[i];
            string normalized = OperatingSystem.IsMacOS() ? MacKey(parts[i]) : parts[i];
            if (OperatingSystem.IsWindows())
            {
                normalized = normalized.TrimEnd(' ', '.');
            }

            key = i == 0 ? normalized : key + "/" + normalized;
            bool isDirectory = directory || i < parts.Length - 1;
            string original = i == parts.Length - 1 ? (directory ? path[..^1] : path) : prefix;
            if (paths.TryGetValue(key, out (string Path, bool Directory) prior))
            {
                if (prior.Path != original || prior.Directory != isDirectory)
                {
                    findings.Error("export.path-collision", path, $"Paths '{prior.Path}' and '{original}' collide after destination path normalization or as a file and directory.");
                }
            }
            else
            {
                paths.Add(key, (original, isDirectory));
            }
        }
    }

    private static bool InvalidWindowsPart(string part)
    {
        if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith(' ') || part.EndsWith('.'))
        {
            return true;
        }

        string stem = part.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && "123456789\u00b9\u00b2\u00b3".Contains(stem[3], StringComparison.Ordinal);
    }

    [SupportedOSPlatform("macos")]
    private static string MacKey(string part)
    {
        if (part.All(c => c <= 0x7f))
        {
            return part;
        }

        // String.Normalize is a no-op under the product's invariant globalization setting. CoreFoundation
        // supplies canonical decomposition and case folding even then, covering macOS filesystem aliases.
        nint value = CFStringCreateMutable(0, 0);
        if (value == 0)
        {
            throw new InvalidOperationException("Destination path normalization could not be computed.");
        }

        try
        {
            CFStringAppendCharacters(value, part.ToCharArray(), part.Length);
            CFStringNormalize(value, 0); // kCFStringNormalizationFormD
            CFStringFold(value, 1, 0); // kCFCompareCaseInsensitive, canonical root locale
            CFStringNormalize(value, 0);
            nint length = CFStringGetLength(value);
            var characters = new char[checked((int)length)];
            CFStringGetCharacters(value, new CFRange(0, length), characters);
            return new string(characters);
        }
        finally
        {
            CFRelease(value);
        }
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

#pragma warning disable SYSLIB1054 // Runtime marshalling keeps the project free of unsafe code.
    [DllImport(CoreFoundation)] private static extern nint CFStringCreateMutable(nint allocator, nint maxLength);
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)] private static extern void CFStringAppendCharacters(nint value, [In] char[] characters, nint length);
    [DllImport(CoreFoundation)] private static extern void CFStringNormalize(nint value, int form);
    [DllImport(CoreFoundation)] private static extern void CFStringFold(nint value, nuint flags, nint locale);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(nint value);
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)] private static extern void CFStringGetCharacters(nint value, CFRange range, [Out] char[] characters);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CFRange(nint Location, nint Length);
}
