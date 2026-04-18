using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

// ─────────────────────────────────────────────────────────────────────────────
//  TendedWildsAPI  (added in Tended Wilds v1.1.0)
//  Public static bridge class for companion mods.
//
//  This class exposes read-only access to Tended Wilds' internal state via
//  a stable public API. Companion mods call these methods via reflection —
//  no compile-time dependency on TendedWilds.dll is required.
//
//  Usage from a companion mod (e.g. Stalk & Smoke):
//    System.Type apiType = null;
//    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
//    {
//        apiType = asm.GetType("TendedWilds.TendedWildsAPI");
//        if (apiType != null) break;
//    }
//    if (apiType != null)
//    {
//        var method = apiType.GetMethod("GetAttractionBonusNear", ...);
//        float bonus = (float)method.Invoke(null, new object[] { position, radius });
//    }
//
//  Companion mods detect TW by checking:
//    mod.Info.Name.Contains("Tended Wilds") in MelonBase.RegisteredMelons
//  (MOD_ID constant below is the canonical name for that check.)
// ─────────────────────────────────────────────────────────────────────────────

namespace TendedWilds
{
    public static class TendedWildsAPI
    {
        /// <summary>
        /// Canonical mod name for companion detection.
        /// Companion mods should check RegisteredMelons for this string.
        /// </summary>
        public const string MOD_ID = "Tended Wilds";

        private static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // ── Attraction bonus for Deer Stands (Stalk & Smoke companion) ────────

        /// <summary>
        /// Returns a multiplicative attraction bonus (≥ 1.0) based on cultivated
        /// berry bushes and greens within radius of the given world position.
        ///
        /// A Deer Stand placed near cultivated berry plots benefits from the
        /// natural lure that cultivated plants provide to deer.
        ///
        /// Formula:
        ///   baseBonus = 1.0
        ///   +0.05 per Berries-cultivating ForagerShack within radius
        ///   +0.03 per Greens-cultivating ForagerShack within radius
        ///   capped at 1.5
        /// </summary>
        public static float GetAttractionBonusNear(Vector3 position, float radius)
        {
            try
            {
                float bonus = 1.0f;
                float radiusSqr = radius * radius;

                foreach (var shack in GetForagerShacksNear(position, radiusSqr))
                {
                    var cultivatedItems = GetCultivatedItemIDs(shack);
                    foreach (int itemId in cultivatedItems)
                    {
                        // ItemID.Berries = 3 (confirmed from FF decompile ItemID enum)
                        if (itemId == (int)ItemID.Berries)  bonus += 0.05f;
                        // ItemID.Greens = 15
                        if (itemId == (int)ItemID.Greens)   bonus += 0.03f;
                        // ItemID.Nuts = 19 (hazelnut bushes also attract deer)
                        if (itemId == (int)ItemID.Nuts)     bonus += 0.04f;
                    }
                }

                return Mathf.Clamp(bonus, 1.0f, 1.5f);
            }
            catch { return 1.0f; }
        }

        // ── Willow stock for trap discount (Stalk & Smoke companion) ──────────

        /// <summary>
        /// Returns the total willow units available across all ForagerShacks
        /// within radius of position.
        ///
        /// Used by Stalk & Smoke to gate reduced willow cost for crafting
        /// hunting traps at a Trapper Lodge — nearby cultivated willow
        /// provides the raw material for wickerwork snares.
        /// </summary>
        public static int GetWillowStockNear(Vector3 position, float radius)
        {
            try
            {
                float radiusSqr = radius * radius;
                int totalWillow = 0;

                foreach (var shack in GetForagerShacksNear(position, radiusSqr))
                {
                    // Check cultivated willow (ItemID.Willow = 56)
                    var cultivatedItems = GetCultivatedItemIDs(shack);
                    if (!cultivatedItems.Contains((int)ItemID.Willow)) continue;

                    // Count willow in storage
                    totalWillow += GetItemCountInShackStorage(shack, (int)ItemID.Willow);
                }

                return totalWillow;
            }
            catch { return 0; }
        }

        // ── Herb/mushroom stock for smokehouse herb-cure (Stalk & Smoke) ──────

        /// <summary>
        /// Returns the total herb and mushroom units available across all
        /// ForagerShacks within radius of position.
        ///
        /// Used by Stalk & Smoke's SmokehouseEnhancement.ShouldHerbCure()
        /// to determine if a herb-cured smoking cycle is possible.
        /// Herb-cured smoked goods gain a food variety bonus tag.
        /// </summary>
        public static int GetHerbStockNear(Vector3 position, float radius)
        {
            try
            {
                float radiusSqr = radius * radius;
                int total = 0;

                foreach (var shack in GetForagerShacksNear(position, radiusSqr))
                {
                    // ItemID.Herbs = 23, ItemID.Mushroom = 17
                    total += GetItemCountInShackStorage(shack, (int)ItemID.Herbs);
                    total += GetItemCountInShackStorage(shack, (int)ItemID.Mushroom);
                }

                return total;
            }
            catch { return 0; }
        }

        // ── Fertilizer application for fish oil (Stalk & Smoke companion) ─────

        /// <summary>
        /// Applies a temporary replenishment rate bonus to the ForagerShack
        /// at shackPosition (matched by proximity).
        ///
        /// Used by Stalk & Smoke when Fish Oil is consumed as fertilizer —
        /// the fish oil boosts cultivated plant replenishment rate for the
        /// specified number of game months.
        ///
        /// Parameters:
        ///   shackPosition   — world position of the ForagerShack to boost
        ///   multiplier      — replenishment rate multiplier (e.g. 1.25 = +25%)
        ///   durationMonths  — how many in-game months the bonus lasts
        ///                     (applied immediately; tracking is external)
        /// </summary>
        public static void ApplyReplenishmentBonus(
            Vector3 shackPosition, float multiplier, int durationMonths)
        {
            try
            {
                // Find the shack nearest to shackPosition
                ForagerShack? target = null;
                float bestDist = 4f; // 2u match radius
                foreach (var shack in GetAllForagerShacks())
                {
                    float dist = (shack.transform.position - shackPosition).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        target = shack;
                    }
                }
                if (target == null) return;

                // Apply bonus to itemToReplenishRateDict for all cultivated items
                ApplyReplenishRateMultiplier(target, multiplier);

                MelonLoader.MelonLogger.Msg(
                    $"[TW-API] Fish oil fertilizer: shack '{target.name}' at {shackPosition} " +
                    $"×{multiplier:F2} for {durationMonths} months.");

                // Duration tracking: register a coroutine to revert after durationMonths
                // For now, the multiplier is applied once; future version will track and revert.
                // TODO: implement revert coroutine keyed to in-game month counter
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[TW-API] ApplyReplenishmentBonus: {ex.Message}");
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private static IEnumerable<ForagerShack> GetForagerShacksNear(
            Vector3 position, float radiusSqr)
        {
            foreach (var shack in GetAllForagerShacks())
            {
                float dist = (shack.transform.position - position).sqrMagnitude;
                if (dist <= radiusSqr)
                    yield return shack;
            }
        }

        private static IEnumerable<ForagerShack> GetAllForagerShacks()
        {
            // Preferred path: GameManager.resourceManager.foragerShacksRO
            // (confirmed from Tended Wilds source: TendedWildsMod accesses gm.resourceManager)
            try
            {
                var gm = GameObject.FindObjectOfType<GameManager>();
                if (gm != null)
                {
                    // resourceManager is public on GameManager — direct access
                    var rm = gm.resourceManager;
                    if (rm != null)
                    {
                        // foragerShacksRO is a read-only list property on ResourceManager
                        var shacksProp = rm.GetType().GetProperty("foragerShacksRO",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (shacksProp != null)
                        {
                            var shacks = shacksProp.GetValue(rm) as IEnumerable<ForagerShack>;
                            if (shacks != null)
                            {
                                foreach (var s in shacks) yield return s;
                                yield break;
                            }
                        }
                    }
                }
            }
            catch { }

            // Fallback: brute-force scene search
            foreach (var shack in GameObject.FindObjectsOfType<ForagerShack>())
                yield return shack;
        }

        private static List<int> GetCultivatedItemIDs(ForagerShack shack)
        {
            var result = new List<int>();
            try
            {
                // From TendedWilds source: ForagerShack.cultivatedItems field
                var field = typeof(ForagerShack).GetField("cultivatedItems", AllInstance);
                if (field == null) return result;
                var list = field.GetValue(shack) as System.Collections.IList;
                if (list == null) return result;

                foreach (var entry in list)
                {
                    if (entry == null) continue;
                    var itemField = entry.GetType().GetField("item", AllInstance);
                    if (itemField == null) continue;
                    var item = itemField.GetValue(entry);
                    if (item == null) continue;
                    // Cast both sides to MemberInfo so ?? resolves correctly
                    System.Reflection.MemberInfo itemIdProp =
                        (System.Reflection.MemberInfo)item.GetType().GetProperty("itemID", AllInstance)
                        ?? item.GetType().GetField("itemID", AllInstance);
                    if (itemIdProp == null) continue;

                    object idVal = null;
                    if (itemIdProp is System.Reflection.PropertyInfo pi)
                        idVal = pi.GetValue(item);
                    else if (itemIdProp is System.Reflection.FieldInfo fi)
                        idVal = fi.GetValue(item);
                    if (idVal != null)
                        result.Add(System.Convert.ToInt32(idVal)); // enum → int safe conversion
                }
            }
            catch { }
            return result;
        }

        private static int GetItemCountInShackStorage(ForagerShack shack, int itemIdInt)
        {
            try
            {
                var method = shack.GetType().GetMethod(
                    "GetItemCountFromAllStorages",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return 0;

                // Build an Item instance
                System.Type? itemType = null;
                System.Type? itemIdType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (itemType == null)   itemType   = asm.GetType("Item");
                    if (itemIdType == null) itemIdType = asm.GetType("ItemID");
                    if (itemType != null && itemIdType != null) break;
                }
                if (itemType == null || itemIdType == null) return 0;

                object itemId = System.Enum.ToObject(itemIdType, itemIdInt);
                var ctor = itemType.GetConstructor(new System.Type[] { typeof(string), itemIdType });
                var item = ctor?.Invoke(new object[] { "", itemId });
                if (item == null) return 0;

                var result = method.Invoke(shack, new object[] { item });
                return result is int c ? c : 0;
            }
            catch { return 0; }
        }

        private static void ApplyReplenishRateMultiplier(ForagerShack shack, float multiplier)
        {
            try
            {
                // From TendedWilds / ForageableTransplantation:
                // ForageableResource.itemToReplenishRateDict
                // We apply to all ForageableResources within the shack's work area
                var workArea = shack.workArea;
                if (workArea == null) return;

                foreach (var fRes in GameObject.FindObjectsOfType<ForageableResource>())
                {
                    if (!workArea.IsWithinWorkArea(fRes.transform.position)) continue;

                    var dictField = typeof(ForageableResource).GetField(
                        "itemToReplenishRateDict", AllInstance);
                    if (dictField == null) continue;

                    var dict = dictField.GetValue(fRes) as System.Collections.IDictionary;
                    if (dict == null) continue;

                    // Multiply all existing rates
                    var keys = new object[dict.Count];
                    dict.Keys.CopyTo(keys, 0);
                    foreach (var key in keys)
                    {
                        float current = (float)dict[key];
                        dict[key] = current * multiplier;
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning(
                    $"[TW-API] ApplyReplenishRateMultiplier: {ex.Message}");
            }
        }
    }
}
