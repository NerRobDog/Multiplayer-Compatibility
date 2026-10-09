using System;
using System.Collections;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Some mods change the DefDatabase when the first Game is created (Android Tiers adds WorkGivers and removes a recipe,
    /// Achtung! adds a WorkTypeDef). The host starts its server after loading a save, so MP gives clients its post-change DefInfos,
    /// while a client sitting in the main menu still has the pre-change ones, and MP reports "Count_Diff" for those Def types.
    /// The client replays the same changes after joining, so both sides end up identical; only the join-time comparison is wrong.
    /// Fix: remember the DefInfos as they were before the first Game exists and hand that snapshot out instead.
    /// </summary>
    static class DefInfoSnapshot
    {
        internal static IDictionary snapshot;
    }

    [HarmonyPatch(typeof(Game), MethodType.Constructor)]
    static class DefInfoSnapshot_Remember
    {
        // Game..ctor runs before game components (and anything they change) exist, so the dictionary is still the menu-state one
        [HarmonyPrefix]
        static void RememberDefInfos()
        {
            if (DefInfoSnapshot.snapshot != null) return;

            try
            {
                var current = AccessTools.Field("Multiplayer.Client.MultiplayerData:localDefInfos")?.GetValue(null) as IDictionary;
                if (current == null || current.Count == 0) return;

                DefInfoSnapshot.snapshot = (IDictionary)Activator.CreateInstance(current.GetType(), current);
            }
            catch (Exception e)
            {
                Log.Warning($"MPCompat :: DefInfo snapshot failed: {e.Message}");
            }
        }

    }

    [HarmonyPatch]
    static class DefInfoSnapshot_Use
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method("Multiplayer.Client.ClientJoiningState:PackInitData");

        [HarmonyPostfix]
        static void UseSnapshot(object __result)
        {
            var snapshot = DefInfoSnapshot.snapshot;
            if (snapshot == null || __result == null) return;

            try
            {
                // ServerInitData is an immutable record, DefInfos is init-only: write the backing field
                AccessTools.Field(__result.GetType(), "<DefInfos>k__BackingField").SetValue(__result, snapshot);
            }
            catch (Exception e)
            {
                Log.Warning($"MPCompat :: DefInfo snapshot not applied: {e.Message}");
            }
        }
    }
}
