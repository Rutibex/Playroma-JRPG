# 0007 — Use NW.js as authoritative playtest and WebView2 as secondary host

- Status: Accepted
- Date: 2026-07-19

## Context

Browser-only hosting cannot run every Node-dependent plugin, while an integrated browser host offers useful inspection and messaging.

## Decision

Use the project's existing NW.js environment as the authoritative playtest when available. Add WebView2 as an integrated secondary host with compatibility warnings.

## Consequences

Two hosts must be clearly distinguished in the UI. Debug transport should support both without pretending their capabilities are identical.
