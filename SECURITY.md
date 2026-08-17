# Security Policy

## Supported versions

Security fixes are applied to the current `main` branch and, when practical, the latest tagged public release.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could execute untrusted
project JavaScript, escape a playtest sandbox, overwrite files outside a
project, expose debugger ports, or corrupt project data.

Report it privately to the repository owner with:

- affected revision,
- reproduction steps,
- impact,
- proof-of-concept files that contain no proprietary content,
- suggested mitigation when available.

## Threat model

Playroma treats every opened project as untrusted input.

Ordinary loading and inspection must not execute project JavaScript, plugins,
formulas, or external programs. Later playtest features must require an
explicit user action and visibly indicate that project code is running.

Project writes must remain inside the selected project or an explicitly chosen
export destination. Temporary and backup paths must be validated against path
traversal and symlink/reparse-point attacks.

## Text Studio optional AI providers

Text Studio is offline and provider-free by default. Optional AI providers must be explicitly configured and enabled by the project author.

The built-in process bridge launches the selected executable directly with `UseShellExecute=false`; Playroma does not construct a shell command. Treat any executable you configure as trusted local software. Provider processes can have the same operating-system access as other programs launched by the current user.

Project generation context is not supplied to optional providers unless the recipe explicitly enables `includeProjectContext`. Do not enable that option for a provider whose privacy/security characteristics you have not reviewed.

Provider configuration intentionally does not require or define API-key storage. If an external wrapper needs credentials, use that wrapper's secure credential mechanism rather than placing secrets in Playroma recipe files.
