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
                /*order*/ 30
            });
        }

        private static object NewMeta(string label = null, string tooltip = null,
            object min = null, object max = null, bool restartRequired = false,
            int order = 0, Func<bool> visibleWhen = null)
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
            Reg("Master", TendedWildsMod.cfgModEnabled,
                NewMeta("Mod Enabled", "Disable to fall back to vanilla Forager Shack", restartRequired: true));
            Reg("Master", TendedWildsMod.cfgRelocationEnabled,
                NewMeta("Relocation Enabled", "Master toggle for forageable relocation"));

            // === Per-type relocation ===
            Func<bool> relocationOn = () => TendedWildsMod.cfgRelocationEnabled.Value;
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateHerbs, NewMeta("Herbs", visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateMushrooms, NewMeta("Mushrooms", visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateGreens, NewMeta("Greens", visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateRoots, NewMeta("Roots", visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateNuts, NewMeta("Hazelnuts", visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateWillow, NewMeta("Willow", visibleWhen: relocationOn));
            Reg("Relocate by Type", TendedWildsMod.cfgRelocateBerries,
                NewMeta("Berry Bushes", "Hawthorn, sumac", visibleWhen: relocationOn));

            // === Cost ===
            Reg("Cost", TendedWildsMod.cfgGoldCostToRelocate,
                NewMeta("Gold Cost per Relocation", min: 0, max: 100,
                    tooltip: "0 = free, just labor"));
        }
    }
}
