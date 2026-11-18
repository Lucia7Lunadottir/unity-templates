# Save System (3.0.0)

Slot-based saves from World without Time: `ISaveable` auto-discovery, atomic file writes, checkpoints, guid-keyed object saves (`SaveDataObjectsContainer`), `PositionSaver`, `SaveSlot` UI and `SaveIconUI`.

The types are in the global namespace (except `ISaveableGuid`, namespace `PG.SaveSystem`), as in the game. Version 3.0.0 replaces the older namespaced 2.x layout, so code written for 2.x needs its usings adjusted.

Requires `com.pg.core` ([TagDropdown] on `CheckpointZone`) and `com.pg.menu` (`UIShowHide` on `SaveIconUI`); the Git Package Installer installs them together.

Warning: saves go to `Application.persistentDataPath`, which depends on the Company and Product names in Player Settings. Projects that share them share their saves.
