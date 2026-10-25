# Development record reference

[record-reference.md](record-reference.md) is a generated development reference
for the record contracts affected by templates and reserved package versions:
`living-document`, `document-review`, `document-revision`, `result`,
`template-defaults`, `product`, `observation` and `operation`. It retains every
field of each selected type, including fields that predate these additions.

The build's registered type descriptions supply the canonical selected input in
[inputs/record-types.json](inputs/record-types.json). The standard
`vericairn.software/record-reference` companion renders that input. Its generation
basis records the exact source commit, renderer implementation, input digest and
input byte hash. The input's version is the base build's version; this is a
working reference for the 0.8.4 development additions, not an issued release set.
Frozen references continue to describe their own published builds.

To regenerate, build the committed source, generate references with the existing
documentation tooling into ignored build output, select these eight complete type
entries from its `inputs/record-types.json`, and render that canonical selection
through the same record-reference companion. Retain the selected input beside
the generated reference. Do not hand-edit the generated Markdown or modify a
frozen documentation set. The [template guide](../templates.md) and
[package procedure](../operator-actions.md#package-publication-unlisting-and-deprecation)
explain the contracts and workflows; [record format 3](../record-format-v3.md#template-instances-and-section-evidence)
explains the additive template evidence fields.
