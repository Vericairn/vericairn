// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Data.Converters;
using Vericairn.Sdk;

namespace Vericairn.Workbench;

/// <summary>
/// From 0.9.4 (CODE-80): tells a line taken from the runtime's own words (it begins with the runtime's mark, after the log's time) from
/// a line Workbench derived itself, so the activity log and the status line show the runtime's in a style of their own.
/// </summary>
public static class PresenceLineStyle
{
    /// <summary>True for a line, with or without the log's time before it, that is the runtime's.</summary>
    public static FuncValueConverter<string?, bool> Runtime { get; } = new(PresenceNoticeReader.IsRuntimeLine);
}
