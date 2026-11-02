// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    private RemotePolicyDetail? policyDetail;
    private bool policyCurrent;
    private int policyEpoch;
    private string policyMessage = string.Empty;

    /// <summary>The original policy notice and its independently dated inspection.</summary>
    public RemotePolicyDetail? PolicyDetail => policyDetail;
    /// <summary>Policy inspection replaces the home areas without opening a phone decision.</summary>
    public bool HasPolicyDetail => ShowHome && policyDetail is not null;
    /// <summary>Whether the inspection was refreshed, refused or retained as cached context.</summary>
    public string PolicyMessage { get => policyMessage; private set => Set(ref policyMessage, value); }
    /// <summary>The inspection's own age; home refresh never renews it.</summary>
    public string PolicyFreshness => policyDetail is null ? "not observed" :
        $"{(policyCurrent && Selected?.Current == true ? "observed" : "cached")} {policyDetail.AsOf}; {RemoteFreshness.Age(policyDetail.AsOf, clock)}";
    /// <summary>Runtime-reported policy actions, with no transferable authority or phone action.</summary>
    public string PolicyAuthority => policyDetail is null ? "" : $"{PolicyFreshness}\n" +
        (policyDetail.Authority is null ? "Authority unavailable." : "Policy actions observed for this principal: " +
            (policyDetail.Authority.Count == 0 ? "none" : string.Join(", ", policyDetail.Authority)) + ". Exact bounds and current authority must be rechecked on the desktop.");
    /// <summary>The complete existing handoff rendering, or its refusal; preparing is distinct from opening or acting.</summary>
    public string PolicyHandoff => policyDetail?.Handoff is not { } handoff ? "No desktop package observed." :
        (handoff.Problem is { } problem ? "Handoff unavailable: " + problem.Message : handoff.Package is { } package ?
            $"Desktop context {package.Hash}; {package.Bytes} bytes. {PolicyFreshness}. No desktop opening or downstream effect observed." : "Handoff unavailable.") +
        "\n" + handoff.Text;
    /// <summary>The selected estate and runtime remain explicit in the desktop context.</summary>
    public string PolicyDestination => Selected is null ? "" :
        $"Estate {Selected.Connection.Estate}; runtime {RuntimeIdentityScheme.Fingerprint(Selected.Connection.Identity)}; principal {Selected.Connection.Principal}.";
    /// <summary>Re-reads access and the policy, preserving the original selection.</summary>
    public AsyncCommand RefreshPolicyCommand { get; private set; } = null!;
    /// <summary>Reads the existing handoff route after refreshing access and basis; authors and approves nothing.</summary>
    public AsyncCommand PolicyHandoffCommand { get; private set; } = null!;
    /// <summary>Returns to attention without sending a request.</summary>
    public AsyncCommand ClosePolicyCommand { get; private set; } = null!;

    private void InitializePolicyCommands()
    {
        RefreshPolicyCommand = new(() => ReadPolicyAsync(policyDetail!.Notice, false), () => !Busy && policyDetail is not null && Selected?.Current == true);
        PolicyHandoffCommand = new(() => ReadPolicyAsync(policyDetail!.Notice, true), () => !Busy && policyDetail is not null && Selected?.Current == true);
        ClosePolicyCommand = new(() => { SetPolicyDetail(null); return Task.CompletedTask; }, () => !Busy && policyDetail is not null);
    }

    private void NotifyPolicyCommands()
    {
        RefreshPolicyCommand?.NotifyCanExecuteChanged(); PolicyHandoffCommand?.NotifyCanExecuteChanged(); ClosePolicyCommand?.NotifyCanExecuteChanged();
    }

    private void SetPolicyDetail(RemotePolicyDetail? detail)
    {
        policyEpoch++;
        policyDetail = detail;
        policyCurrent = false;
        PolicyMessage = string.Empty;
        NotifyPolicy();
    }

    private void NotifyPolicy()
    {
        Raise(nameof(PolicyDetail)); Raise(nameof(HasPolicyDetail)); Raise(nameof(ShowAreas));
        Raise(nameof(PolicyFreshness)); Raise(nameof(PolicyAuthority)); Raise(nameof(PolicyHandoff)); Raise(nameof(PolicyDestination));
        NotifyPolicyCommands();
    }

    private async Task OpenPolicyAsync(AttentionNotice notice)
    {
        if (Selected?.Observation is not { } observation || notice.Policy is null) return;
        // A new revision of a notice cannot borrow an earlier notice's inspected material.
        RemotePolicyDetail? cached = observation.PolicyDetails.GetValueOrDefault(notice.Revision);
        SetDecision(null);
        SetPolicyDetail(cached ?? new(notice, observation.AsOf, null, null));
        if (Selected.Current) await ReadPolicyAsync(notice, false).ConfigureAwait(true);
        else PolicyMessage = "Cached condition. Nothing was sent. Reconnect and refresh before preparing desktop context.";
    }

    private async Task ReadPolicyAsync(AttentionNotice notice, bool handoff)
    {
        if (Selected is not { Current: true, Client: { } api } estate || notice.Policy is not { } source) return;
        Busy = true;
        policyCurrent = false;
        int epoch = policyEpoch;
        NotifyPolicy();
        try
        {
            ConnectionStatus connection = await connections.ProbeAsync(estate.Connection, CancellationToken.None).ConfigureAwait(true);
            estate.Connection = connection.Connection;
            if (Selected != estate || epoch != policyEpoch) return;
            if (connection.State is not (ConnectionStates.Current or ConnectionStates.Partial))
            {
                estate.Current = false;
                Show(estate);
                ShowNode(estate, connection.State, connection.Message);
                PolicyMessage = "Current access is unavailable. Retained policy context is cached; nothing was submitted.";
                return;
            }
            string observedAt = Now;
            IReadOnlyList<string> authority = await api.PolicyAccessAsync(estate.Connection.Principal).ConfigureAwait(true);
            PolicyResponse? policy = await api.PolicyAsync(source.Finding.Policy.Id).ConfigureAwait(true);
            HandoffResponse? package = null;
            string message = "Policy and authority observed. Review requires the desktop; nothing was approved or written.";
            if (policy is null) message = "The selected policy is unavailable. The original condition remains visible; no empty history or approval is inferred.";
            else if (handoff)
            {
                package = await api.HandoffAsync().ConfigureAwait(true);
                PolicyResponse? checkedPolicy = await api.PolicyAsync(source.Finding.Policy.Id).ConfigureAwait(true);
                IReadOnlyList<string> checkedAuthority = await api.PolicyAccessAsync(estate.Connection.Principal).ConfigureAwait(true);
                if (checkedPolicy?.Basis.Digest != policy.Basis.Digest || !authority.SequenceEqual(checkedAuthority))
                {
                    package = null;
                    message = "The basis or authority changed while preparing context. Refresh before preparing another desktop handoff; the original selection is retained.";
                }
                else message = package is { Package: not null, Problem: null }
                    ? "Desktop handoff prepared for the exact original selection below. No desktop opening, approval or write was observed."
                    : "The runtime could not prepare a complete handoff. Its refusal remains visible; inspect the exact subject on the desktop.";
                policy = checkedPolicy;
                authority = checkedAuthority;
            }
            if (Selected != estate || epoch != policyEpoch) return;
            policyDetail = new(notice, observedAt, policy, authority, package);
            policyCurrent = true;
            if (policy is not null && policy.Basis.Digest != source.Basis.Digest)
                message += " The basis changed since this notice. Compare the original condition with the refreshed history; the subject was not replaced.";
            PolicyMessage = message;
            if (estate.Observation is { } observation)
            {
                var details = new Dictionary<string, RemotePolicyDetail>(observation.PolicyDetails, StringComparer.Ordinal) { [notice.Revision] = policyDetail };
                estate.Observation = observation with { PolicyDetails = details };
                Keep(estate);
            }
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            estate.Current = false;
            if (Selected == estate && epoch == policyEpoch)
            {
                Show(estate);
                PolicyMessage = "Current policy, authority or handoff material is unavailable. Retained context is cached. Nothing was approved, written or queued.";
            }
        }
        finally { Busy = false; NotifyPolicy(); }
    }
}
