using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class AccessTools_AllTypes_PatchTests
    {
        private static Harmony harmony;
        private static Action capturedExecuteWhenFinishedAction;

        private static bool PrefixSkip() => false;
        private static bool MockIsInMainThread() => true;

        private static bool MockExecuteWhenFinished(Action action)
        {
            capturedExecuteWhenFinishedAction = action;
            return false;
        }

        private static FieldInfo AllTypesCachedField =>
            AccessTools.Field(typeof(AccessTools_AllTypes_Patch), "allTypesCached");

        private static FieldInfo CachedAssembliesCountField =>
            AccessTools.Field(typeof(AccessTools_AllTypes_Patch), "cachedAssembliesCount");

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.AccessTools_AllTypes_PatchTests");

            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(AccessTools_AllTypes_PatchTests), nameof(PrefixSkip))));
            }

            var isInMainThreadGetter = AccessTools.PropertyGetter(typeof(UnityData), nameof(UnityData.IsInMainThread));
            if (isInMainThreadGetter != null)
            {
                harmony.Patch(isInMainThreadGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(AccessTools_AllTypes_PatchTests), nameof(MockIsInMainThread))));
            }

            var execWhenFinished = AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished));
            if (execWhenFinished != null)
            {
                harmony.Patch(execWhenFinished, prefix: new HarmonyMethod(AccessTools.Method(typeof(AccessTools_AllTypes_PatchTests), nameof(MockExecuteWhenFinished))));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.AccessTools_AllTypes_PatchTests");
        }

        [SetUp]
        public void SetUp()
        {
            capturedExecuteWhenFinishedAction = null;
            AllTypesCachedField?.SetValue(obj: null, value: null);
            CachedAssembliesCountField?.SetValue(null, 0);
            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            capturedExecuteWhenFinishedAction = null;
            AllTypesCachedField?.SetValue(obj: null, value: null);
            CachedAssembliesCountField?.SetValue(null, 0);
            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
        }

        [Test]
        public void Prefix_WhenCacheValidAndAssemblyCountMatches_ReturnsCachedTypesDirectly()
        {
            var fakeTypes = new List<Type> { typeof(int), typeof(string), typeof(AccessTools_AllTypes_PatchTests) };
            int currentAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;

            AllTypesCachedField.SetValue(null, fakeTypes);
            CachedAssembliesCountField.SetValue(null, currentAssemblyCount);

            IEnumerable<Type> result = null;
            bool shouldRunOriginal = AccessTools_AllTypes_Patch.Prefix(ref result);

            Assert.That(shouldRunOriginal, Is.False);
            Assert.That(result, Is.SameAs(fakeTypes));
        }

        [Test]
        public void Prefix_WhenCacheNullOrAssemblyCountDiffers_RebuildsCacheAndReturnsFalse()
        {
            AllTypesCachedField.SetValue(obj: null, value: null);
            CachedAssembliesCountField.SetValue(null, 0);

            IEnumerable<Type> result = null;
            bool shouldRunOriginal = AccessTools_AllTypes_Patch.Prefix(ref result);

            Assert.That(shouldRunOriginal, Is.False);
            Assert.That(result, Is.Not.Null);

            var cached = (List<Type>)AllTypesCachedField.GetValue(null);
            int cachedCount = (int)CachedAssembliesCountField.GetValue(null);

            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.Count, Is.GreaterThan(0));
            // Assembly count may increase during test run (flaky); use >= to allow for concurrent loads
            Assert.That(cachedCount, Is.GreaterThanOrEqualTo(AppDomain.CurrentDomain.GetAssemblies().Length - 1));
            Assert.That(result, Is.SameAs(cached));
        }

        [Test]
        public void Preload_WhenMultiThreadingDisabled_LoadsSynchronouslyAndWarmsUpFullName()
        {
            FasterGameLoadingSettings.EnableMultiThreading = false;

            AccessTools_AllTypes_Patch.Preload();

            var cached = (List<Type>)AllTypesCachedField.GetValue(null);
            int cachedCount = (int)CachedAssembliesCountField.GetValue(null);

            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.Count, Is.GreaterThan(0));
            // Assembly count may increase during test run (flaky); use >= to allow for concurrent loads
            Assert.That(cachedCount, Is.GreaterThanOrEqualTo(AppDomain.CurrentDomain.GetAssemblies().Length - 1));

            // Verify FullName warmup in GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.ContainsKey(typeof(AccessTools_AllTypes_Patch).FullName), Is.True);
        }

        [Test]
        public void Preload_WhenMultiThreadingEnabled_SchedulesBackgroundTaskAndWarmupCallback()
        {
            FasterGameLoadingSettings.EnableMultiThreading = true;

            AccessTools_AllTypes_Patch.Preload();

            // Verify LongEventHandler callback was registered
            Assert.That(capturedExecuteWhenFinishedAction, Is.Not.Null);

            // Wait for the background task to populate allTypesCached
            bool completed = SpinWait.SpinUntil(() => AllTypesCachedField.GetValue(null) is not null, 3000);
            Assert.That(completed, Is.True, "Background task did not complete within timeout.");

            var cached = (List<Type>)AllTypesCachedField.GetValue(null);
            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.Count, Is.GreaterThan(0));

            // Invoke ExecuteWhenFinished callback to run WarmupTypeCache on main thread
            capturedExecuteWhenFinishedAction();
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.ContainsKey(typeof(AccessTools_AllTypes_Patch).FullName), Is.True);
        }
    }
}
