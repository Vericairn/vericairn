// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class RevisionContextHistoryTests
{
    [Fact]
    public void ContextReturnsExactlyTheHashedAuthoredBytesAndCanRetrieveThemFromLocalHistory()
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\nOriginal authored café.\n");
        using EstateService service = EstateService.Open(estate.Root);
        service.Providers = new ProviderRegistry();
        service.Providers.Register(new GitCommandAdapter());
        var pin = RevisionPinFixture.Pin(service, estate.OperatorId, "First history.", convert: true);
        byte[] authored = RevisionLines.AuthoredContent(RevisionPinFixture.Bytes(estate));
        JsonObject order = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        order["inputs"] = new JsonArray(new JsonObject { ["type"] = "document-revision", ["id"] = pin.Id });
        estate.Add("work-order", estate.OperatorId, order.ToJsonString());
        ContextResponse context = service.Context("H-1");
        Assert.Null(context.Problem);
        JsonElement original = context.Package!.Payload.GetProperty("originals").EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "document");
        Assert.Equal(RevisionLines.HashMode, original.GetProperty("hashMode").GetString());
        Assert.Equal(authored, Convert.FromBase64String(original.GetProperty("content").GetString()!));
        string identity = original.GetProperty("pin").GetString()!;
        Assert.Equal(authored, service.ContextRead(identity).Content);
        ResourceEntry resource = service.Resources().Resources.Single(r => r.Request.Kind == "document");
        Assert.Equal(authored, service.ContextRead(resource.Request.Key!).Content);
        Assert.Equal(ContentHash.Sha256(authored), original.GetProperty("byteHash").GetString());
        RevisionPinMergeJourneyTests.Git(estate.Root, "init", "--initial-branch=main");
        RevisionPinMergeJourneyTests.Git(estate.Root, "add", "estate", "records", "design");
        RevisionPinMergeJourneyTests.Git(estate.Root, "commit", "-m", "Retain the generated document revision");
        estate.WriteFile(RevisionPinFixture.Document, "# A different working document\n");
        ISourceAdapter history = new GitCommandAdapter();
        string hash = EstateModel.BodyString(pin, "hash")!;
        Assert.Null(history.ReadFileAtHash(estate.Root, RevisionPinFixture.Document, hash));
        Assert.Equal(authored, history.ReadFileAtHash(estate.Root, RevisionPinFixture.Document, hash, RevisionLines.HashMode));
        Assert.Equal(authored, service.ContextRead(identity).Content);
        Assert.Equal(authored, ContextSelection.LocalOriginal(estate.Root, RevisionPinFixture.Document, hash, history, RevisionLines.HashMode));
        Assert.Null(ContextSelection.LocalOriginal(estate.Root, RevisionPinFixture.Document, hash, history, "unknown-mode"));
        Assert.Equal("# A different working document\n", Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)));
    }
}
