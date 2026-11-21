// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.4 (OPS-22): writes a request's pinned files, under their names, and its guide into a folder, from the estate's objects, so the
/// operator can fetch and finish it from any copy of the estate. It refuses before writing anything when an object is missing or does not
/// match its hash, a name is not a plain file name, or a file it would write exists already. It signs and records nothing.
/// </summary>
internal sealed class OperatorFetchCommand : Command
{
    /// <summary>The name of the guide written beside the request's files.</summary>
    internal const string GuideName = "guide.md";

    public OperatorFetchCommand(CommandContext context)
        : base("fetch", "Write a request's pinned files, under their names, and its guide (guide.md) into a folder, read from the estate's objects and checked against their hashes. Refuses, writing nothing, when an object is missing, a name is not a plain file name or a file exists already. Records nothing.")
    {
        var id = new Argument<string>("request") { Description = "The current revision of the operator request." };
        var to = new Option<string>("--to") { Required = true, Description = "The folder to write into; created when missing. Existing files are never replaced." };
        Arguments.Add(id);
        Options.Add(to);
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            bool json = parse.GetValue(context.JsonOption);
            RecordResponse? response = api.Record(parse.GetValue(id)!);
            if (response is null || response.Type != "operator-request")
                return Refuse("No operator request has that id; nothing was written.");
            if (!response.Current)
                return Refuse($"Request revision {response.Id} is superseded; fetch its current revision, which vericairn operator list shows. Nothing was written.");
            var findings = new FindingList();
            // The envelope as the record's file holds it, in canonical form; its signatures and hash are the runtime's to have checked.
            if (RecordParser.Parse(Records.Canonical.CanonicalJson.SerializePrettyUtf8(response.Envelope), response.Path, findings) is not RecordEnvelope request
                || findings.HasErrors || request.ComputedHash != response.Hash)
                return Refuse("The request record could not be read; nothing was written.");

            var files = new List<(string Name, string Hash, byte[] Bytes)>();
            var problems = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { GuideName };
            foreach (JsonElement item in request.Body.GetProperty("content").EnumerateArray().Where(i => Text(i, "kind") == "file"))
            {
                string name = Text(item, "name") ?? string.Empty, hash = Text(item, "hash") ?? string.Empty;
                if (!PlainName(name)) { problems.Add($"{name} is not a plain file name"); continue; }
                if (!names.Add(name)) { problems.Add($"{name} is named twice, or as the guide"); continue; }
                byte[]? bytes;
                try { bytes = api.ObjectGet(hash); }
                catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { bytes = null; problems.Add($"{name} ({hash}) could not be read: {exception.Message}"); continue; }
                if (bytes is null) problems.Add($"the estate's objects do not hold {name} ({hash})");
                else if (ContentHash.Sha256(bytes) != hash) problems.Add($"the object for {name} does not match {hash}");
                else files.Add((name, hash, bytes));
            }

            string folder = Path.GetFullPath(parse.GetValue(to)!);
            foreach (string name in names.Where(n => File.Exists(Path.Combine(folder, n)) || Directory.Exists(Path.Combine(folder, n))))
                problems.Add($"{name} exists already in the folder, and is never replaced");
            if (problems.Count > 0)
                return Refuse("Nothing was written: " + string.Join("; ", problems) + ".");

            Directory.CreateDirectory(folder);
            foreach ((string name, _, byte[] bytes) in files) Write(Path.Combine(folder, name), bytes);
            Write(Path.Combine(folder, GuideName), System.Text.Encoding.UTF8.GetBytes(OperatorRequests.Guide(request)));
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new
                {
                    request = request.Id, hash = request.ComputedHash, guide = GuideName,
                    files = files.Select(f => new { name = f.Name, hash = f.Hash, bytes = f.Bytes.LongLength }).ToArray(),
                }, ProtocolJson.Options));
            }
            else
            {
                foreach ((string name, string hash, _) in files) context.Output.WriteLine($"Wrote {name} at {hash}");
                context.Output.WriteLine($"Wrote {GuideName}: request {request.Id}'s guide and the commands that finish it.");
            }

            return CommandContext.Clean;

            int Refuse(string message)
            {
                if (json) context.Output.WriteLine(JsonSerializer.Serialize(new { problem = new Problem("refused", message, []) }, ProtocolJson.Options));
                else context.Error.WriteLine(message);
                return CommandContext.HasErrors;
            }
        }, readOnly: true));
    }

    // A plain file name: no directory, no parent, no drive, nothing a file system would read as a path.
    private static bool PlainName(string name) => name.Length is > 0 and <= 255 && name is not ("." or "..") && name.Trim() == name && !name.EndsWith('.')
        && name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\', ':']) < 0 && Path.GetFileName(name) == name;

    private static void Write(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
