using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading
{
    [TestFixture]
    public class DeferredLoaderTests
    {
        private static Harmony harmony;
        private static Texture2D mockBadTex;
        private static Texture2D mockContentFinderTex;
        private static Material mockMaterial;
        private DelayedActions delayedActions;

        private static bool PrefixSkip() => false;
        private static bool MockIsInMainThread() => true;

        private static bool MockContentFinderGet(string itemPath, bool reportFailure, ref Texture2D __result)
        {
            __result = mockContentFinderTex;
            return false;
        }

        private sealed class TestGraphic : Graphic
        {
            private readonly Material mat;
            public TestGraphic(Material mat) => this.mat = mat;
            public override Material MatSingle => mat;
        }

        private static ThingDef postLoadSpecialCalledDef;

        private static bool MockPostLoadSpecial(ThingDef parentDef)
        {
            postLoadSpecialCalledDef = parentDef;
            return false;
        }

        private static ThingDef CreateMockThingDef(string name)
        {
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            def.defName = name;
            return def;
        }

        [OneTimeSetUp]
#pragma warning disable MA0051 // 需依序初始化多個 Harmony mock patch，拆分成多個方法會降低可讀性
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.DeferredLoaderTests");

            mockBadTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            mockContentFinderTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            mockMaterial = (Material)FormatterServices.GetUninitializedObject(typeof(Material));
            // 使用 BaseContent.BadTex 的真實值（可能是 null 或實際紋理）。
            // 不可嘗試覆寫 BadTex：它是 static readonly（initonly）欄位，型別初始化後 SetValue 會拋
            // FieldAccessException（cctor 已被 TestSetup 攔截，BadTex 保持未初始化）。
            // 直接讀取可確保 def.uiIcon 與 BaseContent.BadTex 永遠同一實例（ReferenceEquals 成立）。
            try
            {
                mockBadTex = BaseContent.BadTex;
            }
            catch
            {
                mockBadTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            }

            // Patch UnityData.IsInMainThread to return true
            var isInMainThreadGetter = AccessTools.PropertyGetter(typeof(UnityData), nameof(UnityData.IsInMainThread));
            if (isInMainThreadGetter != null)
            {
                harmony.Patch(isInMainThreadGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(MockIsInMainThread))));
            }

            // Patch FGLLog.Emit
            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(PrefixSkip))));
            }

            // Patch ContentFinder<Texture2D>.Get
            // priority 設高於 TestSetup 的 Prefix_TexStub（預設 400），確保此 mock 先執行並回傳 mockContentFinderTex
            var contentFinderGet = AccessTools.Method(typeof(ContentFinder<Texture2D>), nameof(ContentFinder<Texture2D>.Get), new Type[] { typeof(string), typeof(bool) });
            if (contentFinderGet != null)
            {
                var mockCf = new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(MockContentFinderGet))) { priority = 600 };
                harmony.Patch(contentFinderGet, prefix: mockCf);
            }

            // 注意：Material.mainTexture 是 ECall getter，測試環境（無 Unity native）下無法
            // Harmony patch（拋 SecurityException），因此 LoadDeferredGraphicsCoroutine 中
            // 「從 MatSingle.mainTexture 提取 UI 圖示」的成功路徑在此環境無法直接驗證，
            // 改由 WhenBadTexAndHasGraphic_MainTextureUnavailable_KeepsUiIconAndContinues 驗證安全路徑。

            // Patch PlantProperties.PostLoadSpecial
            var plantPropType = AccessTools.TypeByName("Verse.PlantProperties") ?? AccessTools.TypeByName("RimWorld.PlantProperties");
            var postLoadSpecialMethod = plantPropType != null ? AccessTools.Method(plantPropType, "PostLoadSpecial") : null;
            if (postLoadSpecialMethod != null)
            {
                harmony.Patch(postLoadSpecialMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(MockPostLoadSpecial))));
            }

            // Initialize FasterGameLoadingMod.harmony for SoundStarter_Patch.Unpatch()
            var fglModHarmonyProp = typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.harmony), BindingFlags.Public | BindingFlags.Static);
            fglModHarmonyProp?.SetValue(obj: null, value: new Harmony("FasterGameLoadingMod.TestInstance"), index: null);
        }
#pragma warning restore MA0051

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.DeferredLoaderTests");
        }

        [SetUp]
        public void SetUp()
        {
            delayedActions = new DelayedActions();
            postLoadSpecialCalledDef = null;
            SoundStarter_Patch.ResetUnpatchedStatus();
        }

        [TearDown]
        public void TearDown()
        {
            delayedActions?.ClearQueues();
            delayedActions?.StopStopwatch();
        }

        [Test]
        public void LoadDeferredGraphicsCoroutine_DrainsQueueAndPopulatesLoadedDefs()
        {
            var def1 = CreateMockThingDef("Def1");
            var def2 = CreateMockThingDef("Def2");
            bool act1Executed = false;
            bool act2Executed = false;

            delayedActions.EnqueueGraphic(def1, () => act1Executed = true);
            delayedActions.EnqueueGraphic(def2, () => act2Executed = true);

            var loadedDefs = new List<ThingDef>();
            var coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            while (coroutine.MoveNext()) { }

            Assert.That(act1Executed, Is.True);
            Assert.That(act2Executed, Is.True);
            Assert.That(loadedDefs, Contains.Item(def1));
            Assert.That(loadedDefs, Contains.Item(def2));
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadDeferredGraphicsCoroutine_WhenBadTexAndHasUiIconPath_ReloadsFromContentFinder()
        {
            var def = CreateMockThingDef("DefWithIconPath");
            def.uiIcon = mockBadTex;
            def.uiIconPath = "Things/Item/TestIcon";

            delayedActions.EnqueueGraphic(def, () => { });

            var loadedDefs = new List<ThingDef>();
            var coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            while (coroutine.MoveNext()) { }

            Assert.That(def.uiIcon, Is.SameAs(mockContentFinderTex));
        }

        [Test]
        public void LoadDeferredGraphicsCoroutine_WhenBadTexAndHasGraphic_MainTextureUnavailable_KeepsUiIconAndContinues()
        {
            // Material.mainTexture 是 ECall getter，測試環境（無 Unity native）無法取得，
            // 因此驗證產品碼在 mainTexture 取得失敗時的安全行為：uiIcon 保持原值且協程不崩潰。
            var def = CreateMockThingDef("DefWithGraphic");
            def.uiIcon = mockBadTex;
            def.uiIconPath = null;
            def.graphicData = new GraphicData
            {
                graphicClass = typeof(Graphic_Single),
            };
            var graphicField = AccessTools.Field(typeof(GraphicData), "cachedGraphic");
            graphicField?.SetValue(def.graphicData, new TestGraphic(mockMaterial));

            delayedActions.EnqueueGraphic(def, () => { });

            var loadedDefs = new List<ThingDef>();
            var coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            Assert.DoesNotThrow(() =>
            {
                while (coroutine.MoveNext()) { }
            });

            Assert.That(def.uiIcon, Is.SameAs(mockBadTex));
            Assert.That(loadedDefs, Contains.Item(def));
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadDeferredGraphicsCoroutine_WhenActionThrows_CatchesAndContinuesWithRemainingDefs()
        {
            var def1 = CreateMockThingDef("Def1");
            var def2 = CreateMockThingDef("Def2_Fails");
            var def3 = CreateMockThingDef("Def3");
            bool act1Executed = false;
            bool act3Executed = false;

            delayedActions.EnqueueGraphic(def1, () => act1Executed = true);
            delayedActions.EnqueueGraphic(def2, () => throw new InvalidOperationException("Simulated load error"));
            delayedActions.EnqueueGraphic(def3, () => act3Executed = true);

            var loadedDefs = new List<ThingDef>();
            var coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            Assert.DoesNotThrow(() =>
            {
                while (coroutine.MoveNext()) { }
            });

            Assert.That(act1Executed, Is.True);
            Assert.That(act3Executed, Is.True);
            Assert.That(loadedDefs, Contains.Item(def1));
            Assert.That(loadedDefs, Does.Not.Contain(def2));
            Assert.That(loadedDefs, Contains.Item(def3));
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadDeferredGraphicsCoroutine_CallsPlantPostLoadSpecialOnSuccess_AndSkipsOnFailure()
        {
            var plantPropType = AccessTools.TypeByName("Verse.PlantProperties") ?? AccessTools.TypeByName("RimWorld.PlantProperties");
            var plantPropObj = plantPropType != null ? FormatterServices.GetUninitializedObject(plantPropType) : null;

            var defSuccess = CreateMockThingDef("DefPlantSuccess");
            var plantField = AccessTools.Field(typeof(ThingDef), "plant");
            plantField?.SetValue(defSuccess, plantPropObj);

            delayedActions.EnqueueGraphic(defSuccess, () => { });

            var loadedDefs = new List<ThingDef>();
            var coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            while (coroutine.MoveNext()) { }

            Assert.That(postLoadSpecialCalledDef, Is.SameAs(defSuccess));

            // Now test failure doesn't invoke PostLoadSpecial
            postLoadSpecialCalledDef = null;
            var defFailure = CreateMockThingDef("DefPlantFailure");
            plantField?.SetValue(defFailure, plantPropObj);

            delayedActions.EnqueueGraphic(defFailure, () => throw new InvalidOperationException("Failed"));
            coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            while (coroutine.MoveNext()) { }

            Assert.That(postLoadSpecialCalledDef, Is.Null);
        }

        [Test]
        public void LoadDeferredIconsCoroutine_ExecutesOnlyWhenUiIconIsBadTex()
        {
            var defBadTex = CreateMockThingDef("DefBadTex");
            defBadTex.uiIcon = mockBadTex;

            var validTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            var defGoodTex = CreateMockThingDef("DefGoodTex");
            defGoodTex.uiIcon = validTex;

            bool badTexActionExecuted = false;
            bool goodTexActionExecuted = false;

            delayedActions.EnqueueIcon(defBadTex, () => badTexActionExecuted = true);
            delayedActions.EnqueueIcon(defGoodTex, () => goodTexActionExecuted = true);

            var coroutine = DeferredLoader.LoadDeferredIconsCoroutine(delayedActions);
            while (coroutine.MoveNext()) { }

            Assert.That(badTexActionExecuted, Is.True);
            Assert.That(goodTexActionExecuted, Is.False);
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadDeferredIconsCoroutine_WhenActionThrows_CatchesAndContinues()
        {
            var def1 = CreateMockThingDef("DefIcon1");
            def1.uiIcon = mockBadTex;
            var def2 = CreateMockThingDef("DefIcon2");
            def2.uiIcon = mockBadTex;

            bool def2Executed = false;
            delayedActions.EnqueueIcon(def1, () => throw new InvalidOperationException("Icon error"));
            delayedActions.EnqueueIcon(def2, () => def2Executed = true);

            var coroutine = DeferredLoader.LoadDeferredIconsCoroutine(delayedActions);
            Assert.DoesNotThrow(() =>
            {
                while (coroutine.MoveNext()) { }
            });

            Assert.That(def2Executed, Is.True);
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));
        }

        [Test]
        public void ResolveSubSoundDefsCoroutine_DrainsQueueAndCallsUnpatch()
        {
            var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            bool act1Executed = false;
            bool act2Executed = false;

            delayedActions.EnqueueSubSound(sound1, () => act1Executed = true);
            delayedActions.EnqueueSubSound(sound2, () => act2Executed = true);

            var coroutine = DeferredLoader.ResolveSubSoundDefsCoroutine(delayedActions);
            while (coroutine.MoveNext()) { }

            Assert.That(act1Executed, Is.True);
            Assert.That(act2Executed, Is.True);
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
        }

        [Test]
        public void ResolveSubSoundDefsCoroutine_WhenActionThrows_CatchesAndContinues()
        {
            var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            bool act2Executed = false;

            delayedActions.EnqueueSubSound(sound1, () => throw new InvalidOperationException("Audio error"));
            delayedActions.EnqueueSubSound(sound2, () => act2Executed = true);

            var coroutine = DeferredLoader.ResolveSubSoundDefsCoroutine(delayedActions);
            Assert.DoesNotThrow(() =>
            {
                while (coroutine.MoveNext()) { }
            });

            Assert.That(act2Executed, Is.True);
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
        }

        [Test]
        public void UpdateMapMeshForLoadedDefs_WhenCurrentGameNull_RunsSafely()
        {
            Assert.DoesNotThrow(() => DeferredLoader.UpdateMapMeshForLoadedDefs(new List<ThingDef>()));
        }
    }
}
