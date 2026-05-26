# Tended Wilds

A Forager Shack overhaul for **Farthest Frontier** (MelonLoader + Harmony).

## Features

- **Year-round cultivation** — no seasonal lockout on the Forager Shack's cultivated plots.
- **Wild planting** — plant forageables (herbs, greens, roots, mushrooms, nuts, willow, berries) out in the wilderness for an item + gold cost.
- **1–9 priority system** — per-shack, per-item foraging priority instead of a simple on/off toggle.
- **Forageable relocation** — pick up and move any forageable (herb patch, mushroom cluster, hazelnut/willow/hawthorn/sumac bush, etc.) to a new spot.

Integrates with **Keep Clarity**'s in-game mod-manager settings panel when KC is installed.

## Relationship to Forageable Transplantation

Tended Wilds is a **strict superset** of Forageable Transplantation — it includes all of FT's relocation functionality plus the cultivation, wild-planting, and priority features. If you run both, FT auto-disables itself to avoid duplicate Harmony patches. If you're using TW, you can safely uninstall FT.

## Known issues

- **In-progress relocations and wild plantings break across a save/quit + reload.** Both features work in two stages: placing the order creates a temporary build site that the mod finishes into the correct forageable once builders complete it. That swap (and the seasonal setup) is tracked in memory and is not yet persisted across save/load. So a relocation **or** wild planting that is **still being built** when you save & quit will, on reload, complete as a blueberry bush (and/or lose its proper growing-season windows).

  **Workaround:** let any pending relocations and wild plantings finish building before you save & quit. Anything already completed is unaffected — this only bites orders that are mid-flight at save time.

## Compatibility

- Farthest Frontier (Mono build) with MelonLoader.
- Plays nicely with Keep Clarity, Warden of the Wilds, Rivers Restored, and the rest of the constellation.
