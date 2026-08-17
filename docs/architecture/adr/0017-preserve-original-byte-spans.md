# Preserve original byte spans

- Status: Accepted
- Date: 2026-07-19

## Context

Compatibility requires more than semantic JSON equality because large rewrites create noisy diffs and can alter plugin-sensitive text.

## Decision

Every syntax value records its original UTF-8 span. Changes are non-overlapping replacements applied over the original bytes.

## Consequences

Scalar edits can change one token while every unrelated byte is copied verbatim.
