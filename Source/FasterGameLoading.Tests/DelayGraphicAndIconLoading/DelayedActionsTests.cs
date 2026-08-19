using System;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;
using Verse.Sound;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading
{
    [TestFixture]
    public class DelayedActionsTests
    {
        private Harmony harmony;
        private DelayedActions delayedActions;

        private static bool PrefixSkip() => false;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.DelayedActionsTests");
            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsTests), nameof(PrefixSkip))));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.DelayedActionsTests");
        }

        [SetUp]
        public void SetUp()
        {
            delayedActions = new DelayedActions();
        }

        [TearDown]
        public void TearDown()
        {
            delayedActions?.ClearQueues();
            delayedActions?.StopStopwatch();
        }

        private static ThingDef CreateMockThingDef(string name)
        {
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            def.defName = name;
            return def;
        }

        [Test]
        public void EnqueueGraphic_And_TryDequeueGraphic_OperateCorrectlyInFifoOrder()
        {
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
            Assert.That(delayedActions.TryDequeueGraphic(out _, out _), Is.False);

            var def1 = CreateMockThingDef("TestDef1");
            var def2 = CreateMockThingDef("TestDef2");
            bool action1Called = false;
            bool action2Called = false;
            Action act1 = () => action1Called = true;
            Action act2 = () => action2Called = true;

            delayedActions.EnqueueGraphic(def1, act1);
            delayedActions.EnqueueGraphic(def2, act2);

            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(2));

            // Dequeue 1
            Assert.That(delayedActions.TryDequeueGraphic(out var outDef1, out var outAct1), Is.True);
            Assert.That(outDef1, Is.SameAs(def1));
            outAct1();
            Assert.That(action1Called, Is.True);
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(1));

            // Dequeue 2
            Assert.That(delayedActions.TryDequeueGraphic(out var outDef2, out var outAct2), Is.True);
            Assert.That(outDef2, Is.SameAs(def2));
            outAct2();
            Assert.That(action2Called, Is.True);
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));

            // Empty
            Assert.That(delayedActions.TryDequeueGraphic(out _, out _), Is.False);
        }

        [Test]
        public void EnqueueIcon_And_TryDequeueIcon_OperateCorrectlyInFifoOrder()
        {
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));
            Assert.That(delayedActions.TryDequeueIcon(out _, out _), Is.False);

            var def1 = CreateMockThingDef("IconDef1");
            var def2 = CreateMockThingDef("IconDef2");
            bool act1Called = false;
            bool act2Called = false;

            delayedActions.EnqueueIcon(def1, () => act1Called = true);
            delayedActions.EnqueueIcon(def2, () => act2Called = true);

            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(2));

            Assert.That(delayedActions.TryDequeueIcon(out var outDef1, out var outAct1), Is.True);
            Assert.That(outDef1, Is.SameAs(def1));
            outAct1();
            Assert.That(act1Called, Is.True);
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(1));

            Assert.That(delayedActions.TryDequeueIcon(out var outDef2, out var outAct2), Is.True);
            Assert.That(outDef2, Is.SameAs(def2));
            outAct2();
            Assert.That(act2Called, Is.True);
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));

            Assert.That(delayedActions.TryDequeueIcon(out _, out _), Is.False);
        }

        [Test]
        public void EnqueueSubSound_And_TryDequeueSubSound_OperateCorrectlyInFifoOrder()
        {
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
            Assert.That(delayedActions.TryDequeueSubSound(out _, out _), Is.False);

            var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            bool act1Called = false;
            bool act2Called = false;

            delayedActions.EnqueueSubSound(sound1, () => act1Called = true);
            delayedActions.EnqueueSubSound(sound2, () => act2Called = true);

            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(2));

            Assert.That(delayedActions.TryDequeueSubSound(out var outDef1, out var outAct1), Is.True);
            Assert.That(outDef1, Is.SameAs(sound1));
            outAct1();
            Assert.That(act1Called, Is.True);
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(1));

            Assert.That(delayedActions.TryDequeueSubSound(out var outDef2, out var outAct2), Is.True);
            Assert.That(outDef2, Is.SameAs(sound2));
            outAct2();
            Assert.That(act2Called, Is.True);
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));

            Assert.That(delayedActions.TryDequeueSubSound(out _, out _), Is.False);
        }

        [Test]
        public void EnqueueMainThreadAction_IgnoresNullAndEnqueuesNonNull()
        {
            delayedActions.EnqueueMainThreadAction(action: null);

            bool actionExecuted = false;
            delayedActions.EnqueueMainThreadAction(() => actionExecuted = true);

            delayedActions.Update();
            Assert.That(actionExecuted, Is.True);
        }

        [Test]
        public void ClearQueues_ClearsAllQueues()
        {
            var def = CreateMockThingDef("TestClear");
            var iconDef = CreateMockThingDef("IconClear");
            var sound = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            bool mainThreadActionExecuted = false;

            delayedActions.EnqueueGraphic(def, () => { });
            delayedActions.EnqueueIcon(iconDef, () => { });
            delayedActions.EnqueueSubSound(sound, () => { });
            delayedActions.EnqueueMainThreadAction(() => mainThreadActionExecuted = true);

            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(1));
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(1));
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(1));

            delayedActions.ClearQueues();

            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));

            delayedActions.Update();
            Assert.That(mainThreadActionExecuted, Is.False);
        }

        [Test]
        public void Update_DrainsMainThreadActions_AndCatchesExceptionsWithoutAbortingRemainingActions()
        {
            bool firstExecuted = false;
            bool thirdExecuted = false;

            delayedActions.EnqueueMainThreadAction(() => firstExecuted = true);
            delayedActions.EnqueueMainThreadAction(() => throw new InvalidOperationException("Test exception in main thread action"));
            delayedActions.EnqueueMainThreadAction(() => thirdExecuted = true);

            Assert.DoesNotThrow(() => delayedActions.Update());

            Assert.That(firstExecuted, Is.True);
            Assert.That(thirdExecuted, Is.True);
        }

        [Test]
        public void StopwatchAndBudget_BehaviorOperatesCorrectly()
        {
            // Initially false when stopwatch is 0
            Assert.That(delayedActions.IsOverBudget, Is.False);

            delayedActions.StartStopwatch();
            delayedActions.RestartStopwatch();
            delayedActions.StopStopwatch();

            // When Current.Game is null in test runner, MaxImpactThisFrame is 0.05f (50ms)
            Assert.That(DelayedActions.MaxImpactThisFrame, Is.EqualTo(0.05f));
        }

        [Test]
        public void ResetEarlyLoading_ResetsStaticFlagsAndLoader()
        {
            DelayedActions.AllDeferredVisualsLoaded = true;
            DelayedActions.AdaptiveStaticAtlasBakeFailed = true;

            delayedActions.ResetEarlyLoading();

            Assert.That(DelayedActions.AllDeferredVisualsLoaded, Is.False);
            Assert.That(DelayedActions.AdaptiveStaticAtlasBakeFailed, Is.False);
        }

        [Test]
        public void CacheResetter_ResetsDelayedActionsStaticFlags()
        {
            DelayedActions.AllDeferredVisualsLoaded = true;
            DelayedActions.AdaptiveStaticAtlasBakeFailed = true;

            CacheResetter.ResetAll();

            Assert.That(DelayedActions.AllDeferredVisualsLoaded, Is.False);
            Assert.That(DelayedActions.AdaptiveStaticAtlasBakeFailed, Is.False);
        }

        [Test]
        public void LateUpdate_InvokesEarlyModContentLoaderUpdateWithoutThrowing()
        {
            // LateUpdate 每幀呼叫 EarlyModContentLoader.Update；在設定關閉時該方法提前返回，
            // 此處僅驗證呼叫路徑本身不拋出例外（覆蓋 DelayedActions.LateUpdate）。
            Assert.DoesNotThrow(() => delayedActions.LateUpdate());
        }

        [Test]
        public void PerformActions_WhenDeferredVisualPipelineDisabled_ResolvesSubSoundsAndMarksLoaded()
        {
            bool originalDelay = FasterGameLoadingSettings.DelayGraphicLoading;
            bool originalLoaded = DelayedActions.AllDeferredVisualsLoaded;
            FasterGameLoadingSettings.DelayGraphicLoading = false;
            try
            {
                // DelayGraphicLoading=false 時略過延遲圖形/圖集烘焙管線，直接標記視覺已載入，
                // 並在 finally 中執行取消 SoundStarter 攔截與清空 savedGraphics（覆蓋 PerformActions 主協程）。
                var enumerator = delayedActions.PerformActions();
                while (enumerator.MoveNext()) { }

                Assert.That(DelayedActions.AllDeferredVisualsLoaded, Is.True);
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = originalDelay;
                DelayedActions.AllDeferredVisualsLoaded = originalLoaded;
            }
        }

        [Test]
        public void PerformActions_WhenDeferredVisualPipelineEnabled_StartsPipelineAndResolvesWithoutBaking()
        {
            bool originalDelay = FasterGameLoadingSettings.DelayGraphicLoading;
            bool originalLoaded = DelayedActions.AllDeferredVisualsLoaded;
            FasterGameLoadingSettings.DelayGraphicLoading = true;
            try
            {
                // DelayGraphicLoading=true 時進入延遲視覺管線：第一次 MoveNext 覆蓋 210-216
                // （含 LoadDeferredGraphicsCoroutine 呼叫），第二次覆蓋 217-218（產生
                // BakeDeferredAtlasesCoroutine 列舉器，但尚未執行其烘焙本體）。烘焙呼叫需要
                // 遊戲/圖集基礎設施，無頭環境會拋出，故不繼續迭代；finally 區塊會在 Dispose 時安全執行。
                var enumerator = delayedActions.PerformActions();
                try
                {
                    Assert.That(enumerator.MoveNext(), Is.True);
                    Assert.That(enumerator.MoveNext(), Is.True);
                }
                finally
                {
                    if (enumerator is IDisposable disposable)
                        disposable.Dispose();
                }
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = originalDelay;
                DelayedActions.AllDeferredVisualsLoaded = originalLoaded;
            }
        }
    }
}
