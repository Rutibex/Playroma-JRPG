# Read-Only Operations

Project discovery, inventory, inspection, plugin-manifest reading, runtime fingerprinting, reference analysis, validation, and asset scanning are designed to operate without mutating the selected project unless the user invokes an explicit save/import/edit operation.

Read-only code paths must not:

- create project metadata merely because a project was inspected;
- normalize or reserialize JSON;
- repair data automatically;
- change plugin status or order;
- decrypt protected deployment assets;
- launch the game runtime;
- evaluate project scripts, formulas, or plugin source.

The loader records consistency information while reading. If source files change during inspection, Playroma reports the project as unstable rather than silently presenting the snapshot as authoritative.

Save-capable operations are separate and use the transaction, recovery, external-change, and verification infrastructure documented in [SAVE_TRANSACTION_PROTOCOL.md](SAVE_TRANSACTION_PROTOCOL.md).
