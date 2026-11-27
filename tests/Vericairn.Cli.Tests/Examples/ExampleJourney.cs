// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

internal sealed record ExampleJourney(string Format, DateTimeOffset StartsAt, JourneyStep[] Steps)
{
    private static readonly JsonSerializerOptions Schema = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    internal static ExampleJourney Read(string path)
    {
        ExampleJourney journey = JsonSerializer.Deserialize<ExampleJourney>(File.ReadAllText(path), Schema)
            ?? throw new InvalidDataException("The journey is empty.");
        if (journey.Format != "vericairn.example-journey/1" || journey.Steps.Length == 0 || journey.StartsAt == default)
            throw new InvalidDataException("A journey needs its format, a clock start and at least one step.");
        var known = new HashSet<string>(StringComparer.Ordinal) { "scratch", "estate", "home" };
        var steps = new HashSet<string>(StringComparer.Ordinal);
        foreach (JourneyStep step in journey.Steps)
        {
            if (step is null || !ValidName(step.Id) || !steps.Add(step.Id) || !ValidName(step.Role) || step.Arguments.Length == 0
                || step.Expect.ExitCode is < 0 or > 255)
                throw new InvalidDataException("Each step needs a unique id, a role, a command and a valid expected exit code.");
            if ((step.Clock is null) != (step.InstantArguments is null) || step.Clock is <= 0)
                throw new InvalidDataException($"Step {step.Id} needs a positive clock advance and a corresponding instant command.");
            if (step.Variant is not (null or "agents" or "people"))
                throw new InvalidDataException($"Step {step.Id} has an unknown variant.");
            if (step.Driver is not (null or "in-process" or "process")
                || step.Context is not null && !ValidName(step.Context)
                || step.Surface is not null && !ValidName(step.Surface))
                throw new InvalidDataException($"Step {step.Id} has an invalid driver, context or surface.");
            if (step.Files?.Keys.Any(path => !ValidFixturePath(path)) == true)
                throw new InvalidDataException($"Step {step.Id} needs scratch-relative fixture paths.");
            if (step.Provider && !IsProvider(step.Arguments))
                throw new InvalidDataException($"Step {step.Id} cannot use the fake provider for this command.");
            if (step.StandIn is not (null or "native-a" or "native-b")
                || step.StandIn is not null && step.Arguments is not ["worker", "run" or "qualify", ..])
                throw new InvalidDataException($"Step {step.Id} has a stand-in outside a worker run or preflight.");
            foreach (string[] args in new[] { step.Arguments, step.InstantArguments, step.ProcessArguments }.OfType<string[]>())
            {
                if (args.Length == 0 || args[0] is null || args[0].StartsWith('-') || args.Any(a => a is null || a.Contains('\0')
                        || a.Split('=', 2)[0] is "--estate" or "--json" or "--setup-command" or "--basis"))
                    throw new InvalidDataException($"Step {step.Id} must use the journey's own command context.");
                foreach (string name in args.SelectMany(Placeholders))
                    if (!known.Contains(name))
                        throw new InvalidDataException($"Step {step.Id} uses unknown capture '{name}'.");
            }
            if (step.ProcessArguments is not null && step.Arguments is not ["worker", "run", ..])
                throw new InvalidDataException($"Step {step.Id} may substitute process arguments only for a worker run.");
            if ((step.ProcessArguments is null) != (step.ProcessExpect is null)
                || step.ProcessArguments is not null && !AgentCheck(step.ProcessArguments))
                throw new InvalidDataException($"Step {step.Id} must preview its agent step with goal step --check.");
            if (step.Capture is null) continue;
            foreach ((string key, string pathValue) in step.Capture)
                if (!ValidName(key) || !known.Add(key) || !ValidPath(pathValue))
                    throw new InvalidDataException($"Step {step.Id} has an invalid or duplicate capture '{key}'.");
        }
        return journey;
    }

    internal static bool IsProvider(string[] args) => args is ["release", "submit" or "observe" or "publish", ..] && !args.Contains("--check");
    internal static bool AgentCheck(string[] args) => args is ["goal", "step", ..] && args.Contains("--check");

    internal static string Guide(ExampleJourney journey) => string.Concat(
        "```sh\n",
        string.Join('\n', journey.Steps.Where(step => !step.Setup).Select(step =>
            "vericairn " + string.Join(' ', step.Arguments.Select(a => Regex.IsMatch(a, @"^[A-Za-z0-9_./:=<>-]+$") ? a : "'" + a.Replace("'", "'\\''", StringComparison.Ordinal) + "'")))),
        "\n```\n");

    internal static JsonNode? At(JsonNode? output, string path)
    {
        if (!ValidPath(path)) throw new InvalidDataException($"Invalid JSON assertion path '{path}'.");
        foreach (string component in path.Split('/').Skip(1))
        {
            string key = component.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (output is JsonObject obj && obj.TryGetPropertyValue(key, out JsonNode? value))
                output = value;
            else if (output is JsonArray array && int.TryParse(key, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int index) && index < array.Count)
                output = array[index];
            else throw new InvalidDataException($"JSON output does not have '{path}'.");
        }
        return output;
    }

    private static bool ValidName(string value) => Regex.IsMatch(value, "^[a-z][a-zA-Z0-9-]*$");
    private static bool ValidPath(string value) => value.StartsWith('/') && !Regex.IsMatch(value, "~(?![01])");
    internal static bool ValidFixturePath(string path) => path.Split('/').All(part => Regex.IsMatch(part, @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*$"));
    internal static IEnumerable<string> Placeholders(string argument) => Regex.Matches(argument, @"\$\{([a-z][a-zA-Z0-9-]*)\}").Select(match => match.Groups[1].Value);
    internal static string Expand(string argument, IReadOnlyDictionary<string, string> values) =>
        Regex.Replace(argument, @"\$\{([a-z][a-zA-Z0-9-]*)\}", match => values.TryGetValue(match.Groups[1].Value, out string? value) ? value
            : throw new InvalidDataException($"Capture '{match.Groups[1].Value}' is unavailable in this variant."));

    internal static JsonNode? ExpandJson(JsonNode? node, IReadOnlyDictionary<string, string> values) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(field => KeyValuePair.Create(field.Key, ExpandJson(field.Value, values)))),
        JsonArray array => new JsonArray(array.Select(item => ExpandJson(item, values)).ToArray()),
        JsonValue scalar when scalar.TryGetValue(out string? value) => JsonValue.Create(Expand(value, values)),
        _ => node?.DeepClone(),
    };
}

internal sealed record JourneyStep(string Id, string Role, string[] Arguments, JourneyExpectation Expect,
    Dictionary<string, string>? Capture = null, bool Setup = false, int? Clock = null, string[]? InstantArguments = null,
    string[]? ProcessArguments = null, JourneyExpectation? ProcessExpect = null, string? Variant = null, bool Provider = false, string? StandIn = null,
    Dictionary<string, JsonNode?>? Files = null, string? Context = null, string? Driver = null, string? Surface = null, bool Text = false);

internal sealed record JourneyExpectation(int ExitCode, Dictionary<string, JsonNode?>? Fields = null, string? State = null, string? Refusal = null,
    string? Verdict = null, string? ErrorContains = null, string? OutputContains = null)
{
    internal void Assert(CliHarness.Outcome outcome, IReadOnlyDictionary<string, string> captures)
    {
        if (outcome.Code != ExitCode) throw new InvalidDataException($"Expected exit {ExitCode}, got {outcome.Code}. {outcome.Err} {outcome.Out}");
        if (ErrorContains is not null && !outcome.Err.Contains(ExampleJourney.Expand(ErrorContains, captures), StringComparison.Ordinal))
            throw new InvalidDataException($"Expected diagnostic containing '{ErrorContains}'.");
        if (OutputContains is not null && !outcome.Out.Contains(ExampleJourney.Expand(OutputContains, captures), StringComparison.Ordinal))
            throw new InvalidDataException($"Expected output containing '{OutputContains}'.");
        if (Fields is null && State is null && Refusal is null && Verdict is null) return;
        JsonNode? output = JsonNode.Parse(outcome.Out);
        var assertions = Fields is null ? new Dictionary<string, JsonNode?>(StringComparer.Ordinal) : new(Fields, StringComparer.Ordinal);
        string scope = output is JsonObject root && root["step"] is JsonObject ? "/step" : "";
        if (State is not null) assertions.Add(scope + "/state", JsonValue.Create(State));
        if (Refusal is not null) assertions.Add(scope + "/problem/code", JsonValue.Create(Refusal));
        if (Verdict is not null) assertions.Add(scope + "/exposure/verdict", JsonValue.Create(Verdict));
        foreach ((string path, JsonNode? template) in assertions)
        {
            JsonNode? value = ExampleJourney.ExpandJson(template, captures);
            if (!JsonNode.DeepEquals(value, ExampleJourney.At(output, path)))
                throw new InvalidDataException($"Unexpected JSON field '{path}': expected {value?.ToJsonString() ?? "null"}.");
        }
    }
}
