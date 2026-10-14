// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Vericairn.Protocol;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// The target bar names the principal Workbench writes as, by its current name, with the device the runtime signs with. In
/// 0.3.1 a principal named with --as showed under its first revision's name beside the first device held on the machine,
/// another principal's, with the selector showing that other principal while the explicit one silently won.
/// </summary>
[Collection("workbench")]
public class ActingTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task APrincipalNamedWithAsShowsItsCurrentNameAndTheDeviceThatSigns()
    {
        string root = Seed.Temp("wb-acting");
        string scratch = Seed.Temp("wb-acting-scratch");
        string op = Seed.Init(root);
        string agent = Seed.Admit(root, op, scratch, "Agent One", "agent", "contributor");
        string agentDevice = Device(root, agent, "Agent device", op);
        Thread.Sleep(5);
        string opDevice = Device(root, op, "Operator device", op);
        Assert.True(string.CompareOrdinal(agentDevice, opDevice) < 0, "The agent's device must come first, as the builder's did on the operator's machine.");
        Seed.Cli("--estate", root, "--json", "records", "new", "principal", "--by", op, "--supersedes", op, "--body", Seed.Write(scratch, "renamed.json", """{"kind":"human","name":"Operator Renamed","roles":["operator"]}"""));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-acting"))) { ActingOverride = op };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));
            ComboBox box = await fixture.Ui(() => window.FindControl<ComboBox>("ActingBox")!);
            Assert.Equal(op, shell.ActingPrincipalId);
            Assert.Equal(opDevice, shell.SelectedActing?.Id);
            Assert.Equal(opDevice, Assert.IsType<DeviceRow>(box.SelectedItem).Id);
            Assert.Equal("Operator Renamed (device Operator device)", shell.ActingPrincipal);

            // The review workspace acts as the same principal, with the operator's independence.
            await fixture.Ui(shell.Refresh);
            Assert.Equal(op, shell.ActingPrincipalId);
            Assert.Equal(opDevice, shell.SelectedActing?.Id);

            // Choosing another device in the selector chooses its principal, and a refresh keeps that choice.
            await fixture.Ui(() => box.SelectedItem = shell.ActingCandidates.Single(d => d.Id == agentDevice));
            Assert.Null(shell.ActingOverride);
            Assert.Equal(agent, shell.ActingPrincipalId);
            Assert.Equal("Agent One (device Agent device)", shell.ActingPrincipal);
            await fixture.Ui(shell.Refresh);
            Assert.Equal(agent, shell.ActingPrincipalId);
            Assert.Equal(agentDevice, shell.SelectedActing?.Id);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task APrincipalWithNoDeviceHeldHereIsShownUnableToWriteWhereSignaturesAreRequired()
    {
        string root = Seed.Temp("wb-acting-none");
        string scratch = Seed.Temp("wb-acting-none-scratch");
        string op = Seed.Init(root);
        string reviewer = Seed.Admit(root, op, scratch, "Reviewer", "agent", "reviewer");
        Device(root, op, "Operator device", op);

        // The estate requires every record to be signed, as the first-party estate does.
        string estate = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "estate", "manifest.json"))).RootElement.GetProperty("estate").GetString()!;
        JsonNode charter = JsonNode.Parse(File.ReadAllText(Directory.EnumerateFiles(Path.Combine(root, "records", "estate"), estate + ".json", SearchOption.AllDirectories).Single()))!["body"]!;
        charter["policies"]!["signatures"] = "required";
        Seed.Cli("--estate", root, "--json", "records", "new", "estate", "--by", op, "--supersedes", estate, "--body", Seed.Write(scratch, "charter.json", charter.ToJsonString()));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-acting-none"))) { ActingOverride = reviewer };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root));
        Assert.Equal(reviewer, shell.ActingPrincipalId);
        Assert.Null(shell.SelectedActing);
        Assert.Equal("Reviewer (named with --as; no admitted device of this principal is held here, so writes will be refused)", shell.ActingPrincipal);
    }

    private static string Device(string root, string principal, string name, string op)
    {
        string device = Seed.Id(Seed.Cli("--estate", root, "--json", "devices", "create", "--principal", principal, "--name", name, "--custody", "file"));
        Seed.Cli("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "A test device.");
        return device;
    }
}
