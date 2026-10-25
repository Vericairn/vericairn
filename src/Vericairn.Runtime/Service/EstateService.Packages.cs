// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private sealed record PackageSelection(RecordEnvelope Product, RecordEnvelope? Release, string Version, IPackageRegistryProvider Provider, string[] Packages)
    {
        internal RecordEnvelope Subject => Release ?? Product;
    }

    /// <inheritdoc />
    public PackagesResponse PackagesUnlist(PackagesUnlistRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PackageIntent(request.Product, request.Version, request.Package, request.By, null, request.Reserved);
    }

    /// <inheritdoc />
    public PackagesResponse PackagesDeprecate(PackagesDeprecateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Reasons is not { Count: > 0 and <= 3 } || request.Reasons.Any(r => !PackageRecordRules.Reasons.Contains(r, StringComparer.Ordinal))
            || request.Reasons.Distinct(StringComparer.Ordinal).Count() != request.Reasons.Count
            || request.Message?.Length > 500 || request.AlternatePackage is not null && !PackageIdentity.ValidId(request.AlternatePackage)
            || request.AlternateRange is not null && (string.IsNullOrWhiteSpace(request.AlternateRange) || request.AlternateRange.Length > 256 || request.AlternatePackage is null))
            return PackageProblem("Specify distinct NuGet reasons, a message of at most 500 characters and an optional alternate package with its range.");
        return PackageIntent(request.Product, request.Version, request.Package, request.By,
            new(request.Reasons.Order(StringComparer.Ordinal).ToArray(), string.IsNullOrEmpty(request.Message) ? null : request.Message,
                request.AlternatePackage?.ToLowerInvariant(), request.AlternateRange), request.Reserved);
    }

    private PackagesResponse PackageIntent(string product, string version, string package, string by, PackageDeprecation? deprecation, bool reserved)
    {
        try
        {
            lock (gate)
            {
                RequirePackageActor(by, accountAction: true);
                PackageSelection selection = SelectPackages(product, version, reserved);
                if (!selection.Packages.Contains(package, StringComparer.OrdinalIgnoreCase))
                    return PackageProblem("The product does not declare this package.");
                string action = deprecation is null ? "package-unlist" : "package-deprecate";
                JsonObject target = PackagePins(selection, package.ToLowerInvariant());
                if (deprecation is not null) AddDeprecation(target, deprecation, intent: true);
                string identity = "nuget-org:" + action + ":" + ContentHash.Sha256(Encoding.UTF8.GetBytes(package.ToLowerInvariant() + ":" + version.ToLowerInvariant()));
                EstateModel model = session.Load();
                RecordEnvelope? prior = model.Current("operation").SingleOrDefault(o => EstateModel.BodyString(o, "identity") == identity);
                if (prior is not null)
                {
                    RecordEnvelope original = model.ById[model.Chain(prior)[0]];
                    // Reruns keep the recorded target even when declarations acquire a newer revision.
                    JsonElement pinned = original.Body.GetProperty("target");
                    if (model.Chain(selection.Product)[0] != model.Chain(model.ById[PackageRecordRules.Text(pinned, "product")!])[0]
                        || PackageRecordRules.Has(pinned, "reserved") != reserved
                        || PackageRecordRules.DeprecationFields.Any(k => PackageRecordRules.Text(pinned, k) != target[k]?.GetValue<string>()))
                        return PackageProblem("This package action already has a different intent; observe its original operation without replacing its identity.")
                            with { Intent = Written(original), Operation = Written(prior) };
                    return PackageIntentAnswer(original, prior);
                }
                JsonObject body = new()
                {
                    ["provider"] = selection.Provider.Provider, ["action"] = action, ["identity"] = identity,
                    ["intent"] = deprecation is null ? "The account holder intends to unlist this package version on nuget.org."
                        : "The account holder intends to deprecate this package version on nuget.org with the recorded metadata.",
                    ["target"] = target, ["status"] = "not-started", ["evidence"] = new JsonArray(),
                };
                WriteOutcome write = NewRecord(new("operation", by, Element(body),
                    $"{selection.Subject.Type}:{selection.Subject.Id}:{selection.Subject.ComputedHash}"));
                if (!write.Ok) return PackageProblem("The package intent was refused; nothing was recorded.", write.Problem);
                RecordEnvelope intent = session.Load().ById[write.Written!.Id];
                return PackageIntentAnswer(intent, intent);
            }
        }
        catch (ProviderRefusedException exception) { return PackageProblem(exception.Message); }
    }

    /// <inheritdoc />
    public PackagesResponse PackagesObserve(PackagesObserveRequest request) => PackagesObserveAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task<PackagesResponse> PackagesObserveAsync(PackagesObserveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecordEnvelope? intent = null;
        RecordEnvelope? operation = null;
        try
        {
            PackageSelection selection;
            lock (gate)
            {
                RequirePackageActor(request.By, accountAction: request.Operation is not null);
                if (request.Operation is not null)
                {
                    if (request.Product is not null || request.Version is not null || request.Reserved) return PackageProblem("Observe an operation or a product version, not both; reserved selection belongs to a product version.");
                    EstateModel model = session.Load();
                    if (!model.ById.TryGetValue(request.Operation, out RecordEnvelope? named) || named.Type != "operation" || !PackageRecordRules.IsAction(named))
                        return PackageProblem("Name a recorded package unlisting or deprecation operation.");
                    operation = model.Latest(named);
                    intent = model.ById[model.Chain(operation)[0]];
                    JsonElement target = intent.Body.GetProperty("target");
                    RecordEnvelope product = model.ById[PackageRecordRules.Text(target, "product")!];
                    RecordEnvelope? release = PackageRecordRules.Has(target, "reserved") ? null : model.ById[PackageRecordRules.Text(target, "release")!];
                    IPackageRegistryProvider provider = Providers.PackageRegistry(PackageRecordRules.Text(target, "provider")!)
                        ?? throw new ProviderRefusedException("The original package registry provider is unavailable.");
                    selection = new(product, release, PackageRecordRules.Text(target, "version")!, provider, [PackageRecordRules.Text(target, "package")!]);
                }
                else selection = SelectPackages(request.Product, request.Version, request.Reserved);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var states = new List<PackageRegistryState>();
            string version = selection.Version;
            foreach (string package in selection.Packages)
            {
                PackageRegistryState state = await selection.Provider.ObserveAsync(package.ToLowerInvariant(), version, deadline.Token).ConfigureAwait(false);
                if (!string.Equals(state.Package, package, StringComparison.OrdinalIgnoreCase) || state.Version != version)
                    throw new ProviderRefusedException("The registry provider returned another package or version.");
                states.Add(state);
            }
            deadline.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                RequirePackageActor(request.By, accountAction: intent is not null);
                EstateModel model = session.Load();
                if (operation is not null && model.Latest(operation).Id != operation.Id)
                    return PackageProblem("The operation changed during observation; observe its original identity again.") with { Intent = Written(intent!), Operation = Written(model.Latest(operation)) };
                if (intent is null && (!model.IsCurrent(selection.Product) || selection.Release is not null && !model.IsCurrent(selection.Release)))
                    return PackageProblem("The product or release changed during observation; repeat with its current declaration.");
                var authored = new List<AuthoredRecord>();
                var results = new List<PackageObservationResult>();
                bool matched = true;
                foreach (PackageRegistryState state in states)
                {
                    JsonObject facts = PackagePins(selection, state.Package);
                    facts["publication"] = state.Publication; facts["listing"] = state.Listing; facts["deprecation"] = state.Deprecation;
                    if (state.PackageContent is not null) facts["packageContent"] = state.PackageContent;
                    if (state.Details is not null) AddDeprecation(facts, state.Details, intent: false);
                    JsonObject body = new()
                    {
                        ["kind"] = "package", ["asOf"] = state.AsOf,
                        ["summary"] = $"nuget.org reports {state.Package} {state.Version}: {state.Publication}, {state.Listing}, {state.Deprecation}.",
                        ["facts"] = facts, ["evidence"] = JsonSerializer.SerializeToNode(state.Evidence, ProtocolJson.Options),
                        ["limitations"] = new JsonArray("Public API state at observation time; registry indexes can change or lag.",
                            "Package bytes, acceptance and the identity of the account holder were not verified."),
                    };
                    (AuthoredRecord? record, string? refusal) = AuthorLocked(new("observation", request.By, Element(body), (intent ?? selection.Subject).Reference), model);
                    if (record is null) return PackageProblem("The observation could not be authored: " + refusal);
                    authored.Add(record);
                    results.Add(new(state, Written(record.Envelope)));
                    if (intent is not null) matched = PackageRecordRules.Matches(EstateModel.BodyString(intent, "action")!, intent.Body.GetProperty("target"), Element(facts));
                }
                RecordWritten? outcome = operation is null ? null : Written(operation);
                if (operation is not null && EstateModel.BodyString(operation, "status") != "observed-success")
                {
                    JsonObject body = JsonNode.Parse(operation.Body.GetRawText())!.AsObject();
                    body["status"] = matched ? "observed-success" : "uncertain";
                    RecordWritten observation = results[0].Observation;
                    body["observed"] = new JsonObject { ["at"] = states[0].AsOf, ["reference"] = observation.Id,
                        ["summary"] = matched ? "The intended state was observed on nuget.org." : "The intended state was not observed on nuget.org; the account holder's act is unconfirmed." };
                    body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "The registry state observed against the original intent.", ["location"] = observation.Path, ["hash"] = observation.Hash });
                    (AuthoredRecord? record, string? refusal) = AuthorLocked(new("operation", request.By, Element(body), operation.Subject, operation.Id), model);
                    if (record is null) return PackageProblem("The outcome could not be authored: " + refusal);
                    authored.Add(record);
                    outcome = Written(record.Envelope);
                }
                (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherLocked(authored, "Record public package registry observations and the account-holder outcome.", null);
                PackageObservationResult[] stored = results.Where(r => written.Any(w => w.Id == r.Observation.Id)).ToArray();
                if (problem is not null) return PackageProblem("The observation batch was not stored completely; inspect its recorded members before resuming.", problem)
                    with { Packages = stored, Intent = intent is null ? null : Written(intent), Operation = operation is null ? null : Written(session.Load().Latest(operation)) };
                return new(matched, matched ? "observed" : "unconfirmed", matched ? "The registry observations are recorded."
                    : operation is not null && EstateModel.BodyString(operation, "status") == "observed-success"
                        ? "The latest registry state no longer matches the intent; the earlier observed success remains recorded."
                        : "The intended state is not currently observed; keep the original intent and observe again after the account holder's act.", stored,
                    intent is null ? null : Written(intent), outcome);
            }
        }
        catch (ProviderRefusedException exception) { return PackageProblem(exception.Message) with { Intent = intent is null ? null : Written(intent), Operation = operation is null ? null : Written(operation) }; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return PackageProblem("The observation deadline expired; no new registry state was recorded.") with { Intent = intent is null ? null : Written(intent), Operation = operation is null ? null : Written(operation) }; }
    }

    private void RequirePackageActor(string by, bool accountAction)
    {
        EstateModel model = session.Load();
        if (session.Manifest.RecordFormat < 3 || model.Findings.HasErrors) throw new ProviderRefusedException("Package observations require a valid estate at record format 3.");
        if (model.CurrentPrincipal(by) is null || accountAction && !model.HasRole(by, Roles.Operator))
            throw new ProviderRefusedException(accountAction ? "The estate's operator records account-holder intents and their outcomes." : "Name an observing principal of this estate.");
        if (RequestDeviceRefusal(model) is not null) throw new ProviderRefusedException("The requesting device cannot record package observations.");
    }

    private PackageSelection SelectPackages(string? product, string? version, bool reserved)
    {
        EstateModel model = session.Load();
        if (product is null || !model.ById.TryGetValue(product, out RecordEnvelope? declaration) || declaration.Type != "product" || !model.IsCurrent(declaration)
            || !declaration.Body.TryGetProperty("packages", out JsonElement packages) || !PackageIdentity.ValidVersion(version))
            throw new ProviderRefusedException(reserved ? "Name a current product declaring packages and its reserved version." : "Name a current product declaring packages and its recorded release version.");
        RecordEnvelope? release = reserved ? null : model.Current("release").SingleOrDefault(r => r.Subject is not null && model.Chain(declaration).Contains(r.Subject.Id, StringComparer.Ordinal)
            && string.Equals(EstateModel.BodyString(r, "version"), version, StringComparison.OrdinalIgnoreCase));
        if (reserved && !PackageRecordRules.DeclaresReserved(declaration, version))
            throw new ProviderRefusedException("The current product declaration does not reserve this version.");
        if (!reserved && release is null) throw new ProviderRefusedException("The product has no recorded release of this version.");
        IPackageRegistryProvider provider = Providers.PackageRegistry(packages.GetProperty("provider").GetString()!)
            ?? throw new ProviderRefusedException("The declared package registry provider is unavailable.");
        return new(declaration, release, version!.ToLowerInvariant(), provider, packages.GetProperty("ids").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    private static JsonObject PackagePins(PackageSelection selection, string package)
    {
        JsonObject pins = new()
        {
            ["provider"] = selection.Provider.Provider, ["product"] = selection.Product.Id, ["productHash"] = selection.Product.ComputedHash,
            ["package"] = package, ["version"] = selection.Version,
        };
        if (selection.Release is { } release)
        {
            pins["release"] = release.Id; pins["releaseHash"] = release.ComputedHash;
        }
        else pins["reserved"] = selection.Version;
        return pins;
    }

    private static void AddDeprecation(JsonObject facts, PackageDeprecation details, bool intent)
    {
        facts["reasons"] = intent ? JsonValue.Create(string.Join(',', details.Reasons)) : JsonSerializer.SerializeToNode(details.Reasons);
        if (details.Message is not null) facts["deprecationMessage"] = details.Message;
        if (details.AlternatePackage is not null) facts["alternatePackage"] = details.AlternatePackage;
        if (details.AlternateRange is not null) facts["alternateRange"] = details.AlternateRange;
    }

    private static PackagesResponse PackageIntentAnswer(RecordEnvelope original, RecordEnvelope current)
    {
        JsonElement target = original.Body.GetProperty("target");
        string package = PackageRecordRules.Text(target, "package")!;
        string version = PackageRecordRules.Text(target, "version")!;
        string observe = $"vericairn packages observe --operation {original.Id} --by <operator-id>";
        if (EstateModel.BodyString(current, "status") == "observed-success")
            return new(true, "observed-success", "The original intent already has an observed success. A fresh observation can check its current state.", [], Written(original), Written(current), observe);
        string step = EstateModel.BodyString(original, "action") == "package-unlist"
            ? $"Sign in as the package owner at https://www.nuget.org/packages/{package}/{version}/Manage. Clear 'List in search results' for version {version} and save."
            : $"Sign in as the package owner at https://www.nuget.org/packages/{package}/{version}/Deprecate. Select only version {version}; set reasons {PackageRecordRules.Text(target, "reasons")}, message {JsonSerializer.Serialize(PackageRecordRules.Text(target, "deprecationMessage") ?? "")}, alternate package {PackageRecordRules.Text(target, "alternatePackage") ?? "none"} and range {PackageRecordRules.Text(target, "alternateRange") ?? "none"}; save the deprecation.";
        step += $" Then run {observe}.";
        return new(true, EstateModel.BodyString(current, "status")!, "The original account-holder intent is recorded; Vericairn has made no registry change.", [], Written(original), Written(current), step);
    }

    private static RecordWritten Written(RecordEnvelope record) => new(record.ExpectedPath, record.Id, record.ComputedHash);
    private static PackagesResponse PackageProblem(string message, Problem? problem = null) => new(false, "refused", message, [], Problem: problem ?? new("refused", message, []));
}
