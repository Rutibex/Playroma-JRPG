# Save Recovery

Use the CLI to inspect transaction journals:

```bash
playroma-jrpg recovery inspect /path/to/project
```

A candidate includes its transaction ID, last journal state, and recommendation.

```bash
playroma-jrpg recovery rollback /path/to/project <transaction-id>
playroma-jrpg recovery complete /path/to/project <transaction-id>
```

Rollback requires every recorded backup. Completion requires every unfinished
temporary output. Missing or hash-inconsistent material must be treated as
manual review rather than guessed.
