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
        private static Texture2D mockMaterialTex;
        private static Material mockMaterial;
        private DelayedActions delayedActions;

        private static bool PrefixSkip() => false;
        private static bool MockIsInMainThread() => true;

        private static bool MockContentFinderGet(string itemPath, bool reportFailure, ref Texture2D __result)
        {
            __result = mockContentFinderTex;
            return false;
        }

        private static bool MockGetMainTexture(ref Texture __result)
        {
            __result = mockMaterialTex;
            return false;
        }

        private class TestGraphic : Graphic
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
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.DeferredLoaderTests");

            mockBadTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            mockContentFinderTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            mockMaterialTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            mockMaterial = (Material)FormatterServices.GetUninitializedObject(typeof(Material));

            // Set BaseContent.BadTex
            var badTexField = AccessTools.Field(typeof(BaseContent), nameof(BaseContent.BadTex));
            badTexField?.SetValue(null, mockBadTex);

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
            var contentFinderGet = AccessTools.Method(typeof(ContentFinder<Texture2D>), nameof(ContentFinder<Texture2D>.Get), new Type[] { typeof(string), typeof(bool) });
            if (contentFinderGet != null)
            {
                harmony.Patch(contentFinderGet, prefix: new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(MockContentFinderGet))));
            }

            // Patch Material.mainTexture
            var getMainTexture = AccessTools.PropertyGetter(typeof(Material), "mainTexture");
            if (getMainTexture != null)
            {
                harmony.Patch(getMainTexture, prefix: new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(MockGetMainTexture))));
            }

            // Patch PlantProperties.PostLoadSpecial
            var plantPropType = AccessTools.TypeByName("Verse.PlantProperties") ?? AccessTools.TypeByName("RimWorld.PlantProperties");
            var postLoadSpecialMethod = plantPropType != null ? AccessTools.Method(plantPropType, "PostLoadSpecial") : null;
            if (postLoadSpecialMethod != null)
            {
                harmony.Patch(postLoadSpecialMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(DeferredLoaderTests), nameof(MockPostLoadSpecial))));
            }

            // Initialize FasterGameLoadingMod.harmony for SoundStarter_Patch.Unpatch()
            var fglModHarmonyProp = typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.harmony), BindingFlags.Public | BindingFlags.Static);
            fglModHarmonyProp?.SetValue(null, new Harmony("FasterGameLoadingMod.TestInstance"), null);
        }

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
        public void LoadDeferredGraphicsCoroutine_WhenBadTexAndHasGraphic_ExtractsMatSingleMainTexture()
        {
            var def = CreateMockThingDef("DefWithGraphic");
            def.uiIcon = mockBadTex;
            def.uiIconPath = null;
            def.graphicData = new GraphicData
            {
                graphicClass = typeof(Graphic_Single)
            };
            var graphicField = AccessTools.Field(typeof(GraphicData), "cachedGraphic");
            graphicField?.SetValue(def.graphicData, new TestGraphic(mockMaterial));

            delayedActions.EnqueueGraphic(def, () => { });

            var loadedDefs = new List<ThingDef>();
            var coroutine = DeferredLoader.LoadDeferredGraphicsCoroutine(delayedActions, loadedDefs);

            while (coroutine.MoveNext()) { }

            Assert.That(def.uiIcon, Is.SameAs(mockMaterialTex));
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
            var coroutine = DeferredLoader.UpdateMapMeshForLoadedDefs(new List<ThingDef>());
            Assert.DoesNotThrow(() =>
            {
                while (coroutine.MoveNext()) { }
            });
        }
    }
}
