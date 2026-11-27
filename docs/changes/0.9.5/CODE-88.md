# CODE-88: Acquiring a repository runs nothing

## docs/operator-guide.md

### In "7. Repositories and checks as evidence", after the paragraph beginning "The overrides also apply"

> From 0.9.5 (CODE-88), source inspection, adoption, observation and clone registration use a temporary Git view that Vericairn owns. These commands report the SHA-256 of the exact configuration and the names of settings they ignored, with subsection names masked because they can contain credentials. They accept `--boundary <directory>` when Git metadata legitimately lives elsewhere under an operator-selected directory. Without that option the repository root is the boundary. A registered clone has to remain readable within its own root because later reads use that boundary. Source reads name and refuse an outside Git redirection, an unsafe index path or a working-tree entry they cannot copy as a bounded regular file.
>
> A worker acquires the named base commit into its own repository without submodules or repository-defined programs. If it finds any Git settings outside the allow-list, including an ordinary clone's upstream settings, a human operator must sign a passing `review` of the exact repository resource revision. Run `vericairn records new review --by <operator-id> --subject resource:<resource-id>:<resource-hash> --body <review.json>` with the operator's admitted device. Set `outcome` to `pass` and include `independence`, `summary` and `findings` as for other reviews. Set `repositoryConfiguration.config` to the reported SHA-256 and, when present, `repositoryConfiguration.worktreeConfig` to the reported worktree configuration hash. An edit to either configuration file requires review of the new bytes. Ignored settings stay ignored during acquisition.
>
> Before upgrading from an earlier release, retain any running worker's work through its existing runtime. A workspace with an alternate object store is refused for reuse or retention under 0.9.5 because the alternate can point outside the workspace. Preserve existing commits and files and prepare a new independent workspace for a later run.

## docs/limitations-and-platforms.md

### In "Reading repositories Vericairn did not create", after the section's description of isolated Git reads

> From 0.9.5 (CODE-88), source observations use private copies of Git metadata and regular working-tree files. Working-tree copies are limited to 64 MiB per file, 512 MiB total, 100,000 entries and 30 seconds. Ignored and untracked entries count toward these limits. A symbolic link, hard-linked file, nested repository, special file, unreadable entry or exceeded limit refuses the observation as a whole and identifies the entry. Gitlink directories are skipped. Make an independent clone with `git clone --no-hardlinks` when a local clone shares Git object files through hard links. Git metadata copies are limited to 4 GiB and 100,000 entries per copied store, with a 30-second acquisition deadline checked between file operations. Observed remote addresses are limited to HTTPS, SSH and local paths under the selected boundary. Retention uses the files reference format and does not create a reflog entry for its guarded local ref write.
