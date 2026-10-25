// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>List prompt definitions and their current choices without recording a use.</summary>
    PromptsResponse Prompts(PromptQuery query) => new([], new("unsupported", "This runtime does not offer saved prompts.", []));
    /// <summary>Add, revise or retire an estate prompt.</summary>
    WriteOutcome PromptSave(PromptSaveRequest request) => WriteOutcome.Fail(new("unsupported", "This runtime does not offer saved prompts.", []));
    /// <summary>Favor, hide or disable a prompt with explicit expected preference history.</summary>
    WriteOutcome PromptPreference(PromptPreferenceRequest request) => WriteOutcome.Fail(new("unsupported", "This runtime does not offer saved prompts.", []));
    /// <summary>Copy a built-in into the shared estate library.</summary>
    WriteOutcome PromptCopy(PromptCopyRequest request) => WriteOutcome.Fail(new("unsupported", "This runtime does not offer saved prompts.", []));
    /// <summary>Record a prompt together with the operation that uses it.</summary>
    PromptUseResponse PromptUse(PromptUseRequest request) => new(false, Problem: new("unsupported", "This runtime does not offer saved prompts.", []));

    /// <summary>The cancellable prompt discovery operation.</summary>
    Task<PromptsResponse> PromptsAsync(PromptQuery query, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Prompts), () => Prompts(query), false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable shared prompt write.</summary>
    Task<WriteOutcome> PromptSaveAsync(PromptSaveRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PromptSave), () => PromptSave(request), true, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable preference write.</summary>
    Task<WriteOutcome> PromptPreferenceAsync(PromptPreferenceRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PromptPreference), () => PromptPreference(request), true, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable built-in copy.</summary>
    Task<WriteOutcome> PromptCopyAsync(PromptCopyRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PromptCopy), () => PromptCopy(request), true, this as IClientOperationLifetime, cancellationToken);
    /// <summary>The cancellable recorded use.</summary>
    Task<PromptUseResponse> PromptUseAsync(PromptUseRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PromptUse), () => PromptUse(request), true, this as IClientOperationLifetime, cancellationToken);
}
