# Project Discovery

`MvProjectLocator` performs a bounded breadth-first search. The default maximum depth is three directories and the default maximum directory count is 10,000. It does not search an entire drive.

Strong MV evidence includes `Game.rpgproject`, `data/System.json`, `data/MapInfos.json`, the standard database set, `js/rpg_core.js`, `index.html`, `package.json`, and `js/plugins.js`.

A wrapper containing `www/data/System.json` is returned as one candidate rooted at the wrapper. A marker inside `www` is associated with that wrapper so the external NW.js executable and package configuration remain part of the inventory. Duplicate candidates sharing the same data root are collapsed in favor of the strongest evidence.

Reparse-point directories are not traversed. MZ markers are returned as recognized unsupported candidates.
