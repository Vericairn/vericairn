// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench;

/// <summary>The shell's arrangement: presentation preferences, never estate state.</summary>
public sealed record ShellLayout(bool LeftVisible, bool RightVisible, bool BottomVisible, double LeftWidth, double RightWidth, double BottomHeight)
{
    /// <summary>The useful default a user can always recover.</summary>
    public static ShellLayout Default { get; } = new(true, true, true, 240, 320, 160);
}

/// <summary>Per-user preferences under the application-support directory: the layout and the recently opened estates.</summary>
public sealed class Preferences
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string directory;

    /// <summary>Preferences under the Vericairn home.</summary>
    public Preferences() : this(Path.Combine(VericairnHome.Path(), "workbench"))
    {
    }

    /// <summary>Preferences under a directory.</summary>
    public Preferences(string directory) => this.directory = directory;

    /// <summary>The saved layout, or the default.</summary>
    public ShellLayout LoadLayout()
    {
        try
        {
            string path = Path.Combine(directory, "layout.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<ShellLayout>(File.ReadAllText(path), Options) ?? ShellLayout.Default : ShellLayout.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return ShellLayout.Default;
        }
    }

    /// <summary>Saves the layout.</summary>
    public void SaveLayout(ShellLayout layout)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(layout, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
        }
    }

    /// <summary>Recently opened estate roots, most recent first.</summary>
    public IReadOnlyList<string> Recent()
    {
        try
        {
            string path = Path.Combine(directory, "recent.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path), Options) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Remembers an estate root as the most recent.</summary>
    public void Remember(string root)
    {
        try
        {
            List<string> recent = Recent().Where(r => r != root).ToList();
            recent.Insert(0, root);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "recent.json"), JsonSerializer.Serialize(recent.Take(10).ToList(), Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
        }
    }
}
