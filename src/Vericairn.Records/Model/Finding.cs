// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections;

namespace Vericairn.Records.Model;

/// <summary>How serious a finding is.</summary>
public enum Severity
{
    /// <summary>Worth knowing; nothing is wrong.</summary>
    Info,

    /// <summary>Something to resolve; the estate is still usable.</summary>
    Warning,

    /// <summary>The estate or record is invalid.</summary>
    Error,
}

/// <summary>One validation or check result.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Code">Stable machine-readable code such as <c>record.hash-mismatch</c>.</param>
/// <param name="Location">Repository-relative path, optionally with a <c>#fragment</c>.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record Finding(Severity Severity, string Code, string Location, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Severity.ToString().ToUpperInvariant()} {Code} {Location}: {Message}";
}

/// <summary>An ordered collection of findings.</summary>
public sealed class FindingList : IReadOnlyList<Finding>
{
    private readonly List<Finding> findings = [];

    /// <inheritdoc />
    public int Count => findings.Count;

    /// <summary>True when any finding is an error.</summary>
    public bool HasErrors => findings.Exists(f => f.Severity == Severity.Error);

    /// <summary>Number of errors.</summary>
    public int Errors => findings.Count(f => f.Severity == Severity.Error);

    /// <summary>Number of warnings.</summary>
    public int Warnings => findings.Count(f => f.Severity == Severity.Warning);

    /// <inheritdoc />
    public Finding this[int index] => findings[index];

    /// <summary>Adds a finding.</summary>
    public void Add(Finding finding) => findings.Add(finding);

    /// <summary>Adds an error.</summary>
    public void Error(string code, string location, string message) => findings.Add(new Finding(Severity.Error, code, location, message));

    /// <summary>Adds a warning.</summary>
    public void Warning(string code, string location, string message) => findings.Add(new Finding(Severity.Warning, code, location, message));

    /// <summary>Adds an informational finding.</summary>
    public void Info(string code, string location, string message) => findings.Add(new Finding(Severity.Info, code, location, message));

    /// <summary>Findings sorted by location, then code: stable output for comparison across machines.</summary>
    public IReadOnlyList<Finding> Sorted() =>
        findings.OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    public IEnumerator<Finding> GetEnumerator() => findings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
