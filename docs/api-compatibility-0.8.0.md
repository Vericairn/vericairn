# API compatibility for 0.8.0: ownership of shared values

CODE-04 reviews the exported surface at `fcd14ab` before implementation. This is a
NuGet source and binary compatibility change for the fields and removed facilities
listed below. It changes ownership of in-process collections, not the record
format, wire contracts, vocabulary values, enumeration order, CLI text or generated
references. Record data types remain records, including their constructors,
`with` expressions and deconstruction.

## Shared array fields

Every row changes the **field type** from `string[]` to
`System.Collections.Immutable.ImmutableArray<string>`. All remain `public static
readonly` fields with the same name. The initializer's strings, spelling and order
are retained, including composed vocabularies. Callers can enumerate, index, use
`Length`, or call `IndexOf`; mutation cannot change the shared collection. Use
`ToArray()` when a private mutable array or an array-only API is needed. Recompile
consumers: replacing an array with a value type is not binary compatible. Array
assignments, element writes and calls to `Array.IndexOf` require source changes.
`BodyReader.Enum(string, bool, params string[])` retains its public signature;
callers passing one of these fields can supply `ToArray()`.

| Exported member | Old shape | New shape |
| --- | --- | --- |
| `Vericairn.Mcp.McpServer.SupportedProtocolVersions` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.AgentRuns.ChargedUnits` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.AgentRuns.Settled` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.Asks.Waiting` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.EstateManifest.Profiles` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.EstateStatus.ReconcileCodes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.GoalPredicates.Verdicts` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.GoalRules.EvaluationVerdicts` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.GoalRules.Ended` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.LivingDocuments.FoundationGenerators` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.NextEligible.RouteStates` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Estate.NextEligible.Dispatching` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Signing.DeviceKeyStores.Kinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Signing.DeviceKeyStores.Format2Kinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Signing.DeviceKeyStores.PresenceKinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.AcceptanceType.SubjectTypes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.AskType.States` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.AssignmentType.RoutedWith` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.Caps.UnitNames` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.CredentialReferenceType.Kinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.CredentialReferenceType.Statuses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DecisionType.Stakes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DecisionType.Resolutions` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DeviceType.Profiles` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DeviceType.Platforms` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DeviceType.Presences` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DeviceType.Purposes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DocumentReviewType.Outcomes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DocumentReviewType.Independences` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.DocumentReviewType.CheckOutcomes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ExecutionPolicy.Choices` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.GoalStatusType.Statuses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.GoalType.Kinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.GoalType.PlanPolicies` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.GoalType.Approvals` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.HarnessQualificationType.ProbeNames` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.LivingDocumentType.Kinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.LivingDocumentType.Levels` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.LivingDocumentType.Triggers` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.LivingDocumentType.Checks` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.LivingDocumentType.Clearers` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ObservationType.Kinds` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.OperationType.Statuses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.PossibilityType.Dispositions` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ProductType.ReleaseRoles` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ProductType.ReleasePlatforms` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.QualificationType.Outcomes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.RecordTypeRegistry.Reserved` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ReleaseType.Statuses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ReleaseType.Platforms` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ResultType.Outcomes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.ResultType.CheckOutcomes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.Roles.All` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.Scopes.Known` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.RosterType.KnownRoles` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.VerificationType.Outcomes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.VerificationType.Roles` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.VerificationType.EvidenceClasses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.VerificationType.Observers` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Records.Types.WorkStatusType.Statuses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Runtime.Providers.ProviderDescriptor.Statuses` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Runtime.Release.ReleaseFlow.Platforms` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Runtime.Workers.RunSelection.Scopes` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Runtime.Workers.WorkerEnvironment.Allowed` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |
| `Vericairn.Runtime.Workers.WorkerEnvironment.McpServer` | `public static readonly string[]` | `public static readonly ImmutableArray<string>` |

These are all 65 public static array fields in the baseline source.

## Shared values whose declared types stay the same

These members keep their source and binary signatures. Casting an interface to
its former mutable implementation no longer gives write access. The dictionaries
and sets wrap privately owned storage, retaining enumeration order and ordinal
key semantics. Nested state lists are also read-only.

| Exported member | Old shape and ownership | New shape and ownership |
| --- | --- | --- |
| `Vericairn.Records.Model.RecordParser.RequiredKeys` | `public static readonly IReadOnlySet<string>` backed by `HashSet<string>` | Same field; read-only set over a private `HashSet<string>` |
| `Vericairn.Records.Model.RecordParser.OptionalKeys` | `public static readonly IReadOnlySet<string>` backed by `HashSet<string>` | Same field; read-only set over a private `HashSet<string>` |
| `Vericairn.Records.Model.RecordParser.HashExcludedKeys` | `public static readonly IReadOnlySet<string>` backed by `HashSet<string>` | Same field; read-only set over a private `HashSet<string>` |
| `Vericairn.Records.Estate.EstateLayout.AllowedTopLevel` | `public static readonly IReadOnlySet<string>` backed by `HashSet<string>` | Same field; read-only set over a private `HashSet<string>` |
| `Vericairn.Records.Estate.EstateLayout.IgnoredTopLevel` | `public static readonly IReadOnlySet<string>` backed by `HashSet<string>` | Same field; read-only set over a private `HashSet<string>` |
| `Vericairn.Records.Estate.Routing.ReservedRoles` | `public static IReadOnlyDictionary<string, string> { get; }` backed by `Dictionary` | Same property; `ReadOnlyDictionary` over private storage |
| `Vericairn.Records.Estate.GoalPredicates.States` | `public static IReadOnlyDictionary<string, IReadOnlyList<string>> { get; }` backed by `Dictionary` | Same property; `ReadOnlyDictionary` over private storage, retaining the read-only lists |
| `Vericairn.Protocol.ProtocolJson.Options` | `public static JsonSerializerOptions { get; }`; writable until first serialization | Same property and serialization settings; read-only on creation |
| `Vericairn.Runtime.Workers.WorkerOptions.Default` | `public static WorkerOptions { get; }`; configurations backed by mutable dictionary | Same property; its empty configurations are read-only |

To customize JSON conventions, construct `new JsonSerializerOptions(ProtocolJson.Options)`
and change that private copy. Do not mutate the shared defaults.

## Supplied option collections

The following properties retain their declared types and public `get; init;`
accessors. Previously they retained caller-owned collections. Construction,
object initialization and `with` assignment now copy the supplied collection into
owned read-only storage. Null optional collections stay null; values and order
stay the same. Dictionary copies preserve the key comparison of standard mutable
and immutable dictionary implementations. An opaque dictionary view does not expose
its comparer; its copied keys use ordinal lookup. To retain a different comparison,
supply a dictionary that exposes its comparer. Already immutable dictionary storage
can be shared safely. Nested worker configuration inventories
are owned at their corresponding record boundaries, so copying only the outer
configuration dictionary cannot leave a mutable path into the worker.

| Exported member | Old shape | New shape |
| --- | --- | --- |
| `Vericairn.Runtime.Workers.WorkerOptions.Configurations` | `public IReadOnlyDictionary<string, RunConfiguration> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.WorkerOptions.Environment` | `public IReadOnlyDictionary<string, string>? { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.WorkerOptions.Vericairn` | `public IReadOnlyList<string>? { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunConfiguration.Models` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunConfiguration.Settings` | `public IReadOnlyDictionary<string, string>? { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunConfiguration.Efforts` | `public IReadOnlyList<string>? { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunEnvironmentPolicy.Components` | `public IReadOnlyList<RunComponent> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunComponent.Environment` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunComponent.Destinations` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunComponent.Dependencies` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.WorkerConfiguration.AllowedModels` | `public IReadOnlyList<string>? { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.WorkerConfiguration.AllowedEfforts` | `public IReadOnlyList<string>? { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunCapabilities.Scopes` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunCapabilities.Efforts` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunCapabilities.Providers` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunCapabilities.Destinations` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |
| `Vericairn.Runtime.Workers.RunCapabilities.DataBoundaries` | `public IReadOnlyList<string> { get; init; }`; caller-owned | Same signature; owned snapshot |

The scalar properties below are declared explicitly alongside the copied properties
to retain their original serialization order. Both columns include their public
`get; init;` accessors; their values and ownership are unchanged.

| Exported member | Old synthesized shape | New explicit shape |
| --- | --- | --- |
| `Vericairn.Runtime.Workers.RunConfiguration.Name` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunConfiguration.Launcher` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunConfiguration.Policy` | `public JsonElement { get; init; }` | `public JsonElement { get; init; }` |
| `Vericairn.Runtime.Workers.RunConfiguration.EnvironmentPolicy` | `public RunEnvironmentPolicy? { get; init; }` | `public RunEnvironmentPolicy? { get; init; }` |
| `Vericairn.Runtime.Workers.RunComponent.Id` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunComponent.Kind` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunComponent.Version` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunComponent.Hash` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunComponent.ConfigurationHash` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunEnvironmentPolicy.Id` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.WorkerConfiguration.Model` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.WorkerConfiguration.Effort` | `public string { get; init; }` | `public string { get; init; }` |
| `Vericairn.Runtime.Workers.RunCapabilities.ProviderDefault` | `public bool { get; init; }` | `public bool { get; init; }` |
| `Vericairn.Runtime.Workers.RunCapabilities.ClosedEnvironment` | `public bool { get; init; }` | `public bool { get; init; }` |

The positional constructor and generated `Deconstruct` signatures of
`RunConfiguration`, `RunEnvironmentPolicy`, `RunComponent`, `WorkerConfiguration`
and `RunCapabilities` stay the same. Copy constructors and `with` copies may share
already owned immutable storage. Equality remains record equality, with collection
properties compared by their collection equality semantics, not by element value;
separately constructed snapshots need not compare equal. These are configuration
objects, not changes to stored record envelopes or protocol request/response DTOs.

## Facilities removed from the runtime's exported surface

`Vericairn.Runtime.Providers.FakeReleaseProvider` is a test double, with no recorded
decision to ship it as a supported provider. It moves to
`tests/TestSupport/FakeReleaseProvider.cs`, in `Vericairn.TestSupport`, as an
`internal sealed class` source-linked by each consuming test project. No test
support assembly or source enters the runtime package. NuGet callers that used the
fake must provide their own `IReleaseProvider` implementation; there is no runtime
type forwarder. Every declared exported member removed with it is listed below.
The test implementation retains these members and their behavior.

| Old runtime export | New runtime shape |
| --- | --- |
| `public sealed class Vericairn.Runtime.Providers.FakeReleaseProvider : IReleaseProvider` | Removed; internal test support type |
| `FakeReleaseProvider.DispatchBehavior` (public nested enum) | Removed; nested enum in test support |
| `FakeReleaseProvider.DispatchBehavior.Normal` | Removed; same test support value |
| `FakeReleaseProvider.DispatchBehavior.RefuseAuthentication` | Removed; same test support value |
| `FakeReleaseProvider.DispatchBehavior.Uncertain` | Removed; same test support value |
| `FakeReleaseProvider(string provider = "github-releases")` | Removed; same test support constructor |
| `DispatchBehavior NextDispatch { get; set; }` | Removed; same test support property |
| `string HeadSha { get; set; }` | Removed; same test support property |
| `IReadOnlyList<DispatchRequest> Dispatches { get; }` | Removed; same test support property |
| `string Provider { get; }` | Removed; same test support property |
| `ProviderDescriptor Descriptor { get; }` | Removed; same test support property |
| `string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings)` | Removed; same test support method |
| `DispatchReceipt Dispatch(DispatchRequest request)` | Removed; same test support method |
| `void CompleteRun(string repository, string runId, bool success, string version, IReadOnlyList<string>? names = null, string checksums = "SHA256SUMS", IReadOnlyDictionary<string, byte[]>? contents = null)` | Removed; same test support method |
| `void Publish(string repository, string tag)` | Removed; same test support method |
| `RunObservation? ObserveRun(string destination, string runId)` | Removed; same test support method |
| `ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings)` | Removed; same test support method |
| `string ManualPublicationCommand(string destination, string tag)` | Removed; same test support method |
| `byte[]? ReadAsset(string destination, string tag, string name)` | Removed; same test support method |
| `Vericairn.Runtime.Workers.WorkerOptions.Minute`: `public TimeSpan { get; init; }` | `internal TimeSpan { get; init; }`; test clock scaling seam, still one minute by default |

`Minute` is not a supported time-unit override. Consumers use the existing
`Heartbeat`, `Grace` and `StopWait` options for worker pacing. Tests reach the
internal seam through the existing friend assemblies.

## Infrastructure retained intentionally

- `RecordedSourceAdapter` stays in the runtime. It is the documented replaying
  implementation of `ISourceAdapter` for offline recorded observations and the
  shared conformance suite. Its public constructor, descriptor and methods stay
  unchanged; a replay does not qualify a live repository.
- Provider, launcher, workspace, storage and migration interfaces remain public
  extension points. `ProviderRegistry.Current` is deliberately a host registration
  service; its explicit registration/removal methods remain available. It is not
  a vocabulary or default value. Registry reads return separate lists.
- `PairingOptions.KeyStore`, `DaemonOptions.IdentityStore`, clocks and logging
  delegates support host integration as well as tests. They remain public.
  Existing internal failure-injection hooks, migration overrides and process
  observation hooks stay internal; their test implementations stay under `tests/`.
- Existing interface-typed collection-expression vocabularies in Protocol, SDK,
  MCP and Workbench, and `StateLanguage.Vocabularies` with its nested state lists,
  already have read-only backing. Their signatures and contents remain unchanged.
  `ReleaseProfile.None`, `RunCapabilities.Unsupported`, communication and shell
  defaults remain immutable shared values. `RecordTypeRegistry.All` and
  `RecordFormats.Reads` return independent collections.

`docs/software.md` describes the relocated fake and retained source adapter. No
frozen release documentation or generated reference is part of this change.
