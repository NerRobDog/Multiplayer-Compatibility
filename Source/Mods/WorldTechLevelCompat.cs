using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>World Tech Level by m00nl1ght</summary>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=3414187030"/>
    [MpCompatFor("m00nl1ght.worldtechlevel")]
    public class WorldTechLevelCompat
    {
        private const string ComponentTypeName = "WorldTechLevel.GameComponent_TechLevel";
        private const string LevelProperty = "WorldTechLevel";

        public WorldTechLevelCompat(ModContentPack mod) => LongEventHandler.ExecuteWhenFinished(LatePatch);

        private static void LatePatch()
        {
            // Lunar грузит сборку мода позже старта, поэтому патчим после загрузки.
            var componentType = AccessTools.TypeByName(ComponentTypeName);
            var setter = AccessTools.PropertySetter(componentType, LevelProperty);

            // Ползунок на вкладке «Планета» (Patch_WITab_Planet) присваивает свойство прямо в UI:
            // без синка у каждого игрока свой уровень, и фракции тянутся к разным уровням.
            MpCompat.harmony.Patch(setter, prefix: new HarmonyMethod(typeof(WorldTechLevelCompat), nameof(PreSetLevel)));
            MP.RegisterSyncMethod(typeof(WorldTechLevelCompat), nameof(SyncedSetLevel));
        }

        private static bool PreSetLevel(TechLevel value)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            SyncedSetLevel(value);
            return false;
        }

        private static void SyncedSetLevel(TechLevel level)
        {
            var component = Current.Game.components.FirstOrDefault(c => c.GetType().FullName == ComponentTypeName);
            if (component == null)
                return;

            // IsExecutingSyncCommand: префикс пропустит присваивание
            AccessTools.PropertySetter(component.GetType(), LevelProperty).Invoke(component, new object[] { level });
        }
    }
}
