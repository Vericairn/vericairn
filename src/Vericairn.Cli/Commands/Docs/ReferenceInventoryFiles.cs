// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Policies;

namespace Vericairn.Cli;

// Build/document adapter. Policy kinds consume the retained snapshot, never this I/O helper.
internal static class ReferenceInventoryFiles
{
    internal const string Name = ReferenceInventory.FileName;

    internal static void Generate(string directory, string? commit, IEnumerable<string> commands, IEnumerable<string> operations, IEnumerable<string> types)
    {
        ReferenceItem[] items = Items(commands, operations, types);
        var references = ReferenceInventory.ReferencePaths.Values.ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(directory, p)), StringComparer.Ordinal);
        string inventory;
        try { inventory = ReferenceInventory.Generate(commit ?? "", items, references); }
        catch (PolicyInputException error) { throw new InvalidOperationException(error.Message, error); }
        File.WriteAllText(Path.Combine(directory, ReferenceInventory.FileName), inventory, new UTF8Encoding(false));
    }

    internal static IReadOnlyList<FindingDto> Check(string directory, string? commit, IEnumerable<string> commands, IEnumerable<string> operations, IEnumerable<string> types)
    {
        try
        {
            PolicyFile[] files = ReferenceInventory.ReferencePaths.Values.Append(ReferenceInventory.FileName).Select(p =>
            {
                string path = Path.Combine(directory, p);
                byte[] bytes = File.ReadAllBytes(path);
                return new PolicyFile(p, ContentHash.Sha256(bytes), [.. bytes], IsLink: (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);
            }).ToArray();
            RetainedReferenceInventory inventory = ReferenceInventory.Read(new("documentation-set", commit ?? "", PolicyBasis.RepositoryContract, true, [.. files]), ReferenceInventory.FileName, commit ?? "");
            ReferenceItem[] expected = Items(commands, operations, types);
            if (expected.Distinct().Count() != expected.Length || !inventory.Items.ToHashSet().SetEquals(expected)) throw new PolicyInputException("The inventory differs from the declared command, operation or record-type set.");
            return [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PolicyInputException)
        {
            // Keep diagnostics portable, including when the underlying filesystem reports an absolute path.
            string reason = error is PolicyInputException ? error.Message : "A retained inventory or reference file cannot be read.";
            return [new("error", "docs.reference-inventory", ReferenceInventory.FileName, reason)];
        }
    }

    private static ReferenceItem[] Items(IEnumerable<string> commands, IEnumerable<string> operations, IEnumerable<string> types) =>
        [.. commands.Select(c => new ReferenceItem("command", c)), .. operations.Select(o => new ReferenceItem("api-operation", o)), .. types.Select(t => new ReferenceItem("record-type", t))];
}
