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

- **In-progress relocations now survive save/reload** (v1.0.15+): a relocation that is still being built when you save & quit completes as the correct forageable after reload — including loads through autosaves and Save-As copies of the same settlement. Cancelling a pending relocation cleans up its record properly.

- **In-progress wild plantings still break across a save/quit + reload.** Wild planting's swap record is tracked in memory only, so a wild plant that is **still being built** when you save & quit will, on reload, complete as a blueberry bush (and/or lose its proper growing-season windows).

  **Best practice:** let pending wild plantings finish building before you save & quit. Anything already completed is unaffected.

  **Recovery (it's not permanent):** if you do reload with a pending wild planting and it comes up as a blueberry bush, just **cancel** the build site and re-issue the planting — it builds correctly the second time, even within the same reloaded session.

## Compatibility

- Farthest Frontier (Mono build) with MelonLoader.
- Plays nicely with Keep Clarity, Warden of the Wilds, Rivers Restored, and the rest of the constellation.
