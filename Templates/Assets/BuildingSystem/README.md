# Building System

Build mode from World without Time: place things from blueprints or from the bag with a ghost preview, rotate, dismantle. Materials are taken from the inventory, blueprints are found with pickups and saved per save slot.

## Quick start
1. Install the package (the Git Package Installer also installs the inventory, menu, localization, save and interact packages).
2. Drop `Prefabs/Build Mode – Canvas` into the scene. It has `BuildMode` and `BuildModeView` and the whole build screen.
3. Give it a Build Catalog: replace `Default Build Catalog` (Create > PG > Build > Catalog) with your own list of blueprints and bag items.
4. Make sure the player has the tag from `Player Tag` (default `Player`), the scene has a main camera and an `Inventory`.

## Input
Actions are found in the project Input Actions by path: `Player/Build` (enter; or assign the `Enter` reference on `BuildMode`) and an action map `Build` with `Place`, `Previous`, `Next`, `Rotate Left`, `Rotate Right`, `Category`, `Dismantle` and `Exit`. Create them in your Input Actions asset. While you build, the map `Build` should be the active one (connect it through `gameplayInputChanged`).

## Bridge (what the game connects)
`BuildBridge` has hooks, all optional:
- `enterBlockers(Transform player) -> bool`: building is not allowed now (magic, flight, telekinesis, dialogue, in the air).
- `exitTriggers() -> bool`: close the mode right now (took off, dialogue started).
- `gameplayInputChanged(owner, enable)`: switch the input context while building.
- `built(bool byRecipe)`, `blueprintsChanged(int count)`: for achievements and statistics.
- `noticeRequested(title, body)`: show a message (for example, "New blueprint").
- `extraSurface(Collider) -> bool`: surfaces that count as buildable (for example, snow).
The placing button of the inventory screen connects through `InventoryBridge` from the inventory package.
