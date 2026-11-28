// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Remote;

/// <summary>An installed collection, named for browsing in Remote.</summary>
public sealed record RemoteCompanionCollectionRow(string Name, CompanionGeneratorRow Generator);

/// <summary>An item named by the shared reader, with its exact permitted revision.</summary>
public sealed record RemoteCompanionItem(string Name, AttentionReference Source);

public sealed partial class RemoteViewModel
{
    private const string RecordCompanion = "vericairn.records/items";
    private const string ManifestCompanion = "vericairn.estate/manifest";
    private const string CompanionUnavailable = "The readable companion is unavailable. Refresh to check access and its source.";
    private bool companionsOpen;
    private CompanionRequest? companionRequest;
    private CompanionRequest? returnToCollection;
    private AttentionReference? companionSubject;
    private RemoteCompanionCollectionRow? selectedCompanionCollection;
    private RemoteCompanionItem? selectedCompanionItem;
    private CompanionObservation? companionPage;
    private IReadOnlyList<RemoteCompanionCollectionRow> companionCollections = [];
    private IReadOnlyList<RemoteCompanionItem> companionItems = [];
    private string companionMessage = "";

    /// <summary>Whether the reader is open within Products.</summary>
    public bool CompanionsOpen => companionsOpen;
    /// <summary>Whether Products is showing its ordinary list.</summary>
    public bool ShowProductList => !CompanionsOpen;
    /// <summary>The available collections in the authorized reader catalog.</summary>
    public IReadOnlyList<RemoteCompanionCollectionRow> CompanionCollections => companionCollections;
    /// <summary>The collection selected for inspection.</summary>
    public RemoteCompanionCollectionRow? SelectedCompanionCollection
    {
        get => selectedCompanionCollection;
        set { if (Set(ref selectedCompanionCollection, value)) NotifyCompanionCommands(); }
    }
    /// <summary>Items on this page, supplied as exact references by the runtime.</summary>
    public IReadOnlyList<RemoteCompanionItem> CompanionItems => companionItems;
    /// <summary>The exact item selected in the list.</summary>
    public RemoteCompanionItem? SelectedCompanionItem
    {
        get => selectedCompanionItem;
        set { if (Set(ref selectedCompanionItem, value)) NotifyCompanionCommands(); }
    }
    /// <summary>Generated text from the same renderer as Workbench.</summary>
    public string CompanionText => WebUtility.HtmlDecode(companionPage?.Response.Text ?? "");
    /// <summary>Source, lineage, and the observation time of the actual displayed rendition.</summary>
    public string CompanionFreshness => companionPage is { } page
        ? $"Observed {page.AsOf}; {RemoteFreshness.Age(page.AsOf, clock)}\n" +
            $"Source snapshot {page.Response.Snapshot} · {page.Response.Currentness} at observation · {page.Response.Audience}\n" +
            (page.Response.Page is { } paging ? $"Page {paging.Number} of {paging.Total}" : $"Page {page.Request.Page}")
        : "";
    /// <summary>The outcome of the last companion read.</summary>
    public string CompanionMessage { get => companionMessage; private set => Set(ref companionMessage, value); }
    /// <summary>Whether collection choices are available.</summary>
    public bool ShowCompanionCollections => CompanionCollections.Count > 0;
    /// <summary>Whether this view includes item navigation.</summary>
    public bool ShowCompanionItems => CompanionItems.Count > 0;
    /// <summary>Whether the runtime returned multiple readable pages.</summary>
    public bool ShowCompanionPages => companionPage?.Response.Page?.Total > 1;

    /// <summary>Read the current catalog.</summary>
    public AsyncCommand OpenCompanionsCommand { get; private set; } = null!;
    /// <summary>Read the selected collection.</summary>
    public AsyncCommand OpenCompanionCollectionCommand { get; private set; } = null!;
    /// <summary>Read the selected exact item.</summary>
    public AsyncCommand OpenCompanionItemCommand { get; private set; } = null!;
    /// <summary>Return to the collection, the catalog or Products.</summary>
    public AsyncCommand BackCompanionCommand { get; private set; } = null!;
    /// <summary>Read a fresh rendition of the selected page.</summary>
    public AsyncCommand RefreshCompanionCommand { get; private set; } = null!;
    /// <summary>Read the previous page in the same selection.</summary>
    public AsyncCommand PreviousCompanionPageCommand { get; private set; } = null!;
    /// <summary>Read the next page in the same selection.</summary>
    public AsyncCommand NextCompanionPageCommand { get; private set; } = null!;

    private void InitializeCompanionCommands()
    {
        OpenCompanionsCommand = Command(() =>
        {
            SetCompanionsOpen(true);
            return ReadCompanionsAsync(null);
        }, () => !Busy && Selected is not null);
        OpenCompanionCollectionCommand = Command(() =>
        {
            CompanionGeneratorRow generator = SelectedCompanionCollection!.Generator;
            return ReadCompanionsAsync(new(generator.Id, generator.Id == ManifestCompanion ? "item" : "current", Contract: generator.Contract));
        }, () => !Busy && SelectedCompanionCollection is not null);
        OpenCompanionItemCommand = Command(() =>
        {
            returnToCollection = companionRequest;
            companionSubject = SelectedCompanionItem!.Source;
            return ReadCompanionsAsync(new(RecordCompanion, "item", companionSubject.Id));
        }, () => !Busy && SelectedCompanionItem is not null);
        BackCompanionCommand = Command(BackCompanionAsync, () => !Busy && CompanionsOpen);
        RefreshCompanionCommand = Command(() => ReadCompanionsAsync(companionRequest), () => !Busy && CompanionsOpen);
        PreviousCompanionPageCommand = Command(() => PageCompanionAsync(-1), () => !Busy && companionPage?.Request.Page > 1);
        NextCompanionPageCommand = Command(() => PageCompanionAsync(1), () => !Busy &&
            companionPage?.Response.Page is { } page && page.Number < page.Total);
    }

    private void NotifyCompanionCommands()
    {
        OpenCompanionsCommand?.NotifyCanExecuteChanged();
        OpenCompanionCollectionCommand?.NotifyCanExecuteChanged();
        OpenCompanionItemCommand?.NotifyCanExecuteChanged();
        BackCompanionCommand?.NotifyCanExecuteChanged();
        RefreshCompanionCommand?.NotifyCanExecuteChanged();
        PreviousCompanionPageCommand?.NotifyCanExecuteChanged();
        NextCompanionPageCommand?.NotifyCanExecuteChanged();
    }

    private Task BackCompanionAsync()
    {
        if (companionRequest is null)
        {
            SetCompanionsOpen(false);
            return Task.CompletedTask;
        }

        CompanionRequest? parent = returnToCollection;
        returnToCollection = null;
        companionSubject = null;
        return ReadCompanionsAsync(parent);
    }

    private Task PageCompanionAsync(int change) =>
        ReadCompanionsAsync(companionPage!.Request with { Page = companionPage.Request.Page + change }, keepSnapshot: true);

    private void SetCompanionsOpen(bool open)
    {
        if (!Set(ref companionsOpen, open, nameof(CompanionsOpen))) return;
        Raise(nameof(ShowProductList));
        if (open) return;
        companionRequest = returnToCollection = null;
        companionSubject = null;
        CompanionMessage = "";
        ShowCompanion(null);
    }

    private void ShowCompanion(CompanionObservation? page,
        IReadOnlyList<RemoteCompanionCollectionRow>? collections = null)
    {
        companionPage = page;
        companionCollections = collections ?? [];
        companionItems = page?.Response.Page?.Items
            .Select(item => new RemoteCompanionItem(item.Title, new(item.RecordType, item.Revision, item.Hash))).ToArray() ?? [];
        SelectedCompanionCollection = null;
        SelectedCompanionItem = null;
        Raise(nameof(CompanionCollections));
        Raise(nameof(ShowCompanionCollections));
        Raise(nameof(CompanionItems));
        Raise(nameof(ShowCompanionItems));
        Raise(nameof(CompanionText));
        Raise(nameof(CompanionFreshness));
        Raise(nameof(ShowCompanionPages));
        NotifyCompanionCommands();
    }

    private async Task ReadCompanionsAsync(CompanionRequest? request, bool keepSnapshot = false)
    {
        if (Selected is not { } estate || !CompanionsOpen) return;
        companionRequest = request;
        AttentionReference? item = companionSubject;
        Busy = true;
        ShowCompanion(null);
        CompanionMessage = "Checking access and source…";
        try
        {
            if (await CheckRuntimeAsync(estate, allowPartial: true).ConfigureAwait(true) is not { } api)
            {
                if (Selected == estate && CompanionsOpen) CompanionReadUnavailable();
                return;
            }

            if (request is null)
            {
                CompanionsResponse catalog = await api.CompanionsAsync().ConfigureAwait(true);
                if (Selected != estate || !CompanionsOpen) return;
                if (catalog is not { Version: 1, Status: "success" }) { CompanionReadUnavailable(); return; }
                RemoteCompanionCollectionRow[] collections = catalog.Generators
                    .Where(generator => !generator.LocalOnly && generator is { Contract: 1, Audience: "Internal" } &&
                        (generator.Pattern is not null || generator.Id is RecordCompanion or ManifestCompanion))
                    .Select(generator => new RemoteCompanionCollectionRow(CollectionName(generator.Id), generator)).ToArray();
                ShowCompanion(null, collections: collections);
                CompanionMessage = collections.Length == 0 ? "No readable collections are available." : "";
                return;
            }

            if (request.Revision is null && !keepSnapshot)
            {
                CompanionsResponse catalog = await api.CompanionsAsync().ConfigureAwait(true);
                if (Selected != estate || !CompanionsOpen) return;
                string? snapshot = request.Generator == ManifestCompanion ? catalog.ManifestSnapshot : catalog.Snapshot;
                if (catalog is not { Version: 1, Status: "success" } || string.IsNullOrEmpty(snapshot))
                {
                    CompanionReadUnavailable();
                    return;
                }
                request = request with { Snapshot = snapshot };
            }

            CompanionResponse response = await api.CompanionRenderAsync(request).ConfigureAwait(true);
            if (Selected != estate || !CompanionsOpen) return;
            if (response is not { Version: 1, Status: "success", Text: not null } ||
                (item is not null && !HasCompanionSource(response, item)))
            {
                CompanionReadUnavailable();
                return;
            }

            var observation = new CompanionObservation(request, response, Now);
            ShowCompanion(observation);
            CompanionMessage = response.Reason ?? "";
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null ||
            exception is NotSupportedException or JsonException or IOException or UnauthorizedAccessException)
        {
            if (Selected == estate && CompanionsOpen) CompanionReadUnavailable();
        }
        finally { Busy = false; }
    }

    private void CompanionReadUnavailable()
    {
        ShowCompanion(null);
        CompanionMessage = CompanionUnavailable;
    }

    private static bool HasCompanionSource(CompanionResponse response, AttentionReference item) =>
        response.Sources.Any(source => source.Kind == "record" && source.RecordType == item.Type &&
            source.Identity == item.Id && source.Hash == item.Hash);

    private static string CollectionName(string id) => id switch
    {
        RecordCompanion => "Estate records",
        ManifestCompanion => "Estate manifest",
        _ => id[(id.LastIndexOf('/') + 1)..].Replace('-', ' ') is { Length: > 0 } title
            ? char.ToUpperInvariant(title[0]) + title[1..] : id,
    };

    private sealed record CompanionObservation(CompanionRequest Request, CompanionResponse Response, string AsOf);
}
