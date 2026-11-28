# READ-05: Readable companions on Remote

## docs/operator-guide.md

### In "13. Workbench, the desktop", after the paragraph beginning "The Documents page (Ctrl+Shift+D)"

> From 0.9.5 (READ-05), open **Readable companions** in Remote's **Products** area. Browse a collection at its exact snapshot
> and select an item to read its exact revision. A cached page shows its original observation time and age. When complete
> material needs the desktop, choose **Continue in Workbench** and prepare a task for the exact permitted subject. Open it
> through **From Remote** on the paired runtime. See the [Remote guide](remote.md).

## docs/limitations-and-platforms.md

### In "Readable companions and build provenance", after the paragraph beginning "The generators cover admitted record items and collections"

> From 0.9.5 (READ-05), Remote uses the authorized Internal companion view over the SDK. It keeps one collection page and
> one item page in memory for the selected runtime. A source-only read must confirm current access and the same source and
> generator basis before Remote reuses either page. When disconnected, the reader reports unavailability. Workbench checks
> access again when the person opens a task received from the phone. Local companion inspectors stay on their owning surfaces.
> Headless Remote reader tests, mobile head builds and real-device observations are separate qualification evidence.

## docs/api-v1.md

### In "Operations", after the paragraph beginning "From 0.8.0, `docs regenerate --compare`"

> From 0.9.5 (READ-05), `GET /v1/companions/render` includes optional `page` metadata for renditions whose generator reports
> page boundaries. `number` is the one-based page, `total` is the number of pages and `items` lists the permitted record
> rows rendered on that page in the same order. Each row gives `revision`, `hash`, `recordType` and `title`. A sources-only
> response omits `page`.

## docs/api-compatibility-0.9.5.md

### With the additive 0.9.5 companion response fields

> READ-05 adds the optional `CompanionResponse.Page` property to expose paging and exact item navigation for supported
> renditions. Clients can use `CompanionPage` and `CompanionPageItem` when the runtime supplies them.
