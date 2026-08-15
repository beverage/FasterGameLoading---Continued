using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class EarlyModContentLoaderTests
    {
        private static Harmony harmony;
        private EarlyModContentLoader loader;
        private DelayedActions delayedActions;
        private List<ModContentPack> originalRunningMods;

        private static bool mockReloadShouldThrow;
        private static readonly List<ModContentPack> reloadedMods = new List<ModContentPack>();
        private static bool mockIsOverBudget;

        private static bool PrefixSkip() => false;

        private static bool MockReloadContentInt(ModContentPack __instance)
        {
            if (mockReloadShouldThrow)
            {
                throw new InvalidOperationException("Simulated ReloadContentInt failure for testing");
            }
            reloadedMods.Add(__instance);
            return false;
        }

        private static bool MockIsOverBudget(ref bool __result)
        {
            __result = mockIsOverBudget;
            return false;
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.EarlyModContentLoaderTests");

            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(EarlyModContentLoaderTests), nameof(PrefixSkip))));
            }

            var reloadContentIntMethod = AccessTools.Method(typeof(ModContentPack), "ReloadContentInt");
            if (reloadContentIntMethod != null)
            {
                harmony.Patch(reloadContentIntMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(EarlyModContentLoaderTests), nameof(MockReloadContentInt))));
            }

            var isOverBudgetGetter = AccessTools.PropertyGetter(typeof(DelayedActions), nameof(DelayedActions.IsOverBudget));
            if (isOverBudgetGetter != null)
            {
                harmony.Patch(isOverBudgetGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(EarlyModContentLoaderTests), nameof(MockIsOverBudget))));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.EarlyModContentLoaderTests");
        }

        [SetUp]
        public void SetUp()
        {
            loader = new EarlyModContentLoader();
            delayedActions = new DelayedActions();
            mockReloadShouldThrow = false;
            mockIsOverBudget = false;
            reloadedMods.Clear();
            ModContentPack_ReloadContentInt_Patch.loadedMods.Clear();
            FasterGameLoadingSettings.earlyModContentLoading = true;

            var runningModsField = AccessTools.Field(typeof(LoadedModManager), "runningMods");
            if (runningModsField != null)
            {
                originalRunningMods = (List<ModContentPack>)runningModsField.GetValue(null);
            }
        }

        [TearDown]
        public void TearDown()
        {
            var runningModsField = AccessTools.Field(typeof(LoadedModManager), "runningMods");
            runningModsField?.SetValue(null, originalRunningMods);

            ModContentPack_ReloadContentInt_Patch.loadedMods.Clear();
            reloadedMods.Clear();
            mockReloadShouldThrow = false;
            mockIsOverBudget = false;
            loader?.Reset();
        }

        private static ModContentPack CreateMockModContentPack(string packageId = "test.mod")
        {
            var mod = (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
            var packageIdField = AccessTools.Field(typeof(ModContentPack), "packageIdInt");
            packageIdField?.SetValue(mod, packageId);
            return mod;
        }

        private static void SetRunningMods(List<ModContentPack> mods)
        {
            var runningModsField = AccessTools.Field(typeof(LoadedModManager), "runningMods");
            runningModsField?.SetValue(null, mods);
        }

        [Test]
        public void EarlyLoadingComplete_InitialIsFalse_WhenCompletedIsTrue_AndSubsequentUpdateReturnsImmediately()
        {
            var mod1 = CreateMockModContentPack("test.mod1");
            var mod2 = CreateMockModContentPack("test.mod2");
            SetRunningMods(new List<ModContentPack> { mod1, mod2 });

            Assert.That(loader.EarlyLoadingComplete, Is.False);

            loader.Update(delayedActions);

            Assert.That(loader.EarlyLoadingComplete, Is.True);
            Assert.That(reloadedMods.Count, Is.EqualTo(2));
            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(mod1), Is.True);
            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(mod2), Is.True);

            // Subsequent update should return immediately without executing anything
            loader.Update(delayedActions);
            Assert.That(reloadedMods.Count, Is.EqualTo(2));
        }

        [Test]
        public void Update_WhenSettingDisabled_ReturnsImmediately()
        {
            FasterGameLoadingSettings.earlyModContentLoading = false;
            var mod = CreateMockModContentPack("test.mod");
            SetRunningMods(new List<ModContentPack> { mod });

            loader.Update(delayedActions);

            Assert.That(loader.EarlyLoadingComplete, Is.False);
            Assert.That(reloadedMods, Is.Empty);
            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods, Is.Empty);
        }

        [Test]
        public void Update_WhenSkipFramesGreaterThanZero_DecrementsAndYieldsFrame()
        {
            var mod1 = CreateMockModContentPack("test.mod1");
            var mod2 = CreateMockModContentPack("test.mod2");
            SetRunningMods(new List<ModContentPack> { mod1, mod2 });

            // Set skipFrames to 2 via reflection
            var skipFramesField = AccessTools.Field(typeof(EarlyModContentLoader), "skipFrames");
            skipFramesField?.SetValue(loader, 2);

            // Frame 1: skipFrames 2 -> 1, no mod processed
            loader.Update(delayedActions);
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(1));
            Assert.That(reloadedMods, Is.Empty);

            // Frame 2: skipFrames 1 -> 0, no mod processed
            loader.Update(delayedActions);
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(0));
            Assert.That(reloadedMods, Is.Empty);

            // Frame 3: skipFrames is 0, normal processing runs
            loader.Update(delayedActions);
            Assert.That(reloadedMods.Count, Is.EqualTo(2));
            Assert.That(loader.EarlyLoadingComplete, Is.True);
        }

        [Test]
        public void Update_WhenConsecutiveTimeoutsReachesThreshold_SetsSkipFramesToFive()
        {
            var mods = new List<ModContentPack>();
            for (int i = 0; i < 5; i++)
            {
                mods.Add(CreateMockModContentPack($"test.mod{i}"));
            }
            SetRunningMods(mods);

            var skipFramesField = AccessTools.Field(typeof(EarlyModContentLoader), "skipFrames");
            var consecutiveTimeoutsField = AccessTools.Field(typeof(EarlyModContentLoader), "consecutiveTimeouts");

            mockIsOverBudget = true;

            // Call 1: process 1 mod, over budget -> consecutiveTimeouts = 1
            loader.Update(delayedActions);
            Assert.That(reloadedMods.Count, Is.EqualTo(1));
            Assert.That((int)consecutiveTimeoutsField.GetValue(loader), Is.EqualTo(1));
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(0));

            // Call 2: process 1 mod, over budget -> consecutiveTimeouts = 2
            loader.Update(delayedActions);
            Assert.That(reloadedMods.Count, Is.EqualTo(2));
            Assert.That((int)consecutiveTimeoutsField.GetValue(loader), Is.EqualTo(2));
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(0));

            // Call 3: process 1 mod, over budget -> consecutiveTimeouts = 3 -> triggers skipFrames = 5, consecutiveTimeouts = 0
            loader.Update(delayedActions);
            Assert.That(reloadedMods.Count, Is.EqualTo(3));
            Assert.That((int)consecutiveTimeoutsField.GetValue(loader), Is.EqualTo(0));
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(5));

            // Call 4: skipFrames 5 -> 4, no mods processed
            loader.Update(delayedActions);
            Assert.That(reloadedMods.Count, Is.EqualTo(3));
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(4));
        }

        [Test]
        public void Update_WhenReloadContentIntThrows_DoesNotAddToLoadedModsAndAllowsRetry()
        {
            var mod = CreateMockModContentPack("test.failing.mod");
            SetRunningMods(new List<ModContentPack> { mod });

            mockReloadShouldThrow = true;

            // Should catch exception and not add to loadedMods
            Assert.DoesNotThrow(() => loader.Update(delayedActions));
            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(mod), Is.False);

            // Now reset and succeed
            mockReloadShouldThrow = false;
            loader.Reset();
            loader.Update(delayedActions);

            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(mod), Is.True);
            Assert.That(loader.EarlyLoadingComplete, Is.True);
        }

        [Test]
        public void Reset_ClearsAllQueuesAndCounterState()
        {
            var mod = CreateMockModContentPack("test.mod");
            SetRunningMods(new List<ModContentPack> { mod });

            var skipFramesField = AccessTools.Field(typeof(EarlyModContentLoader), "skipFrames");
            var consecutiveTimeoutsField = AccessTools.Field(typeof(EarlyModContentLoader), "consecutiveTimeouts");
            var pendingEarlyLoadsField = AccessTools.Field(typeof(EarlyModContentLoader), "pendingEarlyLoads");

            skipFramesField?.SetValue(loader, 3);
            consecutiveTimeoutsField?.SetValue(loader, 2);

            loader.Reset();

            Assert.That(loader.EarlyLoadingComplete, Is.False);
            Assert.That((int)skipFramesField.GetValue(loader), Is.EqualTo(0));
            Assert.That((int)consecutiveTimeoutsField.GetValue(loader), Is.EqualTo(0));
            Assert.That(pendingEarlyLoadsField.GetValue(loader), Is.Null);
        }
    }
}
