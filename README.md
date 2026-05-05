# CookingSkillFix

A small BepInEx patch mod for Valheim that fixes several incompatibilities
between Smoothbrain's **Cooking** skill mod, **Valharvest**, **BoneAppetit**,
and **Oh Honey**.

## Fixes

### 1. Cooking skill XP at custom stations
Smoothbrain's [Cooking](https://thunderstore.io/c/valheim/p/Smoothbrain/Cooking/)
mod only knows about vanilla station names. Crafting at custom stations from
other mods awards Crafting XP instead of Cooking XP.

Fixed stations:
- `rk_griddle` — Valharvest stone griddle
- `piece_prep_table` — Valharvest preparation table
- `piece_apiary` — Oh Honey apiary

### 2. Serving tray compatibility
The vanilla serving tray (`piece_itemstand_food`) rejects mod-added food items
because they never have `m_itemType` set to `Material`. This affects food from
Valharvest, BoneAppetit, and Oh Honey.

This fix scans all registered items at load time and corrects any item that has
food stats (`m_food > 0`) but the wrong item type — but only if the item has a
visible mesh on its prefab, and is not a vanilla item. Covers all food mods
automatically with no hardcoded item list.

### 3. Valharvest food boxes as OdinsFoodBarrels containers
Valharvest adds buildable food boxes (garlic, pepper, potato, tomato, salt,
apple) that normally require you to build/deconstruct to get items back.

When [OdinsFoodBarrels](https://thunderstore.io/c/valheim/p/OdinHimself/OdinsFoodBarrels/)
is installed, this fix registers those boxes with its container restriction
system so they behave like barrels — interact to deposit/withdraw items directly.

Registered boxes:
- `piece_garlicBox` → garlic
- `piece_pepperBox` → pepper
- `piece_potatoBox` → potato
- `piece_tomatoBox` → tomato
- `piece_saltBox` → salt
- `piece_appleBox` → apple

## Requirements

- BepInEx 5.x
- [Smoothbrain's Cooking mod](https://thunderstore.io/c/valheim/p/Smoothbrain/Cooking/)
  (soft dependency — Fix 1 does nothing if absent)
- [OdinsFoodBarrels](https://thunderstore.io/c/valheim/p/OdinHimself/OdinsFoodBarrels/)
  (soft dependency — Fix 3 does nothing if absent)
- Valharvest, BoneAppetit, and/or Oh Honey (whichever you use)

## Install

Drop `CookingSkillFix.dll` into `BepInEx/plugins/`.

## Building

Push to GitHub — the Actions workflow builds automatically.
Download the DLL from **Actions → latest run → CookingSkillFix artifact**.
