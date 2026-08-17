# Lazy map loading

- Status: Accepted
- Date: 2026-07-19

## Context

Large MV projects can contain hundreds of maps and hundreds of thousands of event commands.

## Decision

Read `MapInfos.json` during standard loading and open map bodies only in complete mode or future on-demand workflows.

## Consequences

Project trees appear quickly while complete compatibility scans remain available.
