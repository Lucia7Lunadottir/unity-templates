# Inventory System (2.0.0)

Bag (`Inventory`), items (`Item`, `PlaceableItem`), crafting (`CraftRecipe`, `CraftController`), dropped and placeable items with saving (`DroppedItemManager`, `LootItem`, `PlaceableWorldObject`), quick item slots and the items and craft screens.

## Prefabs
- `Prefabs/Inventory – Panel`: the items screen (`ItemsMenuView`): grid of items, info panel, use, drop, assign to quick slots, place in the world.
- `Prefabs/Craft – Panel`: the craft screen (`CraftMenuView`) with recipe list and ingredients. Its recipe list is empty: assign your `CraftRecipe` assets on `CraftMenuView`.
- `Prefabs/Item Cell – Image`, `Prefabs/Ingredient Cell`, `Prefabs/Recipe – Panel`: the cells and rows the screens are built from.

The panels are plain `RectTransform` roots: put them under a Canvas that has the menu behaviour of `com.pg.menu` (`UIShowHide`, `ConsoleMenuNavigation`, `GamepadUIEnabler`, tabs). The scene needs an `Inventory` object. Input references are empty: assign your actions (navigation, back, assign slot, drop, place).

## Bridge
`InventoryBridge` connects the bag to the game, all optional:
- `currentDayProvider`: the game day stored with dropped things.
- `canPlaceItem(Item) -> bool`, `placeRequested(Item)`: the "Place" button of the items screen (for example, open build mode).
Food and other items with effects are your own `Item` subclasses: override `Interact` (public) and `satiety` for the items screen.

Requires `com.pg.menu`, `com.pg.localization-system`, `com.pg.save-system`, `com.pg.audio-management`, `com.pg.notifications`, `com.pg.interact-system`, `com.pg.pgtween`.
