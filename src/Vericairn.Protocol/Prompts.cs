// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>An exact prompt offer, retaining its source, version and full definition.</summary>
public sealed record PromptRow(string Identity, string Source, string Key, string Version, string Hash, JsonElement Definition,
    bool Favorite = false, bool Hidden = false, bool Disabled = false, bool Retired = false, bool Draft = false);

/// <summary>Category-ordered prompt offers or an explicit refusal; reading creates no use.</summary>
public sealed record PromptsResponse(IReadOnlyList<PromptRow> Prompts, Problem? Problem = null);

/// <summary>Discovery filters. Unavailable entries are visible only when explicitly requested.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromptQuery(string? Person = null, string? Category = null, string? Role = null, bool Favorites = false, bool IncludeUnavailable = false);

/// <summary>Add or revise a full estate definition. Expected pins a prior revision; a new key has no expected hash.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromptSaveRequest(string By, JsonElement Definition, string? Expected = null);

/// <summary>Change one preference without changing the shared prompt. Null leaves that choice untouched.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromptPreferenceRequest(string By, string Prompt, bool? Favorite = null, bool? Hidden = null, bool? Disabled = null, string? Expected = null);

/// <summary>Copy a pinned built-in definition into the estate, preserving its source identity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromptCopyRequest(string By, string Prompt, string Expected, string Key);

/// <summary>An exact prompt selection. Values are literal comment text and cannot supply authority changes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromptSelection(string Prompt, string Expected, IReadOnlyDictionary<string, string> Values);

/// <summary>Use a prompt in exactly one new assignment, new run, or answer to an ask.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromptUseRequest(string By, PromptSelection Selection, AssignRequest? Assignment = null, RunStartRequest? Run = null, string? Ask = null)
{
    /// <summary>After a successful recorded use, also favor the prompt for the acting principal.</summary>
    public bool Favor { get; init; }
}

/// <summary>The recorded target and exact rendered comment. No text is returned for an unrecorded use.</summary>
public sealed record PromptUseResponse(bool Ok, string? Text = null, RecordWritten? Written = null, RunResponse? Run = null, Problem? Problem = null);
