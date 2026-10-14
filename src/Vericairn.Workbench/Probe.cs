// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vericairn.Workbench;

/// <summary>One focusable target as measured: what it is, its size in layout units and in device pixels.</summary>
public sealed record Target(string Name, string Type, double Width, double Height, double DeviceWidth, double DeviceHeight)
{
    /// <summary>Whether both dimensions reach the compact-density minimum of 24 layout units.</summary>
    public bool MeetsMinimum => Width >= 24 && Height >= 24;

    /// <summary>A splitter is a pointer convenience whose every function has a keyboard alternative; it is reported, not judged.</summary>
    public bool Exempt => Type == "GridSplitter";
}

/// <summary>A contrast measurement of one pair of color roles in one theme variant, against the minimum the roles' use requires.</summary>
public sealed record ContrastPair(string Variant, string Foreground, string Background, double Ratio, double Minimum)
{
    /// <summary>Whether the pair reaches its minimum.</summary>
    public bool Meets => Ratio >= Minimum;
}

/// <summary>
/// The accessibility probe: measures every focusable, visible target of a window at the current density and render scaling.
/// It measures what it can (target sizes, automation names, the contrast of the token values) and says what it does not (announcements by a screen reader,
/// which a person observes); its output is an observation for the platform obligations, never a claim beyond the cell it ran in.
/// </summary>
public static class Probe
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Measures the window's focusable targets.</summary>
    public static IReadOnlyList<Target> Measure(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        double scaling = window.RenderScaling;
        var targets = new List<Target>();
        foreach (InputElement element in window.GetVisualDescendants().OfType<InputElement>())
        {
            if (!element.Focusable || !element.IsEffectivelyVisible || element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
            {
                continue;
            }

            string name = AutomationProperties.GetName(element) ?? (element as Control)?.Name ?? string.Empty;
            targets.Add(new Target(name, element.GetType().Name, Math.Round(element.Bounds.Width, 2), Math.Round(element.Bounds.Height, 2), Math.Round(element.Bounds.Width * scaling, 2), Math.Round(element.Bounds.Height * scaling, 2)));
        }

        return targets;
    }

    /// <summary>
    /// Measures the contrast ratio of every pair the color roles form, in the light and the dark variant: text and muted text
    /// on both surfaces and the accent's text on the accent at 4.5 or better, the attention color on the surface at 4.5, the
    /// accent and the outline on the surface at 3 or better (the non-text minimum).
    /// </summary>
    public static IReadOnlyList<ContrastPair> Contrast(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        (string Foreground, string Background, double Minimum)[] pairs =
        [
            ("BrandText", "BrandSurface", 4.5), ("BrandText", "BrandSurfaceRaised", 4.5), ("BrandTextMuted", "BrandSurface", 4.5), ("BrandTextMuted", "BrandSurfaceRaised", 4.5),
            ("BrandAccentText", "BrandAccent", 4.5), ("BrandAttention", "BrandSurface", 4.5), ("BrandAccent", "BrandSurface", 3.0), ("BrandOutline", "BrandSurface", 3.0),
        ];
        var results = new List<ContrastPair>();
        foreach ((string variantName, Avalonia.Styling.ThemeVariant variant) in new[] { ("light", Avalonia.Styling.ThemeVariant.Light), ("dark", Avalonia.Styling.ThemeVariant.Dark) })
        {
            foreach ((string foreground, string background, double minimum) in pairs)
            {
                if (Color(application, foreground, variant) is Avalonia.Media.Color fg && Color(application, background, variant) is Avalonia.Media.Color bg)
                {
                    double lighter = Math.Max(Luminance(fg), Luminance(bg));
                    double darker = Math.Min(Luminance(fg), Luminance(bg));
                    results.Add(new ContrastPair(variantName, foreground, background, Math.Round((lighter + 0.05) / (darker + 0.05), 2), minimum));
                }
            }
        }

        return results;
    }

    private static Avalonia.Media.Color? Color(Application application, string key, Avalonia.Styling.ThemeVariant variant) =>
        application.TryGetResource(key, variant, out object? value) && value is Avalonia.Media.ISolidColorBrush brush ? brush.Color : null;

    private static double Luminance(Avalonia.Media.Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>The measurement as canonical JSON for the verification record.</summary>
    public static string Report(Window window, string cell, bool hostIsVirtual)
    {
        ArgumentNullException.ThrowIfNull(window);
        IReadOnlyList<Target> targets = Measure(window);
        IReadOnlyList<ContrastPair> contrast = Application.Current is Application application ? Contrast(application) : [];
        var document = new JsonObject
        {
            ["format"] = "vericairn.accessibility-probe/1",
            ["cell"] = cell,
            ["hostIsVirtual"] = hostIsVirtual,
            ["measuredAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["renderScaling"] = window.RenderScaling,
            ["density"] = "compact",
            ["minimumLayoutUnits"] = 24,
            ["targets"] = new JsonArray(targets.Select(t => (JsonNode)new JsonObject { ["name"] = t.Name, ["type"] = t.Type, ["width"] = t.Width, ["height"] = t.Height, ["deviceWidth"] = t.DeviceWidth, ["deviceHeight"] = t.DeviceHeight, ["meetsMinimum"] = t.MeetsMinimum, ["exempt"] = t.Exempt }).ToArray()),
            ["belowMinimum"] = targets.Count(t => !t.MeetsMinimum && !t.Exempt),
            ["exemptBelowMinimum"] = targets.Count(t => !t.MeetsMinimum && t.Exempt),
            ["contrast"] = new JsonArray(contrast.Select(c => (JsonNode)new JsonObject { ["variant"] = c.Variant, ["foreground"] = c.Foreground, ["background"] = c.Background, ["ratio"] = c.Ratio, ["minimum"] = c.Minimum, ["meets"] = c.Meets }).ToArray()),
            ["contrastBelowMinimum"] = contrast.Count(c => !c.Meets),
            ["notMeasuredHere"] = new JsonArray("screen-reader announcements (a person observes them)", "focus visibility under the platform's high-contrast setting", "the contrast of text over images or gradients (none exist in the shell)"),
        };
        return document.ToJsonString(Pretty);
    }
}
