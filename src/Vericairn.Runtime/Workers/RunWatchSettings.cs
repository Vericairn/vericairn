// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Workers;

/// <summary>Watching thresholds fixed before launch; configured reference rates are not claims of measured harness performance.</summary>
/// <param name="NormalTokensPerMinute">The launcher's configured reference rate.</param>
/// <param name="BurnMultiplier">How far above the greater of the reference and historical median a rate must rise.</param>
/// <param name="BurnMinimumTokens">The minimum observed spend in a rate sample.</param>
/// <param name="BurnWindowSeconds">The minimum rate sample duration and sliding window.</param>
/// <param name="BurnCapPercent">The fraction of the token reservation consumed before an anomalous rate is critical.</param>
/// <param name="LoopWarning">Repeated identical invocations that need attention.</param>
/// <param name="LoopStop">Repeated identical invocations that require a stop.</param>
/// <param name="LoopWindow">How many recent invocations are compared.</param>
public sealed record RunWatchSettings(long NormalTokensPerMinute = 20000, int BurnMultiplier = 4, long BurnMinimumTokens = 10000,
    int BurnWindowSeconds = 60, int BurnCapPercent = 25, int LoopWarning = 5, int LoopStop = 10, int LoopWindow = 20);
