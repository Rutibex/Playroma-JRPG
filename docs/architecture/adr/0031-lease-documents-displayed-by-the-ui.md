# ADR 0031: Lease Documents Displayed by the UI

Status: Accepted for Phase 4.

Open document tabs acquire `IProjectDocumentLease`. Leased documents are excluded from clean-document LRU eviction until the tab closes. This prevents controls and typed views from referencing disposed document state.
