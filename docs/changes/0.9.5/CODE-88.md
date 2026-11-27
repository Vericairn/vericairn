# CODE-88: Acquiring a repository runs nothing

## docs/operator-guide.md

### In "7. Repositories and checks as evidence", after the paragraph beginning "The overrides also apply"

> From 0.9.5 (CODE-88), source inspection, adoption, observation and clone registration use a temporary Git view that Vericairn owns. These commands report the SHA-256 of the exact configuration and the names of settings they ignored. They accept `--boundary <directory>` when Git metadata legitimately lives elsewhere under an operator-selected directory. Without that option the repository root is the boundary. Source reads name and refuse an outside Git redirection or a working-tree entry they cannot copy as a bounded regular file.
>
> A worker acquires the named base commit into its own repository without submodules or repository-defined programs. If it finds any Git settings outside the allow-list, a human operator must sign a passing `review` of the exact repository resource revision. Its `repositoryConfiguration` object contains `config` with the reported SHA-256 and, when present, `worktreeConfig` with the reported worktree configuration hash. An edit to either configuration file requires review of the new bytes. The review clears workspace preparation. Ignored settings stay ignored during acquisition.

## docs/limitations-and-platforms.md

### In "Reading repositories Vericairn did not create", after the section's description of isolated Git reads

> From 0.9.5 (CODE-88), source observations use private copies of Git metadata and regular working-tree files. Working-tree copies are limited to 64 MiB per file, 512 MiB total, 100,000 entries and 30 seconds. Symbolic links, files with multiple hard links, special files and unreadable entries are refused by name. Gitlinks are skipped. Observed remote addresses are limited to HTTPS, SSH and local paths under the selected boundary.
