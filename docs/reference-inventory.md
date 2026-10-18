# Generated reference inventory

Revision 1, October 18, 2026. Define the retained reference inventory used by document checks and reference-coverage policies.

`vericairn.reference-inventory/1` accompanies the generated CLI, record and API references as `reference-inventory.json`. Its exact build `commit` is a full lowercase Git commit. The `references` array binds each category to its generated file and SHA-256 byte hash: `command` to `cli-reference.md`, `record-type` to `record-reference.md`, and `api-operation` to `api-reference.md`. Its `items` array contains one `{category, name}` per public reference identity, in ordinal category/name order.

Commands use the full canonical invocation, such as `vericairn release submit`, without arguments or flags. Record types use serialized names such as `release`. API operations use an uppercase method, one space and the exact route template, such as `GET /v1/records/{id}`. A command family does not cover its children. Aliases enter only if the generator declares them public. All comparisons are ordinal and case-sensitive.

The build generator supplies the item lists from the command tree, record-type registry and OpenAPI document it uses to write the references. Generation verifies that those identities exactly match the generated headings. Inventory validation rejects unknown fields or formats, duplicate JSON fields, duplicate identities, missing categories, unsafe or conflicting paths, different commits, unavailable bytes, hash mismatches and differences between the item set and reference headings. Fenced examples cannot supply headings. Evaluation reads the retained snapshot supplied by its caller and starts no build, process or network request.

The bounds are 100,000 items, a 32 MiB inventory, a 64 MiB combined generated-reference set and JSON depth 16. Exhaustion is unavailable, with no partial coverage claim. A source adapter supplies normalized relative paths, byte hashes, availability and the exact source commit through the existing policy repository contract.

The documentation manifest names the inventory through `referenceInventory`. Build comparison must check it against the build commit and public item lists. Document checking must verify its commit against the documentation manifest, its hashes and its exact item set. Sets documenting record format 3 or version 0.8.0 onward require the inventory declaration. Earlier frozen sets keep their original contract and bytes. `ReferenceInventoryFiles` is the internal build/document adapter; the evaluator consumes only retained snapshots.
