# 0001 — Use .NET 10 and WinForms for the editor

- Status: Accepted
- Date: 2026-07-19

## Context

Playroma jRPG is a Windows-first desktop authoring tool intended to resemble the productive, dense interface of RPG Maker MV while integrating the wider Playroma C# ecosystem.

## Decision

Use .NET 10 for the solution and WinForms for the editor shell beginning in Phase 4. Keep format, document, validation, search, and generator libraries free of WinForms dependencies.

## Consequences

The editor benefits from mature Windows controls and C# integration. Core libraries remain testable and reusable on non-Windows systems. The project must explicitly handle DPI, accessibility, theming, and custom accelerated drawing.
