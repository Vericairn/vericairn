// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli;

internal sealed record GoalStepOptions(string? Goal, string By, string? Repository, string[] Settings, long? WallMinutes, string? Branch, string? Base, bool Detach, RunChoices? Choices = null);
