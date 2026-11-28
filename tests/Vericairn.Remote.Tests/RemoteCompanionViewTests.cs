// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

public sealed class RemoteCompanionViewTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task CompanionItemRowsShowAndAnnounceTheirLabels()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, WithoutPresenceKey = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            try
            {
                TabControl home = view.FindControl<TabControl>("Home")!;
                home.SelectedItem = Assert.Single(home.Items.OfType<TabItem>(), tab => Equals(tab.Header, "Products"));
                await model.OpenCompanionsCommand.ExecuteAsync();
                model.SelectedCompanionCollection = Assert.Single(model.CompanionCollections, collection => collection.Name == "Estate records");
                await model.OpenCompanionCollectionCommand.ExecuteAsync();
                window.UpdateLayout();

                ListBox list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>(),
                    candidate => AutomationProperties.GetName(candidate) == "Items on this page");
                TextBlock[] rows = list.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && text.DataContext is RemoteCompanionItem).ToArray();
                Assert.NotEmpty(rows);
                Assert.All(rows, row =>
                {
                    var item = Assert.IsType<RemoteCompanionItem>(row.DataContext);
                    Assert.NotEqual(item.Name, item.Label);
                    Assert.Equal(item.Label, row.Text);
                    Assert.Equal(item.Label, AutomationProperties.GetName(row));
                });
            }
            finally
            {
                window.Close();
            }

            return true;
        });
    }
}
