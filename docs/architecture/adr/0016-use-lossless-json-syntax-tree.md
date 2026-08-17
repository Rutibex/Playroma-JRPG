# Use a lossless JSON syntax tree

- Status: Accepted
- Date: 2026-07-19

## Context

Ordinary object serialization can discard unknown properties and rewrite unrelated lexical content.

## Decision

Parse UTF-8 JSON into source-spanned syntax nodes while retaining the original byte buffer. Typed MV views wrap this syntax rather than replacing it.

## Consequences

The implementation is more complex than DTO serialization, but untouched source and plugin-owned data can be preserved exactly.
