// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.4 (GOAL-08): records one output of a standing agent's occurrence, by the contributor of the assignment that carries it: a
/// possibility, an ask or a publication request. The runtime names what raised it (the goal, the occurrence, the assignment and, for an
/// agent, its run in flight) and signs it as the contributor; another principal disposes of it, and nothing it records adopts, answers or
/// publishes anything.
/// </summary>
/// <param name="By">The contributor of the assignment, who records the output.</param>
/// <param name="Assignment">The assignment carrying the occurrence, any revision of it.</param>
/// <param name="Kind">possibility, ask or publication.</param>
/// <param name="Body">
/// For a possibility: title, idea, fingerprint, sources, and optionally origin, evidence, reviewTriggers, links, draft, disclosure, extras and
/// raisesAgain. For an ask: question, recommendation, consequence and optionally options and to (a person; who decides for the goal when
/// omitted). For a publication: destination, text, why, recommendation and consequence.
/// </param>
public sealed record StandingOutputRequest(string By, string Assignment, string Kind, JsonElement Body);
