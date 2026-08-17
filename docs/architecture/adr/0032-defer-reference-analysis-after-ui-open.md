# ADR 0032: Defer Reference Analysis After UI Open

Status: Accepted for Phase 4.

The CLI may open sessions with blocking analysis. The WinForms shell uses deferred analysis so the project summary and navigation appear before the full reference index and validation complete. Analysis remains cancellable and visible through tracked background tasks.
