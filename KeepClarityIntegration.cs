using System;
using System.Reflection;
using MelonLoader;

namespace TendedWilds
{
    /// <summary>
    /// Optional integration with Keep Clarity's settings panel. No-op when
    /// KeepClarity.dll is absent.
    /// </summary>
    internal static class KeepClarityIntegration
    {
        private static bool _resolved, _present;
        private static MethodInfo _registerMod;
        private static MethodInfo _registerEntry;
        private static Type _settingsMetaType;

        private const string ModId = "TendedWilds";
        private const string ModDisplayName = "Tended Wilds";

        public static void TryRegisterAll()
        {
            if (!ResolveApi()) return;
            try
            {
                RegisterMod();
                RegisterEntries();
                MelonLogger.Msg("[TW] Registered with Keep Clarity settings panel");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TW] Keep Clarity registration failed: {ex.Message}");
            }
        }

        private static bool ResolveApi()
        {
            if (_resolved) return _present;
            _resolved = true;
            var apiType = Type.GetType("FFUIOverhaul.Settings.SettingsAPI, KeepClarity");
            if (apiType == null) { _present = false; return false; }
            _settingsMetaType = Type.GetType("FFUIOverhaul.Settings.SettingsMeta, KeepClarity");
            if (_settingsMetaType == null) { _present = false; return false; }
            _registerMod = apiType.GetMethod("RegisterMod", BindingFlags.Public | BindingFlags.Static);
            foreach (var m in apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "Register" && m.IsGenericMethodDefinition) { _registerEntry = m; break; }
            _present = _registerMod != null && _registerEntry != null;
            return _present;
        }

        private static void RegisterMod()
        {
            _registerMod.Invoke(null, new object[] {
                ModId, ModDisplayName,
                "Forager Shack overhaul: year-round cultivation, wild planting, 1-9 priority, forageable relocation",
                /*version*/ null,
                /*iconResourcePath*/ null,
                /*accentRgb — forest green*/ new[] { 0.20f, 0.55f, 0.30f, 1f },
                /*order*/ 20
            });
        }

        private static object NewMeta(string label = null, string tooltip = null,
            object min = null, object max = null, bool restartRequired = false,
            bool reloadRequired = false, int order = 0, Func<bool> visibleWhen = null)
        {
            var m = Activator.CreateInstance(_settingsMetaType);
            void Set(string field, object value)
            {
                var f = _settingsMetaType.GetField(field);
                if (f != null) f.SetValue(m, value);
            }
            Set("Label", label);
            Set("Tooltip", tooltip);
            Set("Min", min);
            Set("Max", max);
            Set("RestartRequired", restartRequired);
            Set("ReloadRequired", reloadRequired);
            Set("Order", order);
            Set("VisibleWhen", visibleWhen);
            return m;
        }

        private static void Reg<T>(string category, MelonPreferences_Entry<T> entry, object meta)
        {
            var closed = _registerEntry.MakeGenericMethod(typeof(T));
            closed.Invoke(null, new object[] { ModId, ModDisplayName, category, entry, meta });
        }

        private static void RegisterEntries()
        {
            // === Master ===
            // ModEnabled gates the whole mod via an early-return in OnInitializeMelon
            // (no patches wired if off) → restart.
            Reg("Master", TendedWildsMod.cfgModEnabled,
                NewMeta("Mod Enabled", "Disable to fall back to vanilla Forager Shack", restartRequired: true));
            // RelocationEnabled gates installation of the relocation Harmony patches
            // in OnInitializeMelon (line ~341). Without those patches relocation can't
            // function regardless of runtime value → restart.
            Reg("Master", TendedWildsMod.cfgRelocationEnabled,
                NewMeta("Relocation Enabled", "Master toggle for forageable relocation", restartRequired: true));

            // === Per-type relocation ===
            // Each toggle is read in ApplyBuildingData (started from OnSceneWasLoaded)
            // to decide which forageables get relocation BuildingData applied at map
            // load → reload.
            Func<bool> relocationOn = () => TendedWildsMod.cfgRelocationEnabled.Value;
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateHerbs, NewMeta("Herbs", reloadRequired: true, visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateMushrooms, NewMeta("Mushrooms", reloadRequired: true, visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateGreens, NewMeta("Greens", reloadRequired: true, visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateRoots, NewMeta("Roots", reloadRequired: true, visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateNuts, NewMeta("Hazelnuts", reloadRequired: true, visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateWillow, NewMeta("Willow", reloadRequired: true, visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateBerries,
                NewMeta("Berry Bushes", "Hawthorn, sumac", reloadRequired: true, visibleWhen: relocationOn));

            // === Cost ===
            // Baked into each forageable's goldRequiredToRelocate BuildingData in
            // ApplyBuildingData at map load → reload.
            Reg("Cost", TendedWildsMod.cfgGoldCostToRelocate,
                NewMeta("Gold Cost per Relocation", min: 0, max: 100,
                    tooltip: "0 = free, just labor", reloadRequired: true));

            // === Foraging ===
            // Gates installation of the ForagingManager exemption patches at init → restart.
            Reg("Foraging", TendedWildsMod.cfgExemptForagingBuildings,
                NewMeta("Exempt Wells/Hunter Shacks from Foraging Penalty",
                    "Wells and Hunter Shacks won't reduce nearby forageable output (Forager Shacks already exempt in vanilla)",
                    restartRequired: true));
        }
    }
}
