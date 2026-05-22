# Tended Wilds + Forageable Transplantation — Handoff Summary

## Tended Wilds (TW)

**Repo:** `C:\Users\saged\source\repos\Tended Wilds\`
**Current version:** **1.0.10**
**Status:** Built green, deployed, version metadata verified.

### What TW does
Forager Shack overhaul for Farthest Frontier:
- Year-round cultivation (no seasonal lockout)
- Wild planting (plant forageables in the wilderness)
- 1–9 priority system
- Forageable relocation (full superset of FT's functionality — see FT relationship below)

### Recent version history
- **v1.0.7** — Chain-on-failure `ApplyBuildingData` rework
- **v1.0.10** — KC mod-manager order alignment fix
  - `KeepClarityIntegration.cs`: changed `order: 30 → 20` to match the cross-mod convention (`KC=0, RR=10, others=20`)
  - `TendedWilds.cs`: fixed stale `v1.0.7` startup log message → `v1.0.10`
  - **AssemblyInfo.cs was stale at `1.0.9.0`** while MelonInfo was `1.0.10` — bumped both `AssemblyVersion` and `AssemblyFileVersion` to `1.0.10.0`, rebuilt Release, verified `(Get-Item ...).VersionInfo` returns `FileVersion=1.0.10.0 / ProductVersion=1.0.10.0`.

### Key files
- `TendedWilds.cs` — Main MelonMod, `MelonInfo "Tended Wilds" "1.0.10"`
- `KeepClarityIntegration.cs` — KC settings panel registration, order=20
- `Properties\AssemblyInfo.cs` — `AssemblyVersion("1.0.10.0")` / `AssemblyFileVersion("1.0.10.0")`
- `TendedWilds.csproj` — Legacy MSBuild-style csproj (NOT SDK-style); uses `Properties\AssemblyInfo.cs` for version metadata (not `<Version>` element)

### Build/deploy
- `dotnet build TendedWilds.csproj -c Release` from repo root
- Post-build target copies DLL to `G:\SteamLibrary\steamapps\common\Farthest Frontier\Farthest Frontier (Mono)\Mods\`
- **Important pattern:** TW's csproj is *legacy-style*, so version comes from `Properties\AssemblyInfo.cs`, not csproj. Don't confuse with MD's SDK-style `<Version>` element.

### Released
- v1.0.10 has a GitHub Release with zip attached (backfilled per the standing rule)

---

## Forageable Transplantation (FT)

**Repo:** `C:\Users\saged\source\repos\Forageable Transplantation\`
**Current version:** **1.1.7**
**Status:** Built, deployed, released.

### What FT does
Standalone relocation for all forageables. **TW is a strict superset** — TW includes all FT functionality.

### TW/FT redundancy kill-switch
FT contains a kill-switch in `OnInitializeMelon`: when TW is detected as loaded, FT logs a warning and **early-returns from `OnInitializeMelon`**, leaving its patches uninstalled. This means:
- Users running TW can leave FT installed with no conflict
- But when recommending mods to TW users, **treat FT as redundant** and recommend uninstalling FT
- Do not duplicate patches between the two — anything that lands in FT must already exist in TW or be ported up

### Recent version history
- **v1.1.4** — Stable baseline
- **v1.1.7** — KC mod-manager order fix
  - `KeepClarityIntegration.cs`: changed `order: 40 → 20`
  - Built Release, committed, pushed, tagged, GitHub Release with zip attached

### Key files
- `ForageableTransplantation.cs` — Main MelonMod, `MelonInfo "Forageable Transplantation" "1.1.7"`
- `KeepClarityIntegration.cs` — KC settings panel registration, order=20

### Build/deploy
- `dotnet build -c Release` from repo root
- Post-build copies DLL to FF Mods folder
- Check `Properties\AssemblyInfo.cs` for version metadata if FT also uses legacy csproj style (verify before next bump — was not explicitly audited like TW was)

---

## Standing rules (apply to both TW & FT)

1. **Git push always implies a GitHub Release.** Every versioned commit → tag (`v1.x.y` annotated tag) → `gh release create` with zip attached. User stated this explicitly: *"if we push to git, I want a release created as well in the future."*

2. **Debug builds by default during dev.** Only Release on explicit ship request (memory note `feedback_debug_builds.md`). Recent KC-order fixes were Release because they were shipping bumps.

3. **Deploy while FF is running.** MelonLoader releases the file handle. Don't ask the user to close FF — just `cp` the DLL.

4. **KC integration order convention:** `KC=0, RR=10, all other mods=20`. Source: `SettingsRegistry.cs:114-117` in Keep Clarity. Every FF mod should use the canonical `KeepClarityIntegration.cs` template (WotW's is the reference).

5. **Verify version metadata** — both DLL `FileVersion` (`AssemblyInfo.cs` for legacy csprojs, `<Version>` for SDK-style) AND `MelonInfo` attribute must match. The TW fix today caught a divergence.

6. **`_FF.dll` suffix** = Steam Workshop deployment, auto-redeployed on game launch. Conflicts with local `*.dll` builds — be aware when diagnosing patch collisions.

---

## Quick reference paths

- **TW repo:** `C:\Users\saged\source\repos\Tended Wilds\`
- **FT repo:** `C:\Users\saged\source\repos\Forageable Transplantation\`
- **FF Mods folder:** `G:\SteamLibrary\steamapps\common\Farthest Frontier\Farthest Frontier (Mono)\Mods\`
- **MelonLoader log (always use this, NOT dated logs):** `G:\SteamLibrary\steamapps\common\Farthest Frontier\Farthest Frontier (Mono)\MelonLoader\Latest.log`
- **FF decompile (for reflection target lookups):** `C:\Users\saged\ClaudeCodeLocalSessions\AssemblyCSharp_Decompiled\Assembly-CSharp.decompiled.cs` (~16 MB)

## Current state
- **TW v1.0.10** — shipped, DLL metadata corrected today, on disk in Mods/, GitHub Release live
- **FT v1.1.7** — shipped, GitHub Release live, kill-switch active when TW is present
- **No open work** on either as of this handoff
