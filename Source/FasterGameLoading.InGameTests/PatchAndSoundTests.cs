using System.Collections.Generic;
using HarmonyLib;
using RimTestRedux;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 在真實執行環境確認 Harmony patch 依設定正確套用／解除。
    /// 單元測試只能驗證轉譯器產生的 IL，無法確認 PatchAll 與 Prepare() 在實際啟動時的結果。
    /// </summary>
    [TestSuite]
    internal static class PatchApplicationTests
    {
        [Test]
        public static void AlwaysOnPatchesAreApplied()
        {
            var failures = new List<string>();
            Check(failures, AccessTools.Method(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll)), expected: true);
            Check(failures, AccessTools.Method(typeof(ModContentPack), nameof(ModContentPack.ReloadContentInt)), expected: true);
            Check(failures, AccessTools.Method(typeof(ModAssetBundlesHandler), nameof(ModAssetBundlesHandler.ReloadAll)), expected: true);
            Check(failures, AccessTools.Method(typeof(World), nameof(World.FinalizeInit)), expected: true);
            Check(failures, AccessTools.Method(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.BakeStaticAtlases)), expected: true);
            Check(failures, AccessTools.Method(typeof(SubSoundDef), nameof(SubSoundDef.ResolveReferences)), expected: true);
            FglState.AssertNone(failures, "patch state mismatches");
        }

        [Test]
        public static void TypeLookupCachePatchFollowsSetting()
        {
            var failures = new List<string>();
            Check(failures, AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssemblyInt)), FasterGameLoadingSettings.TypeLookupCache);
            FglState.AssertNone(failures, "patch state mismatches");
        }

        [Test]
        public static void DelayGraphicPatchesFollowSetting()
        {
            bool expected = FasterGameLoadingSettings.DelayGraphicLoading;
            var failures = new List<string>();
            Check(failures, AccessTools.Method(typeof(ThingDef), nameof(ThingDef.PostLoad)), expected);
            Check(failures, AccessTools.Method(typeof(BuildableDef), nameof(BuildableDef.PostLoad)), expected);
            Check(failures, AccessTools.Method(typeof(GraphicData), nameof(GraphicData.Init)), expected);
            FglState.AssertNone(failures, "patch state mismatches");
        }

        /// <summary>音效解析完成後，SoundStarter 類別的攔截必須全部解除，否則整個 session 沒有聲音。</summary>
        [Test]
        public static void SoundStarterInterceptionIsRemoved()
        {
            var failures = new List<string>();
            Check(failures, AccessTools.Method(typeof(SoundStarter), nameof(SoundStarter.PlayOneShotOnCamera)), expected: false);
            Check(failures, AccessTools.Method(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot)), expected: false);
            Check(failures, AccessTools.Method(typeof(SoundStarter), nameof(SoundStarter.TrySpawnSustainer)), expected: false);
            Check(failures, AccessTools.Method(typeof(SubSoundDef), nameof(SubSoundDef.TryPlay)), expected: false);
            FglState.AssertNone(failures, "patch state mismatches");
        }

        private static void Check(List<string> failures, System.Reflection.MethodBase method, bool expected)
        {
            if (method == null)
            {
                failures.Add("target method not found (game API changed?)");
                return;
            }
            if (FglState.HasFglPatch(method) != expected)
            {
                failures.Add($"{method.DeclaringType?.Name}.{method.Name} expected {(expected ? "patched" : "unpatched")}");
            }
        }
    }

    /// <summary>延遲的 SubSoundDef 解析是否真的全部完成。</summary>
    [TestSuite]
    internal static class DeferredSoundTests
    {
        [Test]
        public static void SubSoundQueueIsDrained()
        {
            Assert.That(FasterGameLoadingMod.delayedActions.SubSoundDefToResolveCount).Is.EqualTo(0);
        }

        /// <summary>
        /// 原版在回呼中把 grains 展開成 resolvedGrains；延遲後漏跑的 SubSoundDef 會保持空清單而無聲。
        /// 只檢查 grains 能解析出實體音檔的項目，缺檔屬於內容問題，原版同樣會報錯。
        /// </summary>
        [Test]
        public static void EverySubSoundWithGrainsIsResolved()
        {
            var failures = new List<string>();
            foreach (var soundDef in DefDatabase<SoundDef>.AllDefsListForReading)
            {
                if (soundDef.subSounds == null) continue;
                foreach (var sub in soundDef.subSounds)
                {
                    if (sub.resolvedGrains.Count > 0 || sub.grains.NullOrEmpty()) continue;
                    if (!AnyGrainResolvable(sub)) continue;
                    failures.Add($"{soundDef.defName}/{sub}");
                }
            }
            FglState.AssertNone(failures, "SubSoundDefs left unresolved");
        }

        private static bool AnyGrainResolvable(SubSoundDef sub)
        {
            foreach (var grain in sub.grains)
            {
                foreach (var _ in grain.GetResolvedGrains())
                {
                    return true;
                }
            }
            return false;
        }
    }
}
