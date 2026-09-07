using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using RimWorld;
using System.Reflection;

namespace Multiplayer.Compat
{
    /// <summary>Quality Builder by Hatti</summary>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=754637870"/>
    /// <remarks>
    /// The packageId <c>hatti.qualitybuilder</c> is also used by community re-uploads
    /// (for example "QualityBuilder Unofficial 1.6", workshop id 3512466087) whose internals
    /// differ from the original mod. A member that is missing there is a different mod version,
    /// not a broken compat, so every lookup below degrades to a log entry instead of an exception.
    /// </remarks>
    [MpCompatFor("hatti.qualitybuilder")]
    class QualityBuilder
    {
        private const string ModName = "Quality Builder (hatti.qualitybuilder)";

        public QualityBuilder(ModContentPack mod)
        {
            const string builderCompTypeName = "QualityBuilder.CompQualityBuilder";

            var builderCompType = AccessTools.TypeByName(builderCompTypeName);
            if (builderCompType == null)
            {
                LogMissing($"type {builderCompTypeName}");
            }
            else
            {
                var toggleSkilled = AccessTools.DeclaredMethod(builderCompType, "ToggleSkilled");
                if (toggleSkilled == null)
                    LogMissing($"method {builderCompTypeName}:ToggleSkilled");
                else
                    MP.RegisterSyncMethod(toggleSkilled);
            }

            //my decompiler shows the method name is <get_RightClickFloatMenuOptions>b__3_0 
            //while harmony saids it is <get_RightClickFloatMenuOptions>b__2_0, weird..."

            TryRegisterQualityPickingLambda("QualityBuilder.CompQualityBuilder+ToggleCommand+<>c", SyncContext.MapSelected);
            TryRegisterQualityPickingLambda("QualityBuilder._Designator_SkilledBuilder+<>c", SyncContext.None);
        }

        /// <summary>
        /// Registers the "player picked a quality in the right click float menu" lambda declared by
        /// <paramref name="typeName"/>, provided the type and the lambda both exist in the loaded assembly.
        /// </summary>
        private static void TryRegisterQualityPickingLambda(string typeName, SyncContext context)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                LogMissing($"type {typeName}");
                return;
            }

            Type[] argTypes = { typeof(QualityCategory) };
            MethodInfo method = MpMethodUtil.GetFirstMethodBySignature(type, argTypes);
            if (method == null)
            {
                LogMissing($"method taking a single {nameof(QualityCategory)} argument in type {typeName}");
                return;
            }

            MP.RegisterSyncWorker<object>(SyncTypes, type);
            var syncMethod = MP.RegisterSyncMethod(method);
            if (context != SyncContext.None)
                syncMethod.SetContext(context);
        }

        private static void LogMissing(string what)
            => Log.Warning($"MPCompat :: {ModName}: could not find {what}. " +
                           "This is most likely a different version or a re-upload of the mod - " +
                           "skipping this part of the compat, the rest stays active.");

        static void SyncTypes(SyncWorker sync, ref object c)
        {
            if (sync.isWriting)
            {
                sync.Write(c.GetType());
            } else
            {
                c = Activator.CreateInstance(sync.Read<Type>());
            }
        }
    }
}
