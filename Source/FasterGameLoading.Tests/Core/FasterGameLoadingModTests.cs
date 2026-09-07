using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.Core
{
    /// <summary>
    /// FasterGameLoadingMod 建構期啟動的背景工作測試。
    /// 建構式本身需要 Unity GameObject 與 Harmony 全域補丁，headless 下無法執行，
    /// 因此以反射直接驅動其中可獨立運作的私有步驟。
    /// </summary>
    [TestFixture]
    public class FasterGameLoadingModTests
    {
        private Harmony harmony;
        private bool? originalImageOptActive;
        private bool originalVerboseLogging;
        private static readonly List<ModMetaData> MockActiveMods = new List<ModMetaData>();
        private static int stubDeletedCount;

        [SetUp]
        public void SetUp()
        {
            originalImageOptActive = (bool?)AccessTools.Field(typeof(ImageOptCompat), "isActive").GetValue(null);
            originalVerboseLogging = FasterGameLoadingSettings.VerboseLogging;
            // FGLLog.Message 只在詳細日誌開啟時輸出；本組測試以該輸出當作背景工作的完成訊號。
            FasterGameLoadingSettings.VerboseLogging = true;
            MockActiveMods.Clear();
            stubDeletedCount = 0;

            harmony = new Harmony("FasterGameLoading.Tests.Core.FasterGameLoadingModTests");

            // ModsConfig 的靜態建構式在測試環境中被停用，其內部清單為 null，
            // 故啟用中的 Mod 清單必須以 stub 取代。
            var activeMods = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.ActiveModsInLoadOrder));
            harmony.Patch(activeMods, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(FasterGameLoadingModTests), nameof(Prefix_ActiveModsStub))));
        }

        [TearDown]
        public void TearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.Core.FasterGameLoadingModTests");
            harmony = null;
            TestSetup.OnLogMessage = null;
            MockActiveMods.Clear();
            FasterGameLoadingSettings.VerboseLogging = originalVerboseLogging;
            AccessTools.Field(typeof(ImageOptCompat), "isActive").SetValue(obj: null, value: originalImageOptActive);
        }

        private static bool Prefix_ActiveModsStub(ref IEnumerable<ModMetaData> __result)
        {
            __result = MockActiveMods;
            return false;
        }

        private static bool Prefix_CleanupStub(ref int __result)
        {
            __result = stubDeletedCount;
            return false;
        }

        [Test]
        public void StartCleanupInvalidImageOptCaches_DoesNothingWhenImageOptInactive()
        {
            AccessTools.Field(typeof(ImageOptCompat), "isActive").SetValue(obj: null, value: false);
            PatchCleanup();
            stubDeletedCount = 5;

            string logged = null;
            TestSetup.OnLogMessage = text => Volatile.Write(ref logged, text);

            InvokeStartCleanup();

            // 沒有 ImageOpt 就不該啟動任何背景 I/O。給足時間讓誤啟動的工作有機會露出。
            SpinWait.SpinUntil(() => Volatile.Read(ref logged) != null, TimeSpan.FromMilliseconds(600));
            Assert.That(Volatile.Read(ref logged), Is.Null);
        }

        [Test]
        public void StartCleanupInvalidImageOptCaches_ReportsDeletedCountInBackground()
        {
            var meta = (ModMetaData)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ModMetaData));
            AccessTools.Field(typeof(ModMetaData), "rootDirInt").SetValue(meta, new System.IO.DirectoryInfo(@"C:\test_mod"));
            MockActiveMods.Add(null);
            MockActiveMods.Add(meta);

            AccessTools.Field(typeof(ImageOptCompat), "isActive").SetValue(obj: null, value: true);
            PatchCleanup();
            stubDeletedCount = 3;

            string logged = null;
            TestSetup.OnLogMessage = text => Volatile.Write(ref logged, text);

            InvokeStartCleanup();

            // 背景清理刻意延遲 TexturePreloadDelayMs 才動手，避開啟動期的磁碟尖峰。
            SpinWait.SpinUntil(() => Volatile.Read(ref logged) != null, TimeSpan.FromSeconds(5));

            Assert.That(Volatile.Read(ref logged), Does.Contain("3"));
        }

        [Test]
        public void StartCleanupInvalidImageOptCaches_SwallowsBackgroundFailures()
        {
            AccessTools.Field(typeof(ImageOptCompat), "isActive").SetValue(obj: null, value: true);
            var target = AccessTools.Method(typeof(ImageOptCompat), nameof(ImageOptCompat.CleanupInvalidDdsZstdCaches));
            harmony.Patch(target, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(FasterGameLoadingModTests), nameof(Prefix_CleanupThrowsStub))));

            string warned = null;
            TestSetup.OnLogWarning = text => Volatile.Write(ref warned, text);
            try
            {
                InvokeStartCleanup();

                SpinWait.SpinUntil(() => Volatile.Read(ref warned) != null, TimeSpan.FromSeconds(5));

                // fire-and-forget 的工作沒有呼叫端會觀察它；例外必須就地記錄，
                // 否則會成為被靜默吞掉的 faulted task。
                Assert.That(Volatile.Read(ref warned), Is.Not.Null);
            }
            finally
            {
                TestSetup.OnLogWarning = null;
            }
        }

        private static bool Prefix_CleanupThrowsStub()
        {
            throw new InvalidOperationException("stubbed cleanup failure");
        }

        private void PatchCleanup()
        {
            var target = AccessTools.Method(typeof(ImageOptCompat), nameof(ImageOptCompat.CleanupInvalidDdsZstdCaches));
            harmony.Patch(target, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(FasterGameLoadingModTests), nameof(Prefix_CleanupStub))));
        }

        private static void InvokeStartCleanup()
        {
            typeof(FasterGameLoadingMod)
                .GetMethod("StartCleanupInvalidImageOptCaches", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(obj: null, parameters: null);
        }

        [Test]
        public void StartXmlScan_WhenXPathCachingDisabled_SkipsScanAndMarksComplete()
        {
            var originalSetting = FasterGameLoadingSettings.XPathCaching;
            try
            {
                FasterGameLoadingSettings.XPathCaching = false;
                XmlNode_SelectSingleNode_Patch.isCacheValidated = true;
                XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;

                typeof(FasterGameLoadingMod)
                    .GetMethod("StartXmlScan", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, null);

                Assert.That(XmlNode_SelectSingleNode_Patch.isCacheValidated, Is.False);
                Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.True);
            }
            finally
            {
                FasterGameLoadingSettings.XPathCaching = originalSetting;
            }
        }

        [Test]
        public void StartXmlScan_WhenExceptionOccurs_FailsClosedSafely()
        {
            var originalSetting = FasterGameLoadingSettings.XPathCaching;
            try
            {
                FasterGameLoadingSettings.XPathCaching = true;
                SessionCache.xmlPathsSinceLastSession.TryAdd("test", 1);

                var runningModsProp = AccessTools.PropertyGetter(typeof(LoadedModManager), nameof(LoadedModManager.RunningMods));
                harmony.Patch(runningModsProp, prefix: new HarmonyMethod(AccessTools.Method(typeof(FasterGameLoadingModTests), nameof(Prefix_RunningModsThrows))));

                typeof(FasterGameLoadingMod)
                    .GetMethod("StartXmlScan", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, null);

                Assert.That(XmlNode_SelectSingleNode_Patch.isCacheValidated, Is.False);
                Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.True);
                Assert.That(SessionCache.xmlPathsSinceLastSession, Is.Empty);
            }
            finally
            {
                FasterGameLoadingSettings.XPathCaching = originalSetting;
            }
        }

        private static bool Prefix_RunningModsThrows()
        {
            throw new InvalidOperationException("stubbed running mods failure");
        }

        private static List<ModContentPack> runningModsNormalStub;
        private static bool Prefix_RunningModsNormal(ref IEnumerable<ModContentPack> __result)
        {
            __result = runningModsNormalStub;
            return false;
        }

        [Test]
        public void StartXmlScan_WhenNormalMods_CollectsScanTargetsAndStartsScan()
        {
            var originalSetting = FasterGameLoadingSettings.XPathCaching;
            try
            {
                FasterGameLoadingSettings.XPathCaching = true;

                // 1. official mod (應被跳過)
                var officialMod = (ModContentPack)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ModContentPack));
                AccessTools.Field(typeof(ModContentPack), "official").SetValue(officialMod, true);
                AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(officialMod, new System.IO.DirectoryInfo(@"C:\OfficialMod"));

                // 2. 一般 mod，有 foldersToLoadDescendingOrder
                var modWithFolders = (ModContentPack)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ModContentPack));
                AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(modWithFolders, new System.IO.DirectoryInfo(@"C:\NormalMod1"));
                AccessTools.Field(typeof(ModContentPack), "foldersToLoadDescendingOrder").SetValue(modWithFolders, new List<string> { @"C:\NormalMod1\1.6" });

                // 3. 一般 mod，無 foldersToLoadDescendingOrder (fallback 到 RootDir)
                var modWithoutFolders = (ModContentPack)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ModContentPack));
                AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(modWithoutFolders, new System.IO.DirectoryInfo(@"C:\NormalMod2"));

                var runningModsProp = AccessTools.PropertyGetter(typeof(LoadedModManager), nameof(LoadedModManager.RunningMods));
                runningModsNormalStub = new List<ModContentPack> { null, officialMod, modWithFolders, modWithoutFolders };
                harmony.Patch(runningModsProp, prefix: new HarmonyMethod(AccessTools.Method(typeof(FasterGameLoadingModTests), nameof(Prefix_RunningModsNormal))));

                var daProp = typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.delayedActions), BindingFlags.Public | BindingFlags.Static);
                var oldDa = daProp?.GetValue(null);
                var testDa = new DelayedActions();
                daProp?.GetSetMethod(nonPublic: true)?.Invoke(null, new object[] { testDa });

                string warn = null;
                TestSetup.OnLogWarning = text => warn = text;

                try
                {
                    typeof(FasterGameLoadingMod)
                        .GetMethod("StartXmlScan", BindingFlags.NonPublic | BindingFlags.Static)
                        .Invoke(null, null);

                    Assert.That(warn, Is.Null, "StartXmlScan 發生警告: " + warn);
                    Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.False);
                }
                finally
                {
                    TestSetup.OnLogWarning = null;
                    daProp?.GetSetMethod(nonPublic: true)?.Invoke(null, new object[] { oldDa });
                }
            }
            finally
            {
                FasterGameLoadingSettings.XPathCaching = originalSetting;
                runningModsNormalStub = null;
            }
        }

        [Test]
        public void SettingsCategory_ReturnsTranslatedCategory()
        {
            var mod = (FasterGameLoadingMod)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(FasterGameLoadingMod));
            var category = mod.SettingsCategory();
            Assert.That(category, Is.Not.Null);
        }

        [Test]
        public void DoSettingsWindowContents_CallsSettings()
        {
            var mod = (FasterGameLoadingMod)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(FasterGameLoadingMod));
            var method = AccessTools.Method(typeof(FasterGameLoadingSettings), nameof(FasterGameLoadingSettings.DoSettingsWindowContents));
            var testHarmony = new Harmony("test.mod.dosettings");
            testHarmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(FasterGameLoadingModTests), nameof(Prefix_DoSettingsStub))));
            try
            {
                mod.DoSettingsWindowContents(new UnityEngine.Rect(0, 0, 100, 100));
                Assert.That(calledSettingsWindow, Is.True);
            }
            finally
            {
                testHarmony.Unpatch(method, HarmonyPatchType.Prefix, testHarmony.Id);
                calledSettingsWindow = false;
            }
        }

        private static bool calledSettingsWindow;
        private static bool Prefix_DoSettingsStub()
        {
            calledSettingsWindow = true;
            return false;
        }
    }
}
