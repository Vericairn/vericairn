// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public HandoffResponse Handoff(long? maxBytes = null)
    {
        lock (gate)
        {
            // Load and retain precisely one file set: a concurrent sync cannot mix two revisions of the copy.
            IReadOnlyList<RecordFile> files = session.Files();
            EstateModel model = EstateLoader.Load(session.Root, files);
            foreach (var finding in InterruptedBatches(model))
            {
                model.Findings.Error(finding.Code, finding.Location, finding.Message);
            }

            return HandoffAssembly.Build(model, files, maxBytes);
        }
    }
}
