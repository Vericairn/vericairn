# Per-change fragments for the held guides

Revision 3, November 20, 2026. Say which paths a fragment may not name and that a home-relative path naming no user and a root named alone are not machine paths (OPS-17 re-check).

Revision 2, November 20, 2026. Say that nothing comes between the title and the first section and that every line of placed text is quoted (OPS-17 review).

Revision 1, November 20, 2026. Define the fragment layout under `docs/changes/<version>/`, through which a change hands its text for a held guide to the documentation-set change of its release (OPS-17).

From 0.9.4, four guides of this repository are held: each release's documentation-set change owns them, and other changes leave
them alone. They are the compatibility notes of the release in preparation (`docs/api-compatibility-<version>.md`),
`docs/operator-guide.md`, `docs/limitations-and-platforms.md` and `docs/api-v1.md`, the most-changed guides whose content the
product's tests do not check. Concurrent changes no longer collide on them: a change writes what it needs in a held guide as a
fragment here, and the documentation-set change applies every fragment of its release at once. Until then the held guides on the
main line describe the last release. An estate can hold this with a `vericairn.estate/concurrent-paths` policy over the held
guides ([Estate policies](../policies.md#concurrent-paths-094)), so that two assignments granted a held guide at once are refused
until one runs after the other or excludes it.

## Layout

A fragment is one Markdown file per change at `docs/changes/<version>/<KEY>.md`, where `<version>` is the release in preparation,
such as `0.9.4`, and `<KEY>` is the change's work-order key, such as `POLICY-08`. Nothing else lives in a version's directory, and
this file is the only file directly under `docs/changes/`.

- The first line is the title, `# <KEY>` followed by a colon and a short description. Nothing but blank lines comes between the
  title and the first section.
- Each section is a level-two heading that names a held guide by its repository path, `## docs/operator-guide.md`,
  `## docs/limitations-and-platforms.md`, `## docs/api-v1.md` or `## docs/api-compatibility-<version>.md` for the fragment's own
  version, each at most once, in that order.
- Under a guide, each placement is a level-three heading that says where the text goes, such as
  `### In "18. Routing, asks and the worker", after the paragraph beginning "goal step takes"`, followed by the text to insert as a
  block quote, every line of it beginning with `>`, in the guide's own style ("From 0.9.4, ..." for a changed behavior).
- A fragment states only what its own change adds, and names no machine path: no path that names one machine's user home, volume
  or temporary folder, in plain text, code, emphasis, a link, a table or a file URL. A machine path is a name below `/Users`, `/home`
  or `/root`, a tilde followed by a user's name and a slash (`~name/`), a name below `/Volumes`, `/mnt` or `/media`, any path on a
  drive letter (`C:\` or `C:/`), a network share (`\\server\share`), a name below `/tmp`, `/var/tmp` or `/var/folders`, each of
  these three also below `/private`, or a file URL naming a host (`file://server/`). It begins where a path begins, at the start of a
  line or after any character but a letter, a digit, `.`, `-`, `~` or `}`, so that a relative path such as `docs/home/guide.md` or
  one below a variable such as `$TMPDIR/vericairn` is not one. A home-relative path that names no user, such as
  `~/.config/systemd/user`, `$HOME/.config` or `%USERPROFILE%\AppData`, and a root named alone, such as `/tmp` or `/Users`, are not
  machine paths.

```markdown
# POLICY-08: The concurrent-paths policy kind holds an unresolved overlap

## docs/operator-guide.md

### In "18. Routing, asks and the worker", after the paragraph beginning "From 0.9.4 (CODE-73), routing names the repository"

> From 0.9.4 (POLICY-08), an estate holds an unresolved overlap with a `vericairn.estate/concurrent-paths` policy ...
```

## Who writes and who applies

A change that needs text in a held guide excludes that guide from its brief and writes its fragment in its own paths, so that its
assignment and the documentation-set assignment are disjoint. A change whose own tests assert text in a held guide, as
`tests/Vericairn.Cli.Tests/EnrollmentTroubleshootingGuideTests.cs` asserts the operator guide's links, keeps that guide in its
brief, changes it directly, and runs in sequence with any other change that does.

The documentation-set change of a release applies every fragment under `docs/changes/<version>/` to the held guides and removes the
fragments in the same commit, before the release's documentation set is frozen; a version whose set is frozen has no fragments left,
which the product's tests check. Frozen sets under `docs/releases/<version>/` are never edited.

Guides that the product's tests assert throughout, such as `docs/cli.md`, the generated references under `docs/reference/` and the
API change files under `.github/api-compatibility/` change with each change, as before; their overlaps are reported, not held.
