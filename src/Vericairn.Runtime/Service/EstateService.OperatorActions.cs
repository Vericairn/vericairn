// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Actions = Vericairn.Records.Estate.OperatorActions;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public OperatorActionsResponse OperatorActions(string? recipient = null, string? assignment = null, CommunicationValues? view = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            JsonObject report = Actions.Derive(model);
            using JsonDocument json = JsonDocument.Parse(Actions.RenderJson(report));
            CommunicationProfile profile = Communication(recipient ?? Routing.RootOperator(model), assignment, view);
            string text = CommunicationPresentation.Protected(profile, "status", "operator actions",
                "Every recorded waiting item follows, with its reason and required action. Reading changes nothing.", Actions.RenderText(report));
            return new OperatorActionsResponse(text, json.RootElement.Clone(), model.Findings.HasErrors);
        }
    }
}
