// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Providers.GitHub;

/// <summary>
/// From 0.9.4 (WATCH-06): the first progress-target adapter: one GitHub discussion per target, in the repository and category its
/// settings name, through bounded gh processes using the operator's own login. It finds the discussion by the marker the runtime ends
/// every text with, creates it once, replaces its text and adds comments. It never reads or stores a credential, keeps no diagnostic
/// output, and passes on nothing of GitHub's text but the discussion's own current text, which the runtime compares with its own.
/// </summary>
public sealed class GitHubDiscussionsTarget : IProgressTargetAdapter
{
    /// <summary>The adapter id a progress target's configuration names.</summary>
    public const string AdapterId = "github-discussions";

    /// <summary>The most characters a discussion's text or a comment holds on GitHub; a longer one is refused before it is sent.</summary>
    internal const int MaxBody = 65_536;

    /// <summary>Discussions read in one page while finding a target's place, as the page query asks.</summary>
    internal const int PageSize = 50;

    /// <summary>The most pages one search reads: 1,000 discussions of the category, beyond which the search is refused rather than incomplete.</summary>
    internal const int MaxPages = 20;

    // The repository, whether anyone may read it, whether its Discussions are on, and its categories.
    private const string RepositoryQuery = "query($owner:String!,$name:String!){repository(owner:$owner,name:$name)"
        + "{id nameWithOwner visibility hasDiscussionsEnabled discussionCategories(first:100){nodes{id name}}}}";

    // A known discussion, by its node identity, with where it is.
    private const string NodeQuery = "query($id:ID!){node(id:$id){...on Discussion{id url body viewerDidAuthor repository{nameWithOwner} category{id}}}}";

    // A page of the category's discussions, oldest first.
    private const string PageQuery = "query($owner:String!,$name:String!,$category:ID!,$after:String){repository(owner:$owner,name:$name)"
        + "{discussions(first:50,after:$after,categoryId:$category,orderBy:{field:CREATED_AT,direction:ASC}){pageInfo{hasNextPage endCursor}nodes{id url body viewerDidAuthor}}}}";

    private const string CreateMutation = "mutation($repository:ID!,$category:ID!,$title:String!,$body:String!){createDiscussion(input:"
        + "{repositoryId:$repository,categoryId:$category,title:$title,body:$body}){discussion{id url}}}";

    private const string ReplaceMutation = "mutation($id:ID!,$body:String!){updateDiscussion(input:{discussionId:$id,body:$body}){discussion{id url}}}";

    private const string CommentMutation = "mutation($id:ID!,$body:String!){addDiscussionComment(input:{discussionId:$id,body:$body}){comment{url}}}";

    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each gh invocation has a one-minute deadline and bounded output.</summary>
    public GitHubDiscussionsTarget() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(1))) { }

    internal GitHubDiscussionsTarget(Func<ProcessStartInfo, ProcessResult> run) => this.run = run;

    /// <inheritdoc />
    public string Adapter => AdapterId;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("github-discussions-gh", IProgressTargetAdapter.Contract, "implemented",
        ["locate", "create", "replace", "comment"],
        ["Places the text with the operator's own gh login, which must be able to create discussions in the category; Vericairn never sees or stores the token.",
         "One discussion per target, found by the marker its text ends with among at most 1,000 discussions of the category; the operator pins it.",
         "A text or comment longer than 65,536 characters is refused before anything is sent, and each is handed to gh in an owner-only temporary file deleted at once.",
         "A repository GitHub reports public or internal is readable beyond its members, so its target takes the public form."],
        [new ProviderSetting("repository", "The GitHub repository the discussion is in, as owner/name.", true),
         new ProviderSetting("category", "The discussion category, by its name; one where only maintainers post, such as Announcements, keeps the post the estate's.", true, "Announcements")],
        Destination: "repository", Locations: ["github"]);

    /// <inheritdoc />
    public ProgressTargetDestination Locate(ProgressTargetSpec target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Repository repository = Observe(target);
        if (target.Known is string known && Node(known) is { } id)
        {
            // The known discussion stands only while it is this login's, in the repository and category the settings name; one deleted,
            // moved or unreadable is looked for again by its marker.
            try
            {
                using JsonDocument document = Read(Graph(NodeQuery, ("id", id)));
                JsonElement node = Data(document).GetProperty("node");
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("id", out _)
                    && string.Equals(Text(node.GetProperty("repository"), "nameWithOwner"), repository.Name, StringComparison.OrdinalIgnoreCase)
                    && Text(node.GetProperty("category"), "id") == repository.Category && node.GetProperty("viewerDidAuthor").GetBoolean())
                {
                    return new(repository.Private, Place(node));
                }
            }
            catch (Exception exception) when (exception is ProviderRefusedException or InvalidOperationException or KeyNotFoundException)
            {
            }
        }

        // The place is the discussion in the category whose text ends with the target's marker, written by this login.
        string marker = "<!-- " + target.Marker + " -->";
        string? after = null;
        for (int page = 1; ; page++)
        {
            if (page > MaxPages) throw new ProviderRefusedException($"The category holds more than {MaxPages * PageSize} discussions, more than one search reads, so the target's place cannot be found completely; nothing was sent.");
            (string, string)[] variables = after is null ? [("owner", repository.Owner), ("name", repository.Short), ("category", repository.Category)]
                : [("owner", repository.Owner), ("name", repository.Short), ("category", repository.Category), ("after", after)];
            using JsonDocument document = Read(Graph(PageQuery, variables));
            try
            {
                JsonElement discussions = Data(document).GetProperty("repository").GetProperty("discussions");
                foreach (JsonElement node in discussions.GetProperty("nodes").EnumerateArray())
                {
                    if (node.GetProperty("viewerDidAuthor").GetBoolean() && Text(node, "body").Contains(marker, StringComparison.Ordinal)) return new(repository.Private, Place(node));
                }

                JsonElement info = discussions.GetProperty("pageInfo");
                if (!info.GetProperty("hasNextPage").GetBoolean()) return new(repository.Private, null);
                after = Text(info, "endCursor");
                if (after.Length is 0 or > 200) throw new InvalidOperationException("The page cursor is unreadable.");
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException)
            {
                throw new ProviderRefusedException("GitHub's list of the category's discussions was incomplete; nothing was sent.");
            }
        }
    }

    /// <inheritdoc />
    public ProgressTargetPlace Create(ProgressTargetSpec target, string body)
    {
        ArgumentNullException.ThrowIfNull(target);
        Bounded(body);
        if (target.Title.Trim().Length == 0 || target.Title.Length > 256) throw new ProviderRefusedException("A discussion's title is one line of at most 256 characters; nothing was sent.");
        Repository repository = Observe(target);
        using JsonDocument document = Effect(Graph(CreateMutation, ("repository", repository.Id), ("category", repository.Category), ("title", target.Title)), body);
        return Receipt(document, "createDiscussion", "discussion", "The discussion was created but its receipt was unreadable.");
    }

    /// <inheritdoc />
    public ProgressTargetPlace Replace(ProgressTargetSpec target, ProgressTargetPlace place, string body)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(place);
        Bounded(body);
        string id = Node(place.Id) ?? throw new ProviderRefusedException("The discussion's node identity is unreadable; nothing was sent.");
        using JsonDocument document = Effect(Graph(ReplaceMutation, ("id", id)), body);
        return Receipt(document, "updateDiscussion", "discussion", "The text was replaced but its receipt was unreadable.");
    }

    /// <inheritdoc />
    public string Comment(ProgressTargetSpec target, ProgressTargetPlace place, string body)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(place);
        Bounded(body);
        string id = Node(place.Id) ?? throw new ProviderRefusedException("The discussion's node identity is unreadable; nothing was sent.");
        using JsonDocument document = Effect(Graph(CommentMutation, ("id", id)), body);
        try
        {
            string url = Text(Data(document).GetProperty("addDiscussionComment").GetProperty("comment"), "url");
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? address) && address.Scheme == Uri.UriSchemeHttps ? url : throw new InvalidOperationException("The address is unreadable.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            throw new ProviderUncertainException("The comment was added but its receipt was unreadable.");
        }
    }

    // The repository and category the settings name, as GitHub reports them: refused when either is missing or Discussions are off.
    private Repository Observe(ProgressTargetSpec target)
    {
        string repository = target.Settings.GetValueOrDefault("repository") ?? string.Empty;
        if (!Patterns.GitHubRepository.IsMatch(repository) || repository.Split('/')[1] is "." or "..")
            throw new ProviderRefusedException("The repository setting must be a GitHub owner/name; nothing was sent.");
        string category = target.Settings.GetValueOrDefault("category") ?? string.Empty;
        if (category.Trim().Length == 0 || category.Length > 100) throw new ProviderRefusedException("The category setting must name a discussion category; nothing was sent.");
        string[] parts = repository.Split('/');
        using JsonDocument document = Read(Graph(RepositoryQuery, ("owner", parts[0]), ("name", parts[1])));
        try
        {
            JsonElement node = Data(document).GetProperty("repository");
            if (node.ValueKind != JsonValueKind.Object) throw new ProviderRefusedException($"{repository} is not a repository the gh login can read; nothing was sent.");
            string name = Text(node, "nameWithOwner");
            if (!string.Equals(name, repository, StringComparison.OrdinalIgnoreCase))
                throw new ProviderRefusedException($"GitHub answered for {repository} under another name; it may have moved. Correct the target's repository setting; nothing was sent.");
            if (!node.GetProperty("hasDiscussionsEnabled").GetBoolean()) throw new ProviderRefusedException($"{repository} has Discussions turned off; turn them on in its settings; nothing was sent.");
            List<(string Id, string Name)> categories = [.. node.GetProperty("discussionCategories").GetProperty("nodes").EnumerateArray().Select(c => (Text(c, "id"), Text(c, "name")))];
            List<(string Id, string Name)> named = [.. categories.Where(c => c.Name == category)];
            if (named.Count == 0) named = [.. categories.Where(c => string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase))];
            if (named.Count != 1 || Node(named[0].Id) is null) throw new ProviderRefusedException($"{repository} has no one discussion category named '{category}'; nothing was sent.");
            string id = Node(Text(node, "id")) ?? throw new InvalidOperationException("The repository's identity is unreadable.");
            return new Repository(id, name, parts[0], parts[1], named[0].Id, Text(node, "visibility") == "PRIVATE");
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new ProviderRefusedException("GitHub's answer about the repository was incomplete; nothing was sent.");
        }
    }

    private static ProgressTargetPlace Place(JsonElement node)
    {
        string id = Node(Text(node, "id")) ?? throw new InvalidOperationException("A discussion's identity is unreadable.");
        string url = Text(node, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address) || address.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("A discussion's address is unreadable.");
        return new ProgressTargetPlace(id, url, Text(node, "body"));
    }

    private static ProgressTargetPlace Receipt(JsonDocument document, string mutation, string field, string unreadable)
    {
        try
        {
            return Place(Data(document).GetProperty(mutation).GetProperty(field)) with { Body = null };
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            throw new ProviderUncertainException(unreadable);
        }
    }

    private static void Bounded(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length > MaxBody) throw new ProviderRefusedException($"The text is longer than GitHub keeps in a discussion ({MaxBody} characters); nothing was sent.");
    }

    private static JsonElement Data(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("GitHub reported errors.");
        return document.RootElement.GetProperty("data");
    }

    private static string[] Graph(string query, params (string Name, string Value)[] variables) =>
        ["api", "graphql", "-f", "query=" + query, .. variables.SelectMany(v => new[] { "-f", v.Name + "=" + v.Value })];

    private JsonDocument Read(string[] arguments)
    {
        ProcessResult result = Invoke(arguments);
        if (!result.Started) throw new ProviderRefusedException("The GitHub CLI could not be started; check that gh is installed; nothing was sent.");
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderRefusedException("GitHub could not be read completely; check gh's login (gh auth status) and the repository's access; nothing was sent.");
        try
        {
            JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            try
            {
                _ = Data(document);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ProviderRefusedException("GitHub returned an unreadable answer; nothing was sent.");
        }
    }

    // A mutation whose text goes to gh in an owner-only file in a fresh temporary directory, deleted once gh has answered: a command
    // line cannot carry a long text on every platform.
    private JsonDocument Effect(string[] arguments, string body)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("vericairn-progress-");
        try
        {
            string file = Path.Combine(directory.FullName, "body.md");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(file, options)) stream.Write(Encoding.UTF8.GetBytes(body));
            ProcessResult result = Invoke([.. arguments, "-F", "body=@" + file]);
            if (!result.Started) throw new ProviderRefusedException("The GitHub CLI could not be started; check that gh is installed; nothing was sent.");
            if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
                throw new ProviderUncertainException("The GitHub request may have taken effect; observe it before any retry.");
            try
            {
                return JsonDocument.Parse(result.StandardOutput);
            }
            catch (JsonException)
            {
                throw new ProviderUncertainException("The GitHub request was answered unreadably and may have taken effect; observe it before any retry.");
            }
        }
        finally
        {
            try { directory.Delete(recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private ProcessResult Invoke(string[] arguments)
    {
        var info = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        return run(info);
    }

    // A GraphQL node identity, which goes back to GitHub only in this closed form.
    private static string? Node(string id) => id.Length is > 0 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '=') ? id : null;

    private static string Text(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private sealed record Repository(string Id, string Name, string Owner, string Short, string Category, bool Private);
}
