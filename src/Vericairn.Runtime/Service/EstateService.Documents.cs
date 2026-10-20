// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public DocumentPinResponse DocumentPin(DocumentPinRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By)) return Refused("The authoring principal must be a ULID.");
        if (string.IsNullOrWhiteSpace(request.Path) || !Patterns.DocumentPath.IsMatch(request.Path)
            || !DocumentPatterns.IsSafePath(request.Path))
            return Refused("A document pin needs a normalized repository-relative file path under design/ or docs/.");
        if (string.IsNullOrWhiteSpace(request.Summary)) return Refused("A document pin needs a summary.");
        if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title)) return Refused("A document title cannot be empty.");
        if (request.Expected is not null && !Ulid.IsValid(request.Expected)) return Refused("The expected pin must be a record id (ULID).");

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope[] pins = model.Current("document-revision")
                .Where(r => EstateModel.BodyString(r, "path") == request.Path).ToArray();
            if (pins.Length > 1) return Refused(DocumentRevisionType.MultipleMessage(request.Path, pins));
            RecordEnvelope? previous = pins.SingleOrDefault();
            if (request.Expected is not null && request.Expected != previous?.Id)
                return Refused($"Expected pin {request.Expected} is not current for {request.Path}; current pin: {previous?.Id ?? "none"}. Read the current pin before retrying.");

            RecordEnvelope? generated = model.Current("living-document").FirstOrDefault(d =>
                EstateModel.BodyString(d, "repository") is null && EstateModel.BodyString(d, "kind") == "generated"
                && !GeneratedDocumentContract.IsRetired(model, d) && GeneratedDocumentContract.Owns(d, request.Path));
            if (generated is not null)
                return Refused($"{request.Path} belongs to generated document {generated.Id}; generated documents take no authored output pin.");

            byte[] bytes;
            try
            {
                string file = GeneratedDocumentVerification.SafeFile(session.Root, request.Path);
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return Refused("The document could not be read from the estate repository; check that it exists with its exact path and without symbolic links or case aliases.");
            }

            string hash = ContentHash.Sha256(bytes);
            if (previous is not null && EstateModel.BodyString(previous, "hash") == hash)
                return Refused($"{request.Path} has unchanged bytes; nothing to pin (current pin {previous.Id}).");
            long revision = previous is null ? 1 : previous.Body.GetProperty("revision").GetInt64() + 1;
            string title = request.Title ?? (previous is null ? null : EstateModel.BodyString(previous, "title"))
                ?? Path.GetFileNameWithoutExtension(request.Path);
            JsonElement body = JsonSerializer.SerializeToElement(new
            {
                path = request.Path, title, hash, bytes = bytes.LongLength,
                mediaType = DocumentMediaType(request.Path), revision, summary = request.Summary,
            });
            WriteOutcome outcome = WriteLocked(new RecordDraft("document-revision", request.By, body, Supersedes: previous?.Id), model);
            return new(outcome.Written, outcome.Written is null ? null : revision, outcome.Problem);
        }

        static DocumentPinResponse Refused(string message) => new(null, null, new Problem("refused", message, []));
    }

    private static string DocumentMediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" or ".markdown" => "text/markdown",
        ".txt" or ".rst" or ".adoc" => "text/plain",
        ".json" => "application/json",
        ".yaml" or ".yml" => "application/yaml",
        ".html" or ".htm" => "text/html",
        ".xml" => "application/xml",
        ".csv" => "text/csv",
        ".pdf" => "application/pdf",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };
}
