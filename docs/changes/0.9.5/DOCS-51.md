# DOCS-51: Draft, approve and restore a service appointment

## docs/operator-guide.md

### In "Runs of a flow (0.9.2)", after the paragraph beginning "goal show, the API's goal read"

> From 0.9.5 (DOCS-51), the [starter flow's worked example](templates.md#the-starter-flow-094) records a service request in a
> prose-scoped work order and supplies the scheduler's permitted pattern separately with `goal propose --flow-path`. The person's
> approval pins the scheduler's draft by hash. A revision makes that approval stale and the next step asks the person again.
> The example inspects the revised approval and restores the run's position under a second Vericairn home.
