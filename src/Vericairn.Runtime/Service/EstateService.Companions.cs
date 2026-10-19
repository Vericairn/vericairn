// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Reading;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <summary>Installed reader coverage and exact declared destinations; no files or records are written.</summary>
    public CompanionsResponse Companions()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return RequestDeviceRefusal(model) is not null || model.Findings.HasErrors
                ? new(1, "unavailable", null, [], [], [], "The complete permitted reader basis is unavailable.")
                : CompanionReader.Coverage(model, ReadCompanionManifest());
        }
    }

    /// <summary>Renders only after current access and the complete source selection have been resolved.</summary>
    public CompanionResponse CompanionRender(CompanionRequest request) => ReadCompanion(request, true);

    /// <summary>Inspects the same exact source basis without invoking the generator.</summary>
    public CompanionResponse CompanionSources(CompanionRequest request) => ReadCompanion(request, false);

    private CompanionResponse ReadCompanion(CompanionRequest request, bool render)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            return RequestDeviceRefusal(model) is not null || model.Findings.HasErrors
                ? CompanionResponse.Unavailable("The complete permitted reader basis is unavailable.")
                : CompanionReader.Read(model, request, render, ReadCompanionManifest);
        }
    }

    private byte[] ReadCompanionManifest() => File.ReadAllBytes(GeneratedDocumentVerification.SafeFile(session.Root, EstateLayout.ManifestPath));

    /// <summary>Fresh comparison writes no repairs, pins, clearance, or recovery journal.</summary>
    public CompanionComparison CompanionCompare(CompanionCompareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            return RequestDeviceRefusal(model) is not null || model.Findings.HasErrors
                ? CompanionReader.UnavailableComparison() : CompanionReader.Compare(model, request);
        }
    }
}
