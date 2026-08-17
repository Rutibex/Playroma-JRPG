# External Modification Policy

Before saving, Playroma hashes the current disk file and compares it with the
strong fingerprint captured when the document was opened. A mismatch blocks the
save by default.

This protects edits made by RPG Maker MV, a text editor, Git operations, another
Playroma process, synchronization software, or a running development tool.

Playroma does not automatically merge external changes. Supported responses are:

- cancel the save;
- reload and discard Playroma changes;
- save a complete project copy elsewhere;
- explicitly select overwrite policy after presenting a warning.

Automatic three-way merge is deferred to a later collaboration phase.
