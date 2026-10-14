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

/// <summary>
/// The accessibility probe: measures every focusable, visible target of a window at the current density and render scaling.
/// It measures what it can (target sizes, automation names) and says what it does not (announcements by a screen reader,
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

    /// <summary>The measurement as canonical JSON for the verification record.</summary>
    public static string Report(Window window, string cell, bool hostIsVirtual)
    {
        ArgumentNullException.ThrowIfNull(window);
        IReadOnlyList<Target> targets = Measure(window);
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
            ["notMeasuredHere"] = new JsonArray("screen-reader announcements (a person observes them)", "contrast of the final token values (no values are decided yet)", "focus visibility under the platform's high-contrast setting"),
        };
        return document.ToJsonString(Pretty);
    }
}
