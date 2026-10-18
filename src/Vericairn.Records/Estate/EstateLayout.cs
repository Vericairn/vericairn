// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;

namespace Vericairn.Records.Estate;

/// <summary>Where things live in an estate repository.</summary>
public static class EstateLayout
{
    /// <summary>Records tree.</summary>
    public const string RecordsDirectory = "records";

    /// <summary>Design documents.</summary>
    public const string DesignDirectory = "design";

    /// <summary>Other documents.</summary>
    public const string DocsDirectory = "docs";

    /// <summary>Small content-addressed objects.</summary>
    public const string ObjectsDirectory = "objects";

    /// <summary>The manifest that pins the Vericairn release.</summary>
    public const string ManifestPath = "estate/manifest.json";

    /// <summary>Largest file allowed outside objects/ (bytes).</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>Largest object allowed in objects/ (bytes).</summary>
    public const long MaxObjectBytes = 8 * 1024 * 1024;

    /// <summary>Entries allowed at the repository root, git's metadata (.gitignore, .gitattributes, .mailmap) among them.</summary>
    public static readonly IReadOnlySet<string> AllowedTopLevel = new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal)
    {
        ".git", ".github", ".gitignore", ".gitattributes", ".mailmap", ".vericairn", "README.md", "LICENSE", "bootstrap", "bootstrap.ps1",
        "estate", RecordsDirectory, DesignDirectory, DocsDirectory, ObjectsDirectory,
    });

    /// <summary>Entries never scanned or validated.</summary>
    public static readonly IReadOnlySet<string> IgnoredTopLevel = new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal) { ".git", ".vericairn" });

    /// <summary>Repository-relative path with forward slashes.</summary>
    public static string Relative(string root, string fullPath)
    {
        string relative = Path.GetRelativePath(root, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
