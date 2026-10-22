// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public WriteOutcome WorkNew(WorkNewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = new JsonArray();
        foreach (string? input in request.Inputs ?? [])
        {
            int separator = input?.IndexOf(':', StringComparison.Ordinal) ?? -1;
            // Preserve malformed values for the record validator, including a missing or extra separator.
            inputs.Add(new JsonObject
            {
                ["type"] = separator < 0 ? input : input![..separator],
                ["id"] = separator < 0 ? "" : input![(separator + 1)..],
            });
        }

        var body = new JsonObject
        {
            ["scope"] = JsonSerializer.SerializeToNode(request.Scope ?? []),
            ["inputs"] = inputs,
            ["deliverables"] = JsonSerializer.SerializeToNode(request.Deliverables ?? []),
        };
        foreach ((string name, string? value) in new (string, string?)[]
        {
            ("key", request.Key), ("kind", request.Kind), ("title", request.Title),
            ("intent", request.Intent), ("exitQuestion", request.ExitQuestion),
        })
        {
            if (value is not null) body[name] = value;
        }
        if (request.After is not null) body["after"] = JsonSerializer.SerializeToNode(request.After);

        return NewRecord(new NewRecordRequest("work-order", request.By, JsonSerializer.SerializeToElement(body)));
    }
}
