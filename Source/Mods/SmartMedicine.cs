using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Smart Medicine by Uuugggg, Compact Hediffs by PeteTimesSix</summary>
    /// SmartMedicine:
    /// <see href="https://github.com/alextd/Rimworld-SmartMedicine"/>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=1309994319"/>
    /// CompactHediffs:
    /// <see href="https://github.com/PeteTimesSix/CompactHediffs"/>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=2031734067"/>
    [MpCompatFor("Uuugggg.SmartMedicine")]
    [MpCompatFor("Memegoddess.SmartMedicine")] // Smart Medicine - Continued, те же типы
    public class SmartMedicine
    {
        private delegate void StockUpPasteSettingsDelegate(Pawn pawn);

        private delegate Dictionary<ThingDef, int> StockUpSettingsDelegate(Pawn pawn);

        private static Type smartMedicineCompType;
        private static StockUpSettingsDelegate stockUpSettingsMethod;
        private static StockUpPasteSettingsDelegate stockUpPasteMethod;
        private static AccessTools.FieldRef<object, Pawn> copiedPawnField;

        // CompatcHediffs compat
        private static FastInvokeHandler getSmartMedicinePriorityCareDictionary;
        private static AccessTools.FieldRef<object, object> delegateTypeHediffCareField;

        public SmartMedicine(ModContentPack mod)
        {
            // Stock up medicine/drugs
            {
                var type = AccessTools.TypeByName("SmartMedicine.StockUpUtility");

                var pasteMethod = AccessTools.Method(type, "StockUpPasteSettings");

                stockUpPasteMethod = AccessTools.MethodDelegate<StockUpPasteSettingsDelegate>(pasteMethod);
                stockUpSettingsMethod = AccessTools.MethodDelegate<StockUpSettingsDelegate>(AccessTools.Method(type, "StockUpSettings"));

                MpCompat.harmony.Patch(pasteMethod,
                    prefix: new HarmonyMethod(typeof(SmartMedicine), nameof(PrePasteSettings)));
                MpCompat.harmony.Patch(AccessTools.Method(type, "SetStockCount", new[] { typeof(Pawn), typeof(ThingDef), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(SmartMedicine), nameof(PreSetStockCount)));

                // Mod methods to sync
                MP.RegisterSyncMethod(AccessTools.Method(type, "StockUpStop", new[] { typeof(Pawn), typeof(ThingDef) }));
                MP.RegisterSyncMethod(AccessTools.Method(type, "StockUpClearSettings"));
                // Our methods to sync
                MP.RegisterSyncMethod(typeof(SmartMedicine), nameof(SyncedSetStockCount));
                MP.RegisterSyncMethod(typeof(SmartMedicine), nameof(SyncedPasteSettings));

                // We'll need the access to copiedPawn field to modify it when pasting
                smartMedicineCompType = AccessTools.TypeByName("SmartMedicine.SmartMedicineGameComp");
                copiedPawnField = AccessTools.FieldRefAccess<Pawn>(smartMedicineCompType, "copiedPawn");
            }

            // Set wound target tend quality
            {
                var type = AccessTools.TypeByName("SmartMedicine.HediffRowPriorityCare");

                if (AccessTools.Method(type, "LabelButton") != null)
                    MpCompat.RegisterLambdaDelegate(type, "LabelButton", 0, 1);
                else
                {
                    // Smart Medicine - Continued: меню ухода строится в CreateCareMenuOptionsWithList (семь пунктов), без LabelButton.
                    // Подменяем действия пунктов на синхронные методы.
                    var settingsType = AccessTools.TypeByName("SmartMedicine.PriorityCareSettingsComp");
                    careGet = MethodInvoker.GetHandler(AccessTools.Method(settingsType, "Get"));
                    careGetIgnore = MethodInvoker.GetHandler(AccessTools.Method(settingsType, "GetIgnore"));
                    MpCompat.harmony.Patch(AccessTools.Method(type, "CreateCareMenuOptionsWithList"),
                        postfix: new HarmonyMethod(typeof(SmartMedicine), nameof(PostCreateCareMenuOptions)));
                    MP.RegisterSyncMethod(typeof(SmartMedicine), nameof(SyncedToggleIgnore));
                    MP.RegisterSyncMethod(typeof(SmartMedicine), nameof(SyncedDefaultCare));
                    MP.RegisterSyncMethod(typeof(SmartMedicine), nameof(SyncedDefaultCareAll));
                    MP.RegisterSyncMethod(typeof(SmartMedicine), nameof(SyncedSetCare));
                }

                // CompatcHediffs compat
                type = AccessTools.TypeByName("PeteTimesSix.CompactHediffs.Rimworld.UI_compat.UI_SmartMedicine");
                if (type != null && AccessTools.Method(AccessTools.TypeByName("SmartMedicine.HediffRowPriorityCare"), "LabelButton") == null)
                {
                    // Smart Medicine - Continued: меню Compact Hediffs подменяем своим на синхронных методах
                    var addButton = AccessTools.Method(type, "AddSmartMedicineFloatMenuButton");
                    if (addButton != null)
                        MpCompat.harmony.Patch(addButton, prefix: new HarmonyMethod(typeof(SmartMedicine), nameof(PreCompactCareButton)));
                }
                else if (type != null)
                {
                    var delegateMethod = MpMethodUtil.GetLambda(type, "AddSmartMedicineFloatMenuButton", lambdaOrdinal: 1);
                    var delegateType = delegateMethod.DeclaringType;

                    MP.RegisterSyncDelegate(type, delegateType!.Name, delegateMethod.Name, new[] { "hediffs" });
                    MpCompat.RegisterLambdaDelegate(type, "AddSmartMedicineFloatMenuButton", new[] { "CS$<>8__locals1/hediffs", "mc" }, 2);

                    delegateTypeHediffCareField = AccessTools.FieldRefAccess<object>(delegateType, "hediffCares");
                    MpCompat.harmony.Patch(AccessTools.Constructor(delegateType),
                        prefix: new HarmonyMethod(typeof(SmartMedicine), nameof(InitHediffCareDictionary)));

                    type = AccessTools.TypeByName("SmartMedicine.PriorityCareComp");
                    getSmartMedicinePriorityCareDictionary = MethodInvoker.GetHandler(AccessTools.Method(type, "Get"));
                }
            }

            // Patched sync methods
            {
                // When ordering a pawn to drop something, it'll try to stop them from stocking up on it.
                // Will cause desyncs if this happened to be something they were stocking up
                // if the pawn decides to unload the things they were stocking up on.
                // Used for both vanilla and RPG style inventory InterfaceDrop method.
                PatchingUtilities.PatchCancelInInterface("SmartMedicine.InterfaceDrop_Patch:Postfix");
            }
        }

        private static FastInvokeHandler careGet, careGetIgnore;
        private static readonly FieldInfo floatMenuAction = AccessTools.Field(typeof(FloatMenuOption), "action");

        private static void PostCreateCareMenuOptions(List<Hediff> affectedHediffs, List<FloatMenuOption> __result)
        {
            // 0 — отдых в постели (игнор), 1 — уход по умолчанию, 2..6 — категории ухода
            if (affectedHediffs.NullOrEmpty() || __result.Count != 7)
                return;

            var affected = new List<Hediff>(affectedHediffs);
            floatMenuAction.SetValue(__result[0], (Action)(() => SyncedToggleIgnore(affected)));
            floatMenuAction.SetValue(__result[1], (Action)(() => SyncedDefaultCare(affected[0])));
            for (var i = 0; i < 5; i++)
            {
                var category = i;
                floatMenuAction.SetValue(__result[2 + i], (Action)(() => SyncedSetCare(affected, category)));
            }
        }

        private static readonly AccessTools.FieldRef<Texture2D[]> careTexturesRef =
            AccessTools.StaticFieldRefAccess<Texture2D[]>(AccessTools.Field(typeof(MedicalCareUtility), "careTextures"));

        // Копия AddSmartMedicineFloatMenuButton из Compact Hediffs, но действия идут через синхронные методы.
        private static bool PreCompactCareButton(Rect buttonRect, IEnumerable<Hediff> hediffs, MedicalCareCategory defaultCare)
        {
            if (!MP.IsInMultiplayer)
                return true;

            if (Event.current.button != 1 || !Widgets.ButtonInvisible(buttonRect, true) || !hediffs.Any(h => h.TendableNow(true)))
                return false;

            var affected = new List<Hediff>(hediffs);
            var textures = careTexturesRef();
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("TD.DefaultCare".Translate(), () => SyncedDefaultCareAll(affected), textures[(int)defaultCare], Color.white)
            };
            for (var i = 0; i < 5; i++)
            {
                var category = i;
                options.Add(new FloatMenuOption(MedicalCareUtility.GetLabel((MedicalCareCategory)category),
                    () => SyncedSetCare(affected, category), textures[category], Color.white));
            }

            Find.WindowStack.Add(new FloatMenu(options));
            return false;
        }

        private static void SyncedDefaultCareAll(List<Hediff> affected)
        {
            var dict = (Dictionary<Hediff, MedicalCareCategory>)careGet(null);
            foreach (var hediff in affected)
                dict.Remove(hediff);
        }

        private static void SyncedToggleIgnore(List<Hediff> affected)
        {
            var set = (HashSet<Hediff>)careGetIgnore(null);
            if (!set.Add(affected[0]))
                set.RemoveWhere(x => affected.Contains(x));
            else
                set.AddRange(affected);
        }

        private static void SyncedDefaultCare(Hediff primary)
            => ((Dictionary<Hediff, MedicalCareCategory>)careGet(null)).Remove(primary);

        private static void SyncedSetCare(List<Hediff> affected, int category)
        {
            var dict = (Dictionary<Hediff, MedicalCareCategory>)careGet(null);
            foreach (var hediff in affected)
                dict[hediff] = (MedicalCareCategory)category;
        }

        private static bool PreSetStockCount(Pawn pawn, ThingDef thingDef, int count)
        {
            if (!MP.IsInMultiplayer)
                return true;

            var dict = stockUpSettingsMethod(pawn);

            // Make sure there's an actual change here, or else it'll end up spamming the sync method
            // That's the main reason why this method exists - if we only sync original one, it'll end up spamming calls
            if (!dict.TryGetValue(thingDef, out var current) || count != current)
                SyncedSetStockCount(pawn, thingDef, count);

            return false;
        }

        private static void SyncedSetStockCount(Pawn pawn, ThingDef thingDef, int count)
        {
            var dict = stockUpSettingsMethod(pawn);
            dict[thingDef] = count;
        }

        private static bool PrePasteSettings(Pawn pawn)
        {
            if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand)
            {
                // Get the pawn to copy, and "share" it with everyone to sync up
                var comp = Current.Game.GetComponent(smartMedicineCompType);
                var copiedPawn = copiedPawnField(comp);
                SyncedPasteSettings(pawn, copiedPawn);
                return false;
            }

            return true;
        }

        private static void SyncedPasteSettings(Pawn pawn, Pawn copiedPawn)
        {
            var comp = Current.Game.GetComponent(smartMedicineCompType);
            // Get original copied pawn (or null if none) to restore later, and replace it with the synced one for now
            var originalPawn = copiedPawnField(comp);
            copiedPawnField(comp) = copiedPawn;

            // Call the actual method, and make sure it's not cancelled/redirected here
            stockUpPasteMethod(pawn);

            // Restore the original pawn, so if anyone else was copying then they'll be free to do so without the
            // pawn they selected being overriden
            copiedPawnField(comp) = originalPawn;
        }

        // CompactHeddifs compat
        private static void InitHediffCareDictionary(object __instance)
            => delegateTypeHediffCareField(__instance) = getSmartMedicinePriorityCareDictionary(null);
    }
}