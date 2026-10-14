// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// Typing for the headless tests. The headless platform delivers raw key and text events to the window root rather than to
/// the focused control, so key presses prove the window's routes (accelerators, Enter on lists, Tab, focus landing) while
/// text is delivered to the focused control as the routed text-input event a platform's keyboard raises on it.
/// </summary>
internal static class Keys
{
    /// <summary>Focuses the control with an automation name (the platform's Tab traversal is not reproduced headless from a focused control).</summary>
    public static void Focus(Window window, string automationName)
    {
        window.UpdateLayout();
        InputElement target = window.GetVisualDescendants().OfType<InputElement>().First(i => Avalonia.Automation.AutomationProperties.GetName(i) == automationName && i.IsEffectivelyVisible);
        target.Focus();
    }

    /// <summary>Types text into the focused control, or into the named control after checking it is focused.</summary>
    public static void Type(Window window, string text, string? automationName = null)
    {
        // The control's own focus state is the reliable source headless; the focus manager's query answers only for an activated window,
        // and a control focused earlier can keep its focused state until the manager learns of the change.
        InputElement? focused = automationName is null
            ? window.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(i => i.IsFocused) ?? window.FocusManager?.GetFocusedElement() as InputElement
            : window.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(i => Avalonia.Automation.AutomationProperties.GetName(i) == automationName && i.IsFocused);
        if (focused is null)
        {
            throw new InvalidOperationException($"Nothing is focused{(automationName is null ? string.Empty : " under the name " + automationName)}; typing has nowhere to go.");
        }

        focused.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = focused });
    }
}
