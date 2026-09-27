using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 攔截 ThingDef.PostLoad 中的 LongEventHandler.ExecuteWhenFinished 呼叫，
    /// 將非必要圖形的載入延遲到遊戲進入後執行。
    /// </summary>
    [HarmonyPatch(typeof(ThingDef), "PostLoad")]
    public static class ThingDef_PostLoad_Patch
    {
        /// <summary>
        /// 決定是否啟用此補丁（當延遲圖形載入設定開啟時）。
        /// </summary>
        public static bool Prepare() => FasterGameLoadingSettings.DelayGraphicLoading;

        /// <summary>
        /// 轉譯器：攔截並修改 ThingDef.PostLoad 中調用 ExecuteWhenFinished 的 IL 代碼，
        /// 將其導向我們自訂的延遲執行邏輯。
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codeInstructions)
        {
            var execute = AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished));
            var executeDelayed = AccessTools.Method(typeof(ThingDef_PostLoad_Patch), nameof(ExecuteDelayed));
            foreach (var code in codeInstructions)
            {
                if (code.Calls(execute))
                {
                    // 將 ExecuteWhenFinished(action) 替換為 ExecuteDelayed(action, this)
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, executeDelayed);
                }
                else
                {
                    yield return code;
                }
            }
        }

        /// <summary>
        /// 仍在原本的時機以 ExecuteWhenFinished 排程，等回呼執行時才以 ShouldBeLoadedImmediately
        /// 決定立即載入或排入延遲佇列：PostLoad 當下 Def 參照（designationCategory、thingCategories、
        /// orderedTakeGroup）尚未解析，這時判斷會讓家具等分類條件永遠不成立。
        /// </summary>
        public static void ExecuteDelayed(Action action, ThingDef def)
        {
            LongEventHandler.ExecuteWhenFinished(() => LoadNowOrDefer(action, def));
        }

        private static void LoadNowOrDefer(Action action, ThingDef def)
        {
            var delayedActions = FasterGameLoadingMod.delayedActions;
            if (delayedActions == null || def.ShouldBeLoadedImmediately())
            {
                action();
                return;
            }
            delayedActions.EnqueueGraphic(def, action);
        }
    }
}
