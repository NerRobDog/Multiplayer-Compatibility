using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Cleaning Area Relaunched by Cerule</summary>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=3586697885"/>
    [MpCompatFor("Cerule.CleaningAreaRelaunched")]
    public class CleaningAreaRelaunched
    {
        private static Type mapComponentType;

        public CleaningAreaRelaunched(ModContentPack mod)
        {
            mapComponentType = AccessTools.TypeByName("CleaningAreaRelaunched.CleaningAreaRelaunched_MapComponent");

            // Выбор зоны уборки в списке зон (как у оригинального Cleaning Area)
            MP.RegisterSyncMethod(AccessTools.PropertySetter(mapComponentType, "cleanArea"));
            MP.RegisterSyncWorker<MapComponent>(SyncMapComponent, mapComponentType);

            // Дизайнаторы собственной зоны: поля берутся из карты, достаточно создать экземпляр
            foreach (var name in new[] { "Designator_AreaCleanExpand", "Designator_AreaCleanClear" })
            {
                var type = AccessTools.TypeByName("CleaningAreaRelaunched." + name);
                if (type != null)
                    MP.RegisterSyncWorker<Designator>(SyncEmptyDesignator, type, shouldConstruct: true);
            }
        }

        private static void SyncMapComponent(SyncWorker sync, ref MapComponent component)
        {
            if (sync.isWriting)
                sync.Write(component.map);
            else
                component = sync.Read<Map>().GetComponent(mapComponentType);
        }

        private static void SyncEmptyDesignator(SyncWorker sync, ref Designator designator)
        {
        }
    }
}
