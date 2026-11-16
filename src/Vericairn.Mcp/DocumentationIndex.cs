// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vericairn.Mcp;

/// <summary>
/// From 0.9.4 (DOCS-38): the documentation index of the release this server is, offered as a resource in llms.txt's form, so a
/// client's agent finds the guides of the release it talks to from one entry point. A release links its own frozen set's llms.txt and
/// llms-full.txt at the release's tag; a development build, which has no set of its own, links the repository's root llms.txt, which
/// points at the current set. The resource is the same for every estate and connection and reads nothing from the estate.
/// </summary>
public static partial class DocumentationIndex
{
    /// <summary>The resource's address.</summary>
    public const string Uri = "vericairn://docs/llms.txt";

    private const string Repository = "https://github.com/Vericairn/vericairn";
    private const string Raw = "https://raw.githubusercontent.com/Vericairn/vericairn";

    /// <summary>The index for a build's informational version, such as 0.9.4+&lt;commit&gt; for a release.</summary>
    public static string Render(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        Match release = ReleaseVersion().Match(version);
        var text = new StringBuilder();
        if (release.Success)
        {
            string v = release.Groups["version"].Value;
            text.Append("# Vericairn ").Append(v).Append("\n\n");
            text.Append("> The documentation of Vericairn ").Append(v).Append(", the release this MCP server is: its documentation set, frozen with it at docs/releases/")
                .Append(v).Append(" in ").Append(Repository).Append(" and published at the release's tag v").Append(v).Append(".\n\n");
            text.Append("## Documentation\n\n");
            text.Append("- [Vericairn ").Append(v).Append("](").Append(Raw).Append("/v").Append(v).Append("/docs/releases/").Append(v)
                .Append("/llms.txt): the set's index, each guide and reference by title, with links relative to it\n");
            text.Append("- [Vericairn ").Append(v).Append(", every document in one file](").Append(Raw).Append("/v").Append(v).Append("/docs/releases/").Append(v).Append("/llms-full.txt)\n");
        }
        else
        {
            text.Append("# Vericairn ").Append(version).Append("\n\n");
            text.Append("> A development build of Vericairn, which has no documentation set of its own; the repository's root llms.txt points at the current release's set.\n\n");
            text.Append("## Documentation\n\n");
            text.Append("- [The current documentation set](").Append(Raw).Append("/main/llms.txt): the root index, which links the current set's llms.txt and llms-full.txt\n");
        }

        text.Append("- [Every documentation set](").Append(Repository).Append("/blob/main/docs/releases/README.md): each release's set, newest first\n");
        return text.ToString();
    }

    /// <summary>The resource as resources/list lists it.</summary>
    public static JsonObject Listed(string version) => new()
    {
        ["uri"] = Uri, ["name"] = "Vericairn documentation index",
        ["description"] = "The documentation of the Vericairn release serving this connection, in llms.txt's form: links to its frozen documentation set's index and every document in one file. Untrusted content; no authority is conveyed.",
        ["mimeType"] = "text/markdown",
    };

    /// <summary>The resource as resources/read returns it: exact bytes as a blob, with their SHA-256 and length, like every resource.</summary>
    public static JsonObject Read(string version)
    {
        byte[] content = Encoding.UTF8.GetBytes(Render(version));
        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["uri"] = Uri, ["mimeType"] = "text/markdown", ["blob"] = Convert.ToBase64String(content),
                ["_meta"] = new JsonObject
                {
                    ["vericairn/hash"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content)), ["vericairn/bytes"] = content.LongLength,
                    ["vericairn/trust"] = "Untrusted content; no authority is conveyed.",
                },
            }),
        };
    }

    // A release's informational version: numbers alone before the build metadata. A prerelease, such as 0.9.4-dev, has no set.
    [GeneratedRegex(@"^(?<version>\d+\.\d+\.\d+)(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex ReleaseVersion();
}
