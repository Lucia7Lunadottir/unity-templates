# Telekinesis

Grab, hold, rotate, pull/push and throw physical objects with the camera aim.

Controls: E tap - grab / release, E hold - take to the bag, LMB - throw, mouse wheel - move closer/away or rotate (CTRL switches), middle button - rotation axis.

## Setup
Add `TelekinesisSystem` to the player and assign the hold point, camera masks, a `VisualEffect`, the UI texts and the input actions (`Interact`, `TakeItem`, `Attack`, scroll, rotate type, depth changer). Needs `com.pg.localization-system` (texts) and `com.pg.menu` (`UIShowHide` panel).

## Bridge (what the game connects)
The system does not know about bags, loot or saving. `TelekinesisBridge` has three hooks:
- `takeHandler(Collider) -> bool`: E hold, put the thing under the aim into the bag; return true when taken.
- `grabFallback(Collider) -> Rigidbody`: the thing has no Rigidbody; return one to grab it (or null).
- `objectDropped(GameObject)`: the thing was released or thrown (re-home its save record, chunk, etc.).

`Samples~/InventoryBridge` shows a complete game side: copy `TelekinesisGameBridge.cs.txt` into your project, rename to `.cs` and adapt the loot and bag classes. Without any handler the system still grabs and throws objects that have a Rigidbody.
