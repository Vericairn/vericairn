# DOCS-48: Which permission bits an export carries and what a restore applies, with a worked example

## docs/operator-guide.md

### In "10. Export, restore, recovery and divergence", in the paragraph beginning "From 0.9.3 (FIX-112)": replace "and an ordinary member as before;" with

> and an ordinary member without it, as the next paragraph shows;

### In "10. Export, restore, recovery and divergence", after the paragraph beginning "From 0.9.3 (FIX-112)" and before the paragraph beginning "FIX-16 judges single records"

> An export carries no other permission bit. Its inventory records `100755` for an executable file and `100644` for every other
> file, records and objects included, so who may read or write a file (its owner, its group or every other account) is not in the
> export. On Linux and macOS a restore creates every file new, under the umask of the shell that runs `estate restore`: an ordinary
> file as `0666` less the umask's bits, an executable file as `0777` less them, and each directory it creates, the destination
> included when it does not exist yet, as `0777` less them. The restored permissions therefore follow the destination's umask, not
> the permissions the files had where the export was made, as a Git checkout's do; `umask` prints the shell's value. For example,
> an estate holds `docs/private.json` at `0600` and `docs/public.json` at `0644`. Both are exported as `100644`, and every file
> restores with identical bytes:
>
> | Restored under | `docs/private.json` | `docs/public.json` | `bootstrap` | `docs/` |
> | --- | --- | --- | --- | --- |
> | `umask 022` | `0644`, `-rw-r--r--` | `0644`, `-rw-r--r--` | `0755`, `-rwxr-xr-x` | `0755`, `drwxr-xr-x` |
> | `umask 077` | `0600`, `-rw-------` | `0600`, `-rw-------` | `0700`, `-rwx------` | `0700`, `drwx------` |
>
> Under `umask 022` the private file becomes readable by every account on the machine; under `umask 077` the public file and
> `bootstrap` become usable by their owner alone. An executable file likewise keeps only its executable mode: one at `0700`
> restores as `0755` under `umask 022`.
>
> When exact permissions matter, such as a file that must stay readable by its owner alone or a group's shared access, keep your
> own mode inventory: each file's path and exact mode, taken when the export is made. Keep it apart from the export and protect it
> as you protect the supplemental capture ([The three inventories of a cold
> recovery](large-artifacts.md#the-three-inventories-of-a-cold-recovery)): the export's inventory does not establish who produced
> it, and a list carried in or beside the archive would establish no more. To use it, restore under a umask at least as strict as
> the strictest mode it lists, such as `(umask 077 && vericairn estate restore --from my-estate.zip --to <empty directory>)`, so
> that no file is readable more widely than intended in the meantime; verify the restore, then apply the listed modes before
> anything else writes to the copy.
>
> On Windows a restore applies no mode: each file and folder it creates takes the access control its parent folder passes on to new
> items, as any file created there does. An export made on Windows records the POSIX `bootstrap` script as `100755` and every other
> file as `100644`, whatever the files' access control lists allow, so no Windows permission travels in an export either.

## docs/limitations-and-platforms.md

### In "Export restore, record publication and recovery", after the paragraph beginning "An export doesn't carry the owner's agent instruction files" (FIX-135's held text replaces that paragraph; this one follows it)

> An export carries no permission beyond the executable mode: its inventory records `100755` for an executable file and `100644`
> for every other file. On Linux and macOS a restore creates files and directories under the umask of the shell that runs it, so a
> file kept at `0600` restores as `0644` under `umask 022`; on Windows it applies no mode, and each file takes the access control
> its folder passes on. Exact permissions need a mode inventory of your own, protected apart from the export and applied after the
> restore; [the operator guide](operator-guide.md#10-export-restore-recovery-and-divergence) shows the umask's effect with a worked
> example.
