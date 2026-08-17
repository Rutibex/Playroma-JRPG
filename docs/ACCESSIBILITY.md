# Accessibility

Playroma's production multi-window UI includes:

- semantic light, dark, and Windows High Contrast palettes;
- DPI- and text-scale-aware visual metrics;
- accessible names, descriptions, roles, and tab ordering;
- visible keyboard focus for owner-drawn controls;
- keyboard routes for map editing, event commands, database navigation, and resource selection;
- non-color severity indicators and text;
- live announcements for important validation and operation results;
- responsive layouts intended to remain usable at larger text sizes.

The production editor uses dedicated Forms rather than a hidden central document-tab shell, which keeps focus ownership and accessible window identity explicit.

Before a public release, manually test Windows High Contrast, 100/150/200 percent DPI, keyboard-only workflows, and a supported screen reader in addition to automated WinForms smoke tests.
