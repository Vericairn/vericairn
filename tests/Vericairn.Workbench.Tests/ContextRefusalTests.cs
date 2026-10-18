// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class ContextRefusalTests(HeadlessFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnavailableRequiredOriginalShowsTheRefusalAndEveryFinding(bool openAssignment)
    {
        string root = Seed.Temp("wb-context-refusal");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-context-refusal-prefs"))) { ActingOverride = person };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
        IEstateApi api = shell.Api!;
        byte[] original = Encoding.UTF8.GetBytes("Required evidence, unavailable in this copy.\n");
        WriteOutcome revision = api.NewRecord(new("document-revision", person, Seed.Json($$"""{"path":"docs/required.md","title":"Required evidence","hash":"{{ContentHash.Sha256(original)}}","bytes":{{original.Length}},"mediaType":"text/markdown","revision":1,"summary":"Required original."}""")));
        Assert.True(revision.Ok, revision.Problem?.Message);
        WriteOutcome order = api.NewRecord(new("work-order", person, Seed.Json($$$"""{"key":"COMMS-REFUSED","kind":"ordinary","title":"Read the original","intent":"Required evidence cannot be omitted.","scope":["tests"],"exitQuestion":"Available?","inputs":[{"type":"document-revision","id":"{{{revision.Written!.Id}}}"}],"deliverables":["result"]}""")));
        Assert.True(order.Ok, order.Problem?.Message);
        WriteOutcome assignment = api.Assign(new("COMMS-REFUSED", person, person, Seed.Json("""{"outcome":"Read the evidence.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["original read"],"recovery":"Restore the evidence."}""")));
        Assert.True(assignment.Ok, assignment.Problem?.Message);
        await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
        BriefResponse brief = api.BriefFor("COMMS-REFUSED", person)!;
        Assert.Equal("incomplete-context", brief.Problem!.Code);
        Assert.NotEmpty(brief.Problem.Findings);
        foreach (string depth in CommunicationResolution.Depths)
        {
            await fixture.UiAsync(async () =>
            {
                shell.StatusViewDepth = depth;
                await shell.PendingOperation;
                if (openAssignment) { await shell.OpenAssignmentAsync(assignment.Written!.Id, cancellationToken: TestContext.Current.CancellationToken); }
                else { await shell.OpenWorkOrderAsync("COMMS-REFUSED", cancellationToken: TestContext.Current.CancellationToken); }
            });
            TextDocument page = Assert.IsType<TextDocument>(shell.SelectedDocument);
            Assert.Contains(brief.Markdown, page.Text, StringComparison.Ordinal);
            Assert.Contains(brief.Problem.Code, page.Text, StringComparison.Ordinal);
            foreach (FindingDto finding in brief.Problem.Findings)
            {
                Assert.Contains(finding.Code, page.Text, StringComparison.Ordinal);
                Assert.Contains(finding.Location, page.Text, StringComparison.Ordinal);
                Assert.Contains(finding.Message, page.Text, StringComparison.Ordinal);
            }
        }
    }
}
