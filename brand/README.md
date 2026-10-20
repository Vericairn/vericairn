# Brand assets

The Vericairn identity as adopted by the operator's decision of October 14, 2026 and described in the estate's `design/brand.md` (revision 2): a plain sans-serif wordmark and an abstract cairn mark, four stacked, offset stones drawn as geometry.

| File | What it is |
|---|---|
| `cairn-mark.svg` | The mark alone, 64 by 64 units, `currentColor`, so the same file renders in monochrome light and dark |
| `package-icon.png` | The package icon, 128 by 128 pixels, the cairn in the dark palette |
| `lockup-horizontal.svg` | Mark and wordmark side by side |
| `lockup-stacked.svg` | Mark above the wordmark |
| `wordmark.svg` | The wordmark alone |

The wordmark is set in Inter (semi-bold, letter-spacing 0.5) at render time; an outlined version, with the glyphs converted to paths, follows when a font-to-path step exists in the build. Editions are text labels beside the wordmark, never separate marks. The mark never carries a checkmark and is never rendered as a "verified" badge: a signature attests and never certifies. Rejected motifs, so they are not proposed again: mountains and summits, shields and badges, compasses and arrows, network graphs, chat bubbles.

## Package icon provenance

`package-icon.png` is rendered from the four rounded rectangles in `cairn-mark.svg`, at 128 by 128 pixels, with the mark in #EAEAEA on an opaque #1E1E1E background. Regenerate it from the repository root with:

```sh
python3 brand/render-package-icon.py
```

The renderer uses only the Python 3 standard library, works offline and samples each pixel on an 8 by 8 grid for smooth edges. It writes a single RGB PNG with no timestamp or other metadata; a stored DEFLATE block keeps its bytes independent of compression-library versions. The mark's rights below also apply to this rendering.

## Rights

The name Vericairn, the wordmark and the mark are Jason Furr's. These files are first-party, created for Vericairn by its builder from no third-party material. The product's Apache License 2.0 covers the software; it grants no right to use the name, the wordmark or the mark as a mark for other software or services (the license's trademark clause). Inter and JetBrains Mono, used by Workbench, are licensed under the SIL Open Font License 1.1; their license texts travel with the font files under `src/Vericairn.Workbench/Assets/Fonts/` and in `THIRD-PARTY-NOTICES.md`.
