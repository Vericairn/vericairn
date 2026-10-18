// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class PolicyKindConformanceTests
{
    private const string Kind = "example.policy/work-count";
    private static readonly string Digest = PolicyBasis.Hash("conformance kind implementation");

    [Fact]
    public void ASecondImplementationMustMatchCompleteCanonicalVectorsIncludingErrorsAndOrdering()
    {
        var reference = new PolicyKindRegistry([Registration(First)]);
        var second = new PolicyKindRegistry([Registration(Second)]);
        PolicyBasis valid = Basis(reference);
        var invalidFindings = new FindingList { new(Severity.Error, "fixture.error", "fixture", "Invalid prerequisite.") };
        var invalidModel = new Vericairn.Records.Estate.EstateModel("", valid.Records, valid.Model.ById, valid.Model.SupersededBy, invalidFindings);
        PolicyBasis[] vectors = [valid, Basis(reference, required: 3), new(invalidModel, valid.AsOf, valid.Origin, implementations: reference.Implementations),
            new(valid.Model, "2025-12-31T23:59:59Z", valid.Origin, implementations: reference.Implementations), Basis(reference, unknownParameter: true)];
        Assert.True(PolicyKindRegistry.Conforms(second, reference, vectors));
        Assert.False(PolicyKindRegistry.Conforms(second, reference, []));
        var wrong = new PolicyKindRegistry([Registration(c => First(c).Select(f => f with { Actual = "Different evidence." }).ToArray())]);
        Assert.False(PolicyKindRegistry.Conforms(wrong, reference, vectors));
        PolicyEvaluation result = PolicyEvaluator.Evaluate(valid, second);
        Assert.True(result.Complete);
        Assert.Equal(["a", "z"], result.Findings.Select(f => f.Target.Identity));
        Assert.Equal(2, Assert.Single(result.Rules).Subjects);
    }

    [Fact]
    public void MissingDuplicateAndChangedImplementationsNeverSilentlyPass()
    {
        PolicyKindRegistration registration = Registration(First);
        var registry = new PolicyKindRegistry([registration]);
        PolicyBasis basis = Basis(registry);
        PolicyKindRegistry[] unavailable = [new([]), new([registration, registration]), new([registration with { Version = 2 }]), new([registration with { ImplementationDigest = PolicyBasis.Hash("different") }])];
        foreach (PolicyKindRegistry missing in unavailable) AssertUnavailable(PolicyEvaluator.Evaluate(basis, missing));
        AssertUnavailable(PolicyEvaluator.Evaluate(new(basis.Model, basis.AsOf, basis.Origin), registry));
        AssertUnavailable(PolicyEvaluator.Evaluate(new(basis.Model, basis.AsOf, basis.Origin, implementations: registry.Implementations.Concat(registry.Implementations)), registry));
    }

    [Fact]
    public void DeclaredBoundsAndMissingSourcesFailTheEntireRuleWithoutTruncation()
    {
        PolicyKindRegistration registration = Registration(First);
        var registry = new PolicyKindRegistry([registration]);
        PolicyBasis basis = Basis(registry);
        foreach (PolicyKindRegistration limited in new[]
        {
            registration with { MaximumRecords = 1 }, registration with { MaximumFindings = 1 },
            registration with { SourceContracts = ["example.inventory/1"] },
        }) AssertUnavailable(PolicyEvaluator.Evaluate(basis, new([limited])));
    }

    [Fact]
    public void AKindCannotForgeExceptionsLowerThePolicyLevelOrCollapseDistinctItems()
    {
        var reference = new PolicyKindRegistry([Registration(First)]);
        PolicyBasis basis = Basis(reference);
        Func<PolicyRuleContext, IReadOnlyList<PolicyFinding>>[] wrong =
        [
            c => First(c).Select(f => f with { Disposition = "excepted" }).ToArray(),
            c => First(c).Select(f => f with { Disposition = "ended-unmet" }).ToArray(),
            c => First(c).Select(f => f with { Level = "remind" }).ToArray(),
            c => First(c).Select(f => f with { Rule = "other-rule" }).ToArray(),
            c => First(c).Select(f => f with { Basis = "other-basis" }).ToArray(),
            c => First(c).Concat(First(c)).ToArray(),
            _ => [],
        ];
        foreach (var evaluator in wrong) AssertUnavailable(PolicyEvaluator.Evaluate(basis, new([Registration(evaluator)])));
    }

    [Fact]
    public void CompleteFoundationResultsAndErrorLocationsAreInvariantAcrossCulturesAndInputOrders()
    {
        PolicyKindRegistry registry = FoundationPolicyTests.Kinds;
        PolicyBasis original = FoundationPolicyTests.Basis([
            FoundationPolicyTests.P(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 0 }, "age"),
            FoundationPolicyTests.P(FoundationPolicyKinds.Staleness, new { @class = "open-ask", maxAgeDays = -1 }, "invalid"),
            FoundationPolicyTests.R("work-order", "z", new { title = "Open311", key = "TASK-02" }), FoundationPolicyTests.Order(),
        ]);
        string expected = PolicyEvaluator.Evaluate(original, registry).Canonical();
        string writing = PolicyBasis.Canonical(WritingEvaluation.Evaluate(original));
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
                var shuffled = new PolicyBasis(PolicyBasisTests.Model(original.Records.Reverse()), original.AsOf, original.Origin, implementations: registry.Implementations.Reverse());
                Assert.Equal(expected, PolicyEvaluator.Evaluate(shuffled, registry).Canonical());
                Assert.Equal(writing, PolicyBasis.Canonical(WritingEvaluation.Evaluate(shuffled)));
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    private static void AssertUnavailable(PolicyEvaluation evaluation)
    {
        Assert.False(evaluation.Complete);
        Assert.Equal("unavailable", Assert.Single(evaluation.Findings).Verdict);
        Assert.Equal(1, evaluation.Holds);
    }

    private static PolicyKindRegistration Registration(Func<PolicyRuleContext, IReadOnlyList<PolicyFinding>> evaluator) => new(Kind, 1, Digest, ["estate"], [], 100, 100,
        c => { PolicyInputs.Shape(c.Parameters, ["minimum"]); PolicyInputs.Integer(c.Parameters, "minimum"); }, evaluator);

    private static PolicyBasis Basis(PolicyKindRegistry registry, int required = 2, bool unknownParameter = false)
    {
        object parameters = unknownParameter ? new { minimum = required, unknown = true } : new { minimum = required };
        PolicyBasis source = FoundationPolicyTests.Basis([FoundationPolicyTests.P(Kind, parameters), FoundationPolicyTests.R("work-order", "z", new { }), FoundationPolicyTests.R("work-order", "a", new { })]);
        return new(source.Model, source.AsOf, source.Origin, implementations: registry.Implementations);
    }

    private static IReadOnlyList<PolicyFinding> First(PolicyRuleContext context)
    {
        RecordEnvelope[] orders = context.Model.Current("work-order").ToArray();
        return orders.Select(o => context.Finding(PolicyWork.Target(context.Model, o), "count", orders.Length >= PolicyInputs.Integer(context.Parameters, "minimum") ? "satisfied" : "unmet",
            PolicyInputs.Number(orders.Length), PolicyInputs.Number(PolicyInputs.Integer(context.Parameters, "minimum")), "Inspect the selected inventory.", o)).ToArray();
    }

    private static IReadOnlyList<PolicyFinding> Second(PolicyRuleContext context)
    {
        List<RecordEnvelope> orders = [];
        foreach (RecordEnvelope record in context.Basis.Records)
            if (record.Type == "work-order" && context.Model.IsCurrent(record)) orders.Add(record);
        long minimum = context.Parameters.GetProperty("minimum").GetInt64();
        List<PolicyFinding> findings = [];
        for (int i = orders.Count - 1; i >= 0; i--)
            findings.Add(context.Finding(PolicyWork.Target(context.Model, orders[i]), "count", orders.Count < minimum ? "unmet" : "satisfied",
                PolicyInputs.Number(orders.Count), PolicyInputs.Number(minimum), "Inspect the selected inventory.", orders[i]));
        return findings;
    }
}
