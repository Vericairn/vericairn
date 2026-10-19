// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

[Trait("Journey", "D19-F")]
public class DocumentCoverageTests
{
    private static readonly InventoryGenerator Generator = Install();
    private static readonly DocumentManagedKind[] Inventory = [new("example.inventory/catalog", "example.inventory/catalog/1"), new("example.inventory/catalog#parts", "example.inventory/parts/1")];
    private static DocumentCoverageDescriptor Descriptor(DocumentManagedKind kind) => new(DocumentCoverage.Version, kind.Id,
        "Find a part and identify its recorded quantity.", new(Generator.Id, 1), new(Generator.Id, 1), kind.InputSchema, "Internal", "on-demand");

    [Fact]
    public void ASeparateDomainRendersItsOwnNestedFieldsThroughTheSharedBoundary()
    {
        byte[] bytes = """{"parts":[{"name":"Valve|A","quantity":7},{"name":"Bolt","quantity":12}]}"""u8.ToArray();
        DocumentSourceSnapshot source = DocumentSourceSnapshot.Content("example.inventory/catalog", "catalog", "example", "catalog.json", ContentHash.Sha256(bytes), bytes);
        var parameters = new Dictionary<string, string> { ["format"] = "1", ["audience"] = "Internal", ["selection"] = "list" }.ToImmutableDictionary(StringComparer.Ordinal);
        DocumentRendition result = DocumentRendering.Render(Generator, new(1, Generator.Descriptor, parameters, "list", null, [source]));
        Assert.Equal(DocumentGenerationStatus.Success, result.Status);
        Assert.Equal("| Part | Quantity |\n|---|---|\n| Bolt | 12 |\n| Valve&#124;A | 7 |\n", Encoding.UTF8.GetString(result.Bytes.AsSpan()));
    }

    [Fact]
    public void AContributionCoversItsDifferentKindAndNestedCollectionWithInstalledContracts()
    {
        DocumentCoverageDescriptor[] descriptors = Inventory.Select(Descriptor).ToArray();
        Assert.Empty(DocumentCoverage.Check(Inventory, descriptors));
        DocumentCoverage.Register(Inventory, descriptors);
        Assert.Throws<InvalidOperationException>(() => DocumentCoverage.Register(Inventory, descriptors));
        Assert.Contains(DocumentCoverage.All, d => d.Kind == "example.inventory/catalog#parts");
    }

    [Fact]
    public void MissingDuplicateUnknownAndUnjustifiedEntriesFailInBothDirections()
    {
        DocumentCoverageDescriptor item = Descriptor(Inventory[0]);
        Assert.Contains(DocumentCoverage.Check(Inventory, [item]), p => p.StartsWith("Missing companion coverage", StringComparison.Ordinal));
        Assert.Contains(DocumentCoverage.Check(Inventory, [item, item]), p => p.StartsWith("Duplicate companion ownership", StringComparison.Ordinal));
        Assert.Contains(DocumentCoverage.Check([Inventory[0]], [item, Descriptor(Inventory[1])]), p => p.StartsWith("Coverage has no managed kind", StringComparison.Ordinal));
        foreach (DocumentCoverageDescriptor invalid in new[]
        {
            item with { Version = 99 }, item with { Item = new(Generator.Id, 2) }, item with { List = null },
            item with { Item = new("example.inventory/missing", 1) }, item with { InputSchema = "wrong" },
            item with { NoReaderReason = "" }, item with { Audience = "Public" },
        }) Assert.NotEmpty(DocumentCoverage.Check([Inventory[0]], [invalid]));
        var privateBytes = new DocumentManagedKind("example.inventory/private-material", "example.inventory/private-material/1", false);
        var noReader = new DocumentCoverageDescriptor(1, privateBytes.Id, "", null, null, privateBytes.InputSchema, "Internal", "", "Private bytes are never exposed; their custody metadata has its own reader.");
        Assert.Empty(DocumentCoverage.Check([privateBytes], [noReader]));
    }

    [Fact]
    public void InventoryTracksEveryAdmittedRecordAndDeclaredCollectionWithoutPretendingTheyAreCovered()
    {
        ImmutableArray<DocumentManagedKind> kinds = DocumentCoverage.RecordKinds;
        foreach (IRecordType type in RecordTypeRegistry.All)
        {
            Assert.Contains(kinds, k => k.Id == "record/" + type.Name);
            foreach (FieldDescription field in type.Fields.Where(f => f.Kind.EndsWith("[]", StringComparison.Ordinal)))
                Assert.Contains(kinds, k => k.Id == "record/" + type.Name + "#" + field.Name);
        }
        Assert.Contains(kinds, k => k.Id == "record/product#editions");
        Assert.Equal(kinds.Length, DocumentCoverage.Check(kinds, []).Length);
    }

    private static InventoryGenerator Install()
    {
        var generator = new InventoryGenerator();
        DocumentGenerators.Register(generator);
        return generator;
    }

    private sealed class InventoryGenerator : IDocumentGenerator
    {
        public string Id => "example.inventory/parts";
        public DocumentGeneratorDescriptor Descriptor { get; } = new("example.inventory/parts", 1, "example.inventory/1", "sha256:" + new string('b', 64),
            new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"], ["selection"] = ["item", "list"] }.ToImmutableDictionary(StringComparer.Ordinal),
            [], ["example.inventory/catalog"], ["item", "list"], "text/markdown", "Ordinal part identity", "Internal", "single");
        public DocumentRenderResult Generate(DocumentGenerationContext context)
        {
            using JsonDocument document = JsonDocument.Parse(context.Sources.Single().Bytes.ToArray());
            IEnumerable<JsonElement> parts = document.RootElement.GetProperty("parts").EnumerateArray().OrderBy(p => p.GetProperty("name").GetString(), StringComparer.Ordinal);
            if (context.Selection == "item") parts = parts.Take(1);
            var text = new StringBuilder("| Part | Quantity |\n|---|---|\n");
            foreach (JsonElement part in parts) text.Append("| ").Append(DocumentMarkdown.Text(part.GetProperty("name").GetString()!))
                .Append(" | ").Append(part.GetProperty("quantity").GetInt32().ToString(CultureInfo.InvariantCulture)).Append(" |\n");
            return DocumentRenderResult.Success(text.ToString());
        }
    }
}
