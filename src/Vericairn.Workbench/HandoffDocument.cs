// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;

namespace Vericairn.Workbench;

/// <summary>A retained hand-over and its current comparison, both through the runtime. Refresh never replaces the basis.</summary>
public sealed class HandoffDocument(Func<IEstateApi?> api, Func<CommunicationProfile>? profile = null, Func<string?>? acting = null) : Document("Hand-over and resume", "handoff")
{
    private HandoffPackage? package;
    private IReadOnlyList<DesktopHandoff> remoteHandoffs = [];
    private DesktopHandoff? selectedRemoteHandoff;
    private string? remotePrincipal;
    private string remoteText = string.Empty;
    private string remoteNote = "Choose an acting person to see their handoffs.";
    private string text = "Prepare a hand-over or open a saved package.", resumeText = "No comparison yet.", note = "Open a handoff from Remote in the From Remote tab.";

    /// <summary>The acting person's handoffs prepared in Remote.</summary>
    public IReadOnlyList<DesktopHandoff> RemoteHandoffs { get => remoteHandoffs; private set => Set(ref remoteHandoffs, value); }

    /// <summary>The row chosen for explicit opening.</summary>
    public DesktopHandoff? SelectedRemoteHandoff
    {
        get => selectedRemoteHandoff;
        set { if (Set(ref selectedRemoteHandoff, value)) Raise(nameof(CanOpenRemoteHandoff)); }
    }

    /// <summary>Whether there is a retained handoff to open.</summary>
    public bool CanOpenRemoteHandoff => SelectedRemoteHandoff is not null;

    /// <summary>The handoff and checked originals currently open in Workbench.</summary>
    public string RemoteText { get => remoteText; private set => Set(ref remoteText, value); }

    /// <summary>Availability and observed opening status.</summary>
    public string RemoteNote { get => remoteNote; private set => Set(ref remoteNote, value); }

    /// <summary>Discard the previous person's view before any new reads.</summary>
    public void ClearRemoteHandoffs()
    {
        remotePrincipal = null;
        SelectedRemoteHandoff = null;
        RemoteHandoffs = [];
        RemoteText = string.Empty;
        RemoteNote = "Choose an acting person to see their handoffs.";
    }

    /// <summary>Refresh the acting person's list without marking any handoff opened.</summary>
    public async Task ReadRemoteHandoffsAsync(CancellationToken cancellationToken = default)
    {
        string? person = acting?.Invoke();
        if (person != remotePrincipal) ClearRemoteHandoffs();
        if (person is null || api() is not { } estate) return;
        remotePrincipal = person;
        await ReadAsync(async _ =>
        {
            DesktopHandoffsResponse response;
            try
            {
                response = await estate.DesktopHandoffsAsync(person, cancellationToken).ConfigureAwait(true);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                response = new([], new("unsupported", "This runtime does not support desktop handoffs.", []));
            }
            if (person != acting?.Invoke() || !ReferenceEquals(estate, api())) return;
            string? selected = SelectedRemoteHandoff?.Id;
            RemoteHandoffs = response.Handoffs;
            SelectedRemoteHandoff = RemoteHandoffs.FirstOrDefault(item => item.Id == selected);
            RemoteNote = response.Problem?.Message ?? (RemoteHandoffs.Count == 0 ? "No handoffs from Remote." : "Choose a handoff to open.");
        }).ConfigureAwait(true);
    }

    /// <summary>Show pinned context and observe the explicit opening. Nothing is submitted on the work's behalf.</summary>
    public async Task OpenRemoteHandoffAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedRemoteHandoff is not { } selected || acting?.Invoke() is not { } person ||
            selected.By != person || api() is not { } estate) return;
        await ReadAsync(async _ =>
        {
            var originals = new StringBuilder();
            foreach (AttentionReference pin in selected.Evidence.Prepend(selected.Subject).Distinct())
            {
                RecordResponse? record = await estate.RecordAsync(pin.Id, cancellationToken).ConfigureAwait(true);
                originals.Append('\n').Append(pin.Type).Append(' ').Append(pin.Id).Append(" at ").Append(pin.Hash).Append('\n');
                originals.Append(record is not null && record.Type == pin.Type && record.Hash == pin.Hash
                    ? CanonicalJson.SerializePretty(record.Envelope)
                    : "The exact original is unavailable on this runtime.");
                originals.Append('\n');
            }

            if (person != acting?.Invoke() || !ReferenceEquals(estate, api())) return;
            DesktopHandoffResponse response = await estate.OpenDesktopHandoffAsync(new(person, selected.Id), cancellationToken).ConfigureAwait(true);
            if (person != acting?.Invoke() || !ReferenceEquals(estate, api())) return;
            if (response.Handoff is not { } handoff)
            {
                RemoteNote = response.Problem?.Message ?? "Opening has not been observed. Refresh Handoffs before retrying.";
                return;
            }

            RemoteNote = $"Opened in Workbench at {handoff.OpenedAt}.";
            RemoteText = $"{handoff.Task}\n\nReason for handoff\n{handoff.Reason}\n\nEstate {handoff.Estate} · Runtime {handoff.Node ?? "identity unavailable"}\nPrepared at {handoff.PreparedAt}\n{(handoff.ExpiresAt is { } expiry ? $"Expires at {expiry}\n" : "")}Subject revision: {handoff.SubjectState}\nObserved work status: {handoff.SubjectStatus ?? "unavailable here"}\nThe chosen task still needs its own review and completion.\n"
                + (handoff.Draft is null ? string.Empty : "\nDraft from Remote\n" + handoff.Draft + "\n")
                + "\nOriginals\n" + originals;
            RemoteHandoffs = RemoteHandoffs.Select(item => item.Id == handoff.Id ? handoff : item).ToArray();
            SelectedRemoteHandoff = handoff;
        }).ConfigureAwait(true);
    }

    /// <summary>Remove the selected handoff and its private draft from this runtime.</summary>
    public async Task RemoveRemoteHandoffAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedRemoteHandoff is not { } selected || acting?.Invoke() is not { } person ||
            selected.By != person || api() is not { } estate) return;
        await ReadAsync(async _ =>
        {
            DesktopHandoffRemoveResponse response = await estate.RemoveDesktopHandoffAsync(new(person, selected.Id), cancellationToken).ConfigureAwait(true);
            if (person != acting?.Invoke() || !ReferenceEquals(estate, api())) return;
            if (!response.Removed)
            {
                RemoteNote = response.Problem?.Message ?? "Removal has not been observed. Refresh Handoffs before retrying.";
                return;
            }

            RemoteHandoffs = RemoteHandoffs.Where(item => item.Id != selected.Id).ToArray();
            SelectedRemoteHandoff = null;
            RemoteText = string.Empty;
            RemoteNote = "The selected handoff was removed.";
        }).ConfigureAwait(true);
    }

    /// <summary>The retained package, or none. Only an explicit prepare or successful load replaces it.</summary>
    public HandoffPackage? Package => package;

    /// <summary>The complete human view, with original records apart from its generated presentation.</summary>
    public string Text { get => text; private set => Set(ref text, value); }

    /// <summary>The complete changed-since comparison, with the runtime's warning about external change.</summary>
    public string ResumeText { get => resumeText; private set => Set(ref resumeText, value); }

    /// <summary>Availability, a refusal, or the outcome of the last explicit action.</summary>
    public string Note { get => note; private set => Set(ref note, value); }

    /// <summary>The exact package identity and measured payload bytes.</summary>
    public string Identity => package is null ? "No retained package." : $"{package.Hash} · {package.Bytes.ToString(CultureInfo.InvariantCulture)} payload bytes";

    /// <summary>The portable artifact, identical to the CLI's saved machine form.</summary>
    public string PackageJson => package is null ? string.Empty : CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(package, ProtocolJson.Options));

    /// <summary>Whether a package can be saved or compared.</summary>
    public bool HasPackage => package is not null;

    /// <summary>Ask the runtime to prepare a new complete basis; keep the old one if it refuses.</summary>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        await ReadAsync(async estate =>
        {
            HandoffResponse response = (await estate.HandoffAsync(cancellationToken: cancellationToken).ConfigureAwait(true));
            if (response.Package is null)
            {
                Note = "A new hand-over was refused.\n" + response.Text;
                return;
            }

            Keep(response.Package, response.Text);
            ResumeText = "No comparison yet. Resume or refresh to check changes since this package.";
            Note = "Hand-over prepared. Save the package to continue on another machine. Open a handoff from Remote in the From Remote tab.";
        }).ConfigureAwait(true);
    }

    /// <summary>Compare against the current runtime while keeping the original basis and its bytes.</summary>
    public async Task CompareAsync(CancellationToken cancellationToken = default)
    {
        if (package is null)
        {
            Note = "Prepare a hand-over or open a saved package first.";
            return;
        }

        await ReadAsync(async estate =>
        {
            ResumeResponse response = (await estate.ResumeHandoffAsync(package, cancellationToken: cancellationToken).ConfigureAwait(true));
            ResumeText = response.Text;
            Note = response.Problem is not null ? response.Problem.Message
                : response.HasErrors ? "The current copy is incomplete or invalid. Review every change and finding before continuing."
                : "Comparison refreshed. The world may have moved; recheck authority and required originals before acting. Open a handoff from Remote in the From Remote tab.";
        }).ConfigureAwait(true);
    }

    /// <summary>Load a saved package only after the runtime validates its integrity, estate and completeness.</summary>
    public async Task<bool> LoadAsync(string json, CancellationToken cancellationToken = default)
    {
        bool loaded = false;
        await ReadAsync(async estate =>
        {
            HandoffPackage? candidate = JsonSerializer.Deserialize<HandoffPackage>(json, ProtocolJson.Options);
            if (candidate is null)
            {
                Note = "No hand-over package was found.";
                return;
            }

            ResumeResponse response = (await estate.ResumeHandoffAsync(candidate, cancellationToken: cancellationToken).ConfigureAwait(true));
            if (response.Problem is not null)
            {
                Note = response.Text;
                return;
            }

            Keep(candidate, RetainedText(candidate));
            ResumeText = response.Text;
            Note = response.HasErrors ? "Package loaded; this copy has missing or invalid state. Review the comparison before continuing."
                : "Package loaded and compared with this estate. The world may have moved. Open a handoff from Remote in the From Remote tab.";
            loaded = true;
        }).ConfigureAwait(true);
        return loaded;
    }

    /// <summary>Save in a new file; an existing retained package is never overwritten.</summary>
    public async Task<bool> SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        if (package is null)
        {
            Note = "There is no retained package to save.";
            return false;
        }

        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await file.WriteAsync(Encoding.UTF8.GetBytes(PackageJson), cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
            Note = "Package saved. Use Open package here or vericairn resume to continue from it.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Note = "Package was not saved: " + exception.Message;
            return false;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Mark retained content as cached when the runtime is unavailable; discard no original.</summary>
    public void Unavailable()
    {
        ClearRemoteHandoffs();
        RemoteNote = "Handoffs from Remote are unavailable. Refresh after reconnecting.";
        Note = "The runtime is unavailable or does not support hand-over and resume. Retained content is cached; its comparison is not current.";
    }

    private void Keep(HandoffPackage retained, string human)
    {
        package = retained;
        Text = ContextTextMeasurement.Presentation(profile is null ? human : CommunicationPresentation.Protected(profile(), "handoff", retained.Hash, "Complete hand-over; its package and originals remain unchanged.", human)).Text;
        Raise(nameof(Package));
        Raise(nameof(Identity));
        Raise(nameof(PackageJson));
        Raise(nameof(HasPackage));
    }

    private async Task ReadAsync(Func<IEstateApi, Task> operation)
    {
        if (api() is not { } estate)
        {
            Unavailable();
            return;
        }

        try
        {
            await operation(estate).ConfigureAwait(true);
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null)
        {
            Note = ClientFailure.FromException(exception)!.Message;
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or JsonException or CanonicalJsonException or ArgumentException or FormatException or KeyNotFoundException)
        {
            Note = "The operation could not be completed; retained content is cached. " + exception.Message;
        }
    }

    private static string RetainedText(HandoffPackage retained)
    {
        JsonElement payload = retained.Payload;
        var result = new StringBuilder("Retained hand-over ").Append(retained.Hash).Append("\nThe world may have moved. This package grants no authority. Open a handoff from Remote in the From Remote tab.\n");
        result.Append("\nDerived presentation (deterministic; synthesis off; not accepted)\n")
            .Append(payload.GetProperty("presentation").GetProperty("text").GetString())
            .Append("\nObserved state (records only)\n").Append(CanonicalJson.SerializePretty(payload.GetProperty("observedState")))
            .Append("\nOriginals (complete)\n");
        foreach (JsonElement source in payload.GetProperty("originals").EnumerateArray())
        {
            result.Append('\n').Append(source.GetProperty("id").GetString()).Append(" at ").Append(source.GetProperty("hash").GetString()).Append('\n')
                .Append(Encoding.UTF8.GetString(Convert.FromBase64String(source.GetProperty("content").GetString()!)));
        }

        return result.ToString();
    }
}
