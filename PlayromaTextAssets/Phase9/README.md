# Playroma Text Studio Phase 9 — Maps, Events, and RPG Maker MV Messages

Phase 9 connects Text Studio to map text and structured RPG Maker MV event-command text.

## Map workflow

Open **Map Properties** and choose **✨ Generate Text…** for:

- Editor Name (`MapInfos.json`)
- Display Name (`MapXXX.json / displayName`)
- Map Note (`MapXXX.json / note`)

A generated map bundle can update all three destinations in one Playroma transaction. Existing plugin-style Map Note tags block whole-field Replace just like protected database Notes.

## Event workflow

The Event Editor now has a **✨ Text** split button and **Text Studio** menu. It can:

- generate for a selected Show Text block;
- generate for a selected Show Choices block;
- generate for a selected Scrolling Text block;
- generate for a selected Comment block;
- insert new Show Text;
- insert new Show Choices;
- insert a complete branching dialogue block;
- insert new Scrolling Text;
- insert a new Comment.

Generated blocks are compiled into native MV command rows (101/401, 102/402/403/404, 105/405, 108/408). They enter the active `EventEditSession` as one local undo operation. Event Editor Apply/OK later commits the session through Playroma's normal project transaction.

## Safety

- Show Text automatically wraps and paginates to normal four-line message pages.
- Show Choices is limited to RPG Maker MV's six-choice maximum.
- Existing face/background/window settings are preserved when replacing Show Text.
- Existing choice default/cancel/position/background settings are preserved when replacing Show Choices.
- Existing scrolling speed/fast-forward settings are preserved when replacing Scrolling Text.
- Choice blocks containing non-text gameplay commands are protected from wholesale generated replacement.
- Event Comments and Map Notes containing plugin-style tags are protected from destructive replacement.
- Unknown MV message escape codes are preserved and reported as warnings rather than discarded.

See `docs/TEXT_STUDIO_PHASE9_MAPS_EVENTS_MV_MESSAGES.md` for architecture and implementation details.
