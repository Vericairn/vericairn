// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Create a work order from fields; the runtime uses the same validation and signing as a new work-order record.</summary>
/// <param name="By">Authoring principal.</param>
/// <param name="Key">Short upper-case work-order key.</param>
/// <param name="Title">Short title.</param>
/// <param name="Intent">Why the work exists.</param>
/// <param name="Scope">What is in scope; omitted means an empty list, which validation refuses.</param>
/// <param name="ExitQuestion">The question whose honest yes closes the work.</param>
/// <param name="Deliverables">What will exist when the work is done; omitted means an empty list, which validation refuses.</param>
/// <param name="Kind">Ordinary work by default, or milestone.</param>
/// <param name="After">Work-order record ids this work starts after; omitted means no dependencies.</param>
/// <param name="Inputs">Input references as type:id; omitted means an empty list.</param>
public sealed record WorkNewRequest(string By, string? Key = null, string? Title = null, string? Intent = null,
    IReadOnlyList<string>? Scope = null, string? ExitQuestion = null, IReadOnlyList<string>? Deliverables = null,
    string? Kind = "ordinary", IReadOnlyList<string>? After = null, IReadOnlyList<string>? Inputs = null);
