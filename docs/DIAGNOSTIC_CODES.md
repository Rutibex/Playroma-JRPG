# Diagnostic Codes

Diagnostic codes are stable API identifiers. Messages may improve without changing the code.

| Range | Area |
|---|---|
| `PJRPG1001–1004` | discovery and project family |
| `PJRPG1101–1106` | standard data and maps |
| `PJRPG1201–1203` | JSON and text encoding |
| `PJRPG1301–1303` | assets and encryption |
| `PJRPG1401–1405` | plugin manifest and scripts |
| `PJRPG1501–1503` | runtime identification |
| `PJRPG1601–1603` | file consistency and inventory |
| `PJRPG1701–1703` | permissions and path safety |
| `PJRPG1801` | unknown event command |
| `PJRPG9001–9008` | Actor and Class authoring |

The constants are defined in `Playroma.JRPG.Core.DiagnosticCodes`. Fatal diagnostics prevent a snapshot. Errors permit partial inspection unless `ContinueAfterErrors` is disabled. Warnings describe compatibility risks. Information diagnostics describe unusual but preservable data.

## Document and save diagnostics

| Code | Meaning |
|---|---|
| PJRPG2001 | Invalid mutable JSON document |
| PJRPG2002 | Unsupported document encoding |
| PJRPG2003 | Duplicate JSON property preserved |
| PJRPG2004 | Typed property has an unexpected type |
| PJRPG2005 | Required typed property is missing |
| PJRPG2006 | Document is restricted to read-only preservation |
| PJRPG2101 | Conflicting source edits |
| PJRPG2102 | Mutation targets a stale syntax node |
| PJRPG2103 | Generated output contains an unauthorized difference |
| PJRPG2104 | Invalid array mutation |
| PJRPG2105 | Record ID differs from its physical array index |
| PJRPG2201 | File changed externally after opening |
| PJRPG2202 | Temporary output could not be written |
| PJRPG2203 | Generated JSON failed validation |
| PJRPG2204 | Atomic replacement or save transaction failed |
| PJRPG2205 | Saved file hash differs from the save plan |
| PJRPG2206 | Backup creation failed |
| PJRPG2207 | Save transaction requires recovery |
| PJRPG2208 | Insufficient disk space |
| PJRPG2209 | Project save lock is already held |
| PJRPG2301 | Incomplete save transaction found |
| PJRPG2302 | Transaction can be rolled back |
| PJRPG2303 | Transaction can be completed |
| PJRPG2304 | Recovery state is ambiguous |
| PJRPG2305 | Required recovery backup is missing |

## Editing, reference, and validation diagnostics

| Code | Meaning |
|---|---|
| PJRPG3001 | Exact project reference points to a missing target |
| PJRPG3002 | Player or vehicle starting position is invalid |
| PJRPG3003 | Plugin manifest entry has no corresponding script file |
| PJRPG3004 | Duplicate plugin entry was found and preserved |
| PJRPG3005 | Plugin command appears to reference a disabled plugin |
| PJRPG3006 | Map data length does not match width × height × 6 |
| PJRPG3007 | Event command list or indentation is structurally invalid |
| PJRPG3008 | Autorun event has no apparent termination path |
| PJRPG3009 | Editing transaction failed or could not be restored completely |
| PJRPG3010 | Reference index could not be built |

## Asset catalogue and operation diagnostics

| Code | Meaning |
|---|---|
| PJRPG5001 | Referenced logical asset has no physical file |
| PJRPG5002 | Asset metadata is corrupt or unreadable |
| PJRPG5003 | Built-in preview provider does not support the format |
| PJRPG5004 | Logical asset has conflicting physical variants or exact duplicate content |
| PJRPG5005 | MV sprite/icon/animation/tile sheet grid is invalid or unusual |
| PJRPG5006 | Asset file is empty |
| PJRPG5007 | Pending asset operation conflicts with another path or destination |
| PJRPG5008 | Staged import/replacement content is missing |
| PJRPG5009 | Staged or saved asset hash differs from the transaction plan |
| PJRPG5010 | Host media decoder does not support the codec |
| PJRPG5011 | Asset changed externally after the operation was staged |

## Tileset and map-rendering diagnostics

| Code | Meaning |
| --- | --- |
| `PJRPG6001` | The map's tileset record is missing or outside `Tilesets.json`. |
| `PJRPG6002` | The map data length does not align exactly with width × height layers. |
| `PJRPG6003` | A map or event tile value is invalid for standard MV rendering. |
| `PJRPG6004` | A shadow cell contains high bits; only the low four bits are rendered and the raw value is preserved. |
| `PJRPG6005` | An autotile shape is invalid for its floor, wall, or waterfall family. |
| `PJRPG6006` | A required tileset, character, or parallax image could not be loaded. |
| `PJRPG6007` | A tileset image has unusual dimensions or a source rectangle falls outside it. |
| `PJRPG6008` | A character sheet does not match the required MV frame grid. |
| `PJRPG6009` | An enabled plugin may add rendering that is not represented by the standard MV base map. |
| `PJRPG6010` | A visual regression comparison differs from its approved reference image. |


## Actor and Class diagnostics

| Code | Meaning |
|---|---|
| `PJRPG9001` | A database field value is invalid at the mutation boundary. |
| `PJRPG9002` | An Actor has an invalid initial/maximum level range. |
| `PJRPG9003` | A Class has malformed EXP or parameter-curve data. |
| `PJRPG9004` | An Actor initial-equipment slot references a missing Weapon or Armor record. |
| `PJRPG9005` | A Class contains a duplicate level/skill learning pair. |
| `PJRPG9006` | A Class learning references a missing Skill record. |
| `PJRPG9007` | A Class learning is above the maximum level of every Actor currently using that Class. |
| `PJRPG9008` | A Class learning has a level outside the MV range 1–99. |

## Animation diagnostics

| Code | Meaning |
|---|---|
| `PJRPG9301` | Animation record or position value is invalid. |
| `PJRPG9302` | Animation frame is missing or is not an array. |
| `PJRPG9303` | Animation cell does not contain the required MV values. |
| `PJRPG9304` | Stored extension cells beyond runtime lane 15 are preserved. |
| `PJRPG9305` | Animation pattern is outside the standard 0–199 range. |
| `PJRPG9306` | Animation timing, flash, colour, or sound structure is invalid. |
| `PJRPG9307` | Animation timing refers to a frame outside the animation. |
| `PJRPG9308` | Animation has no frames. |
| `PJRPG9309` | Animation has no visible cells in MV Core lanes 0–15. |

## Tileset, System, Types, and Terms diagnostics

| Code | Meaning |
|---|---|
| `PJRPG9401` | Tileset record or `Tilesets.json` shape is invalid. |
| `PJRPG9402` | Tileset mode is not World (0) or Area (1). |
| `PJRPG9403` | Tileset image-slot array is malformed or unusual. |
| `PJRPG9404` | Tileset image asset is missing. |
| `PJRPG9405` | Tileset flag array or flag value is invalid. |
| `PJRPG9406` | Tileset flag array has fewer than 8,192 entries. |
| `PJRPG9407` | Tileset flag array has a preserved extension tail. |
| `PJRPG9408` | The 48 shapes of one logical autotile have different stored flags. |
| `PJRPG9410` | `System.json` is invalid. |
| `PJRPG9411` | Player or vehicle starting map/coordinates are invalid. |
| `PJRPG9412` | Vehicle structure is invalid. |
| `PJRPG9413` | A System graphic or audio asset is missing. |
| `PJRPG9414` | Audio setting is invalid or outside MV ranges. |
| `PJRPG9415` | A System array has an invalid shape or value type. |
| `PJRPG9416` | System data references a missing database record. |
| `PJRPG9417` | Attack-motion count differs from the weapon-type layout. |
| `PJRPG9418` | An attack-motion row is invalid. |
| `PJRPG9419` | Terms structure is invalid. |
| `PJRPG9420` | Edited Terms text removed one or more required placeholders. |


## Event authoring diagnostics

| Code | Meaning |
|---|---|
| `PJRPG10001` | A command could not be attached to the structural projection. |
| `PJRPG10002` | Command indentation jumps unexpectedly. |
| `PJRPG10003` | A continuation, branch, or closing row has no compatible opening row. |
| `PJRPG10004` | A structural command has no expected branch markers. |
| `PJRPG10005` | A conditional, loop, choice, or battle block has no closing command. |
| `PJRPG10010` | An unknown or plugin-created command code is preserved. |
| `PJRPG10011` | Command indentation is invalid. |
| `PJRPG10012` | The command parameter value is not a valid MV parameter array. |
| `PJRPG10013` | A command has fewer parameters than its formal schema requires. |
| `PJRPG10014` | A command parameter has an unexpected JSON type. |
| `PJRPG10015` | The event list has no code-0 terminator. |
| `PJRPG10016` | Executable commands appear after the terminator. |
| `PJRPG10017` | The terminator has invalid indent or parameters. |
| `PJRPG10018` | A label is duplicated in one event list. |
| `PJRPG10019` | Jump to Label has no matching label. |
| `PJRPG10020` | Set Movement Route contains no valid route object. |
| `PJRPG10021` | A 505 continuation row contains no route command. |
| `PJRPG10022` | Embedded route and 505 continuation counts differ. |
| `PJRPG10023` | Embedded route and 505 continuation content differs. |
| `PJRPG10024` | A Loop block contains no apparent Break Loop command. |
