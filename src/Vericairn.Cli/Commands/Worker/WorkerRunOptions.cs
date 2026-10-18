// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli;

internal sealed record WorkerRunOptions(string Assignment, string Launcher, string Agent, string By, string Repository, string? Goal, string? Branch, string? Base, long? WallMinutes, string[] Settings, bool Detach, RunChoices? Choices = null);
