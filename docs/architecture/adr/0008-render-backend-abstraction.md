# 0008 — Abstract the map rendering backend

- Status: Accepted
- Date: 2026-07-19

## Context

A tile editor needs accelerated custom drawing, but the optimal Windows rendering backend may change as performance requirements become clearer.

## Decision

Define renderer and surface interfaces outside WinForms. The first accelerated implementation may use SkiaSharp, Direct2D, or another reviewed backend without changing document or editor-command code.

## Consequences

There is an additional abstraction layer, but rendering tests and future backend changes remain isolated.
