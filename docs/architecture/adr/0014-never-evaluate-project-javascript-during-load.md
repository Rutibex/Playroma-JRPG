# Never evaluate project JavaScript during load

- Status: Accepted
- Date: 2026-07-19

## Context

Plugin manifests and source can contain arbitrary code with the user’s operating-system privileges.

## Decision

Extract the standard `$plugins` JSON array with a text scanner and inspect source only as text. No JavaScript engine is invoked by discovery or loading.

## Consequences

Opening an untrusted project does not automatically execute it. Nonstandard manifests may be reported rather than interpreted.
