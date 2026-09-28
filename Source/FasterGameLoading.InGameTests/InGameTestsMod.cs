using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimTestRedux;
using RimTestRedux.Testing;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 遊戲內測試的進入點。
    /// RimTest Redux 內建的「啟動時執行」在 PlayData 載入完成的那一幀就跑測試，
    /// 但 FGL 的延遲圖形、圖示、圖集與音效協程要之後才逐幀跑完；此時測試會量到半成品狀態。
    /// 因此關掉內建的啟動執行，改由 <see cref="TestRunDriver"/> 等延遲管線跑完後再觸發（不需要地圖，主選單即可）。
    /// </summary>
    public sealed class InGameTestsMod : Mod
    {
        public InGameTestsMod(ModContentPack content) : base(content)
        {
            ApplySeededFglSettings(content);
            // 只改記憶體中的值、不寫回設定檔；本 mod 排在 RimTest Redux 之後載入，其設定此時已建立。
            RimTestReduxMod.Settings.RunAtStartup = false;
            RimTestReduxMod.Settings.RunOwnTests = false;
            new Harmony(content.PackageId).PatchAll(typeof(InGameTestsMod).Assembly);
        }

        /// <summary>
        /// rimworld-mod-mcp 的 seed_config 會把 Mod_*_FasterGameLoadingMod.xml 改名成受測 mod（本 mod）的資料夾，
        /// FGL 因此讀不到。本 mod 排在 FGL 之前建構，趁 FGL 讀設定前把檔案複製回 FGL 的檔名。
        /// </summary>
        private static void ApplySeededFglSettings(ModContentPack content)
        {
            var seeded = LoadedModManager.GetSettingsFilename(content.FolderName, nameof(FasterGameLoadingMod));
            if (!File.Exists(seeded)) return;

            var fgl = LoadedModManager.RunningMods.FirstOrDefault(static m => string.Equals(m.PackageIdPlayerFacing, "Taranchuk.FasterGameLoading", StringComparison.OrdinalIgnoreCase));
            if (fgl == null)
            {
                Log.Error("[FGL InGameTests] Seeded FGL settings found but Taranchuk.FasterGameLoading is not running; settings not applied.");
                return;
            }
            File.Copy(seeded, LoadedModManager.GetSettingsFilename(fgl.FolderName, nameof(FasterGameLoadingMod)), overwrite: true);
            Log.Message($"[FGL InGameTests] Applied seeded FGL settings from {Path.GetFileName(seeded)}");
        }
    }

    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class TestRunDriver
    {
        /// <summary>等待延遲管線的上限；逾時仍會執行測試，讓未完成的狀態以測試失敗呈現。</summary>
        private const double TimeoutSeconds = 300;

        private static bool ran;
        private static Stopwatch waitingSince;

        public static void Postfix()
        {
            if (ran || !PlayDataLoader.Loaded || LongEventHandler.AnyEventNowOrWaiting || Find.UIRoot == null)
            {
                return;
            }
            waitingSince ??= Stopwatch.StartNew();

            bool timedOut = waitingSince.Elapsed.TotalSeconds >= TimeoutSeconds;
            if (!timedOut && !FglState.DeferredPipelineFinished)
            {
                return;
            }
            ran = true;

            if (timedOut)
            {
                Log.Error($"[FGL InGameTests] Timed out after {TimeoutSeconds}s waiting for FGL's deferred pipeline; running tests anyway.");
            }
            Log.Message($"[FGL InGameTests] Running suites after {waitingSince.Elapsed.TotalSeconds:F1}s wait. Settings: {FglState.SettingsProfile}");
            Runner.RunAllRegisteredTests();
            StatusExplorer.UpdateAllStatusCounts();
            Viewer.LogTestsResults();
        }
    }
}
