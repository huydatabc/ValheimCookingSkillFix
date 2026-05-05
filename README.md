# CookingSkillFix

A small BepInEx patch mod for Valheim that fixes custom cooking stations from
**Valharvest** and **Oh Honey** not granting XP to blaxxun's **Cooking** skill.

## Problem

blaxxun's [Cooking](https://github.com/blaxxun-boop/Cooking) mod adds a custom
Cooking skill and patches vanilla cooking stations to raise it. However it only
knows about vanilla station names. Custom stations added by other mods fall
through to vanilla behaviour, giving **Crafting XP** instead of **Cooking XP**.

Affected stations:
- `rk_griddle` — Valharvest stone griddle
- `piece_apiary` — Oh Honey apiary

## Fix

This mod runs a Harmony postfix on `Player.Craft`. When a craft happens at one
of the above stations it:
1. Looks up blaxxun's Cooking skill dynamically at runtime (no hardcoded ID)
2. Raises it by the same amount blaxxun's mod uses
3. Undoes the incorrectly awarded Crafting XP

## Requirements

- BepInEx 5.x
- blaxxun's Cooking mod (soft dependency — mod does nothing if Cooking is absent)
- Valharvest and/or Oh Honey (whichever stations you want fixed)

## Install

Drop `CookingSkillFix.dll` into `BepInEx/plugins/`.

## Building

Push to GitHub — the Actions workflow builds it automatically.
Download the DLL from the **Actions** tab → latest run → **CookingSkillFix** artifact.

No local .NET install needed.
