// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>The complete operator queue, in the recipient's communication settings and as canonical vericairn.operator-actions/1 JSON. Record errors keep items visible but suppress authoring commands.</summary>
public sealed record OperatorActionsResponse(string Text, JsonElement Json, bool HasErrors);
