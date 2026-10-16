// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class CommunicationJourneyTests
{
    [Fact]
    public void APersonSetsPreviewsAndResetsTheirProfileWithoutChangingMachineStatus()
    {
        using var estate = new HandoffEstate();
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "standard", "--progress", "none");
        string shown = CliHarness.Ok("--estate", estate.Root, "--recipient", estate.OperatorId, "communication", "show", "--preview").Out;
        Assert.Contains("status: standard", shown, StringComparison.Ordinal);
        Assert.Contains("progress: none", shown, StringComparison.Ordinal);
        Assert.Contains("Complete example", shown, StringComparison.Ordinal);
        string shortJson = CliHarness.Run("--estate", estate.Root, "--recipient", estate.OperatorId, "--depth", "short", "--json", "status").Out;
        string fullJson = CliHarness.Run("--estate", estate.Root, "--recipient", estate.OperatorId, "--depth", "comprehensive", "--json", "status").Out;
        Assert.Equal(shortJson, fullJson);
        string shortText = CliHarness.Run("--estate", estate.Root, "--recipient", estate.OperatorId, "--depth", "short", "status").Out;
        string fullText = CliHarness.Run("--estate", estate.Root, "--recipient", estate.OperatorId, "--depth", "comprehensive", "status").Out;
        Assert.NotEqual(shortText, fullText);
        CliHarness.Ok("--estate", estate.Root, "communication", "reset", "--as", estate.OperatorId);
        using EstateService service = EstateService.Open(estate.Root);
        Assert.Equal("shipped", service.Communication(estate.OperatorId).Fields["status"].Source);
        Assert.Equal(2, estate.Load().OfType("communication-setting").Count());
    }

    [Fact]
    public void UpdatingOneFieldKeepsTheOthersAndInheritResetsOnlyTheNamedField()
    {
        using var estate = new HandoffEstate();
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "standard", "--progress", "none");
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "inherit");
        using EstateService service = EstateService.Open(estate.Root);
        Assert.Equal("short", service.Communication(estate.OperatorId).Fields["status"].Value);
        Assert.Equal("none", service.Communication(estate.OperatorId).Fields["progress"].Value);
    }
}
