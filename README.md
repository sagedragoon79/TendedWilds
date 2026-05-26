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

- **Pending relocations spawn as blueberry bushes after a save/quit + reload.** Relocation is a two-stage process: ordering a move places a temporary build site that the mod swaps to the correct forageable once builders finish. The swap is tracked in memory and is not yet persisted across save/load, so a relocation that is **still in progress** when you save & quit will complete as a blueberry bush on reload.

  **Workaround:** let any pending relocations finish building before you save & quit. Relocations that have already completed are unaffected — this only bites moves that are mid-flight at save time.

## Compatibility

- Farthest Frontier (Mono build) with MelonLoader.
- Plays nicely with Keep Clarity, Warden of the Wilds, Rivers Restored, and the rest of the constellation.
