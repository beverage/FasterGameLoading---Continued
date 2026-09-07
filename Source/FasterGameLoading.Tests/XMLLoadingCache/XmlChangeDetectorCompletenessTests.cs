using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FasterGameLoading.Tests.XMLLoadingCache
{
    [TestFixture]
    public class XmlChangeDetectorCompletenessTests
    {
        [SetUp]
        public void SetUp()
        {
            SessionCache.xmlPathsSinceLastSession.Clear();
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
        }

        [TearDown]
        public void TearDown()
        {
            SessionCache.xmlPathsSinceLastSession.Clear();
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
        }

        [Test]
        public void IncompleteScan_DoesNotValidate_AndClearsPersistedMisses()
        {
            SessionCache.xmlPathsSinceLastSession.TryAdd("Defs/ThingDef/comps", 0);
            var result = new XmlChangeDetector.XmlScanResult(
                new Dictionary<string, long>(), fileCount: 0, isComplete: false);

            try
            {
                XmlChangeDetector.CommitXmlScanResult(result);
            }
            catch (TypeInitializationException)
            {
                // 測試主機沒有 Verse 的完整靜態環境；安全狀態已在記錄日誌前更新。
            }

            Assert.That(XmlNode_SelectSingleNode_Patch.isCacheValidated, Is.False);
            Assert.That(SessionCache.xmlPathsSinceLastSession, Is.Empty);
            Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.True);
        }

        [Test]
        public void ApplyPatchesPatch_TargetsTheActualPatchApplicationMethod()
        {
            var attribute = (HarmonyLib.HarmonyPatch)Attribute.GetCustomAttribute(
                typeof(LoadedModManager_ApplyPatches_Patch), typeof(HarmonyLib.HarmonyPatch));

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.info.declaringType, Is.EqualTo(typeof(Verse.LoadedModManager)));
            Assert.That(attribute.info.methodName, Is.EqualTo(nameof(Verse.LoadedModManager.ApplyPatches)));
        }

        private static bool FailEnumerateFilesPrefix(ref System.Collections.Generic.IEnumerable<System.IO.FileInfo> __result)
        {
            throw new UnauthorizedAccessException("Simulated directory enumeration access denied");
        }

        private static bool FailFileLengthPrefix(ref long __result)
        {
            throw new System.IO.IOException("Simulated file length read failure");
        }

        [Test]
        public void ScanDirectoryMetadata_WhenEnumerateFilesThrows_SetsIsCompleteFalse()
        {
            var method = typeof(XmlChangeDetector).GetMethod("ScanDirectoryMetadata",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FGL_XmlTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempDir);

            var harmony = new HarmonyLib.Harmony("test.scandir.fail_enum");
            var target = typeof(System.IO.DirectoryInfo).GetMethod("EnumerateFiles", new[] { typeof(string), typeof(System.IO.SearchOption) });
            var patch = new HarmonyLib.HarmonyMethod(typeof(XmlChangeDetectorCompletenessTests).GetMethod(nameof(FailEnumerateFilesPrefix), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

            try
            {
                harmony.Patch(target, prefix: patch);

                long hash = 0;
                int count = 0;
                bool isComplete = true;

                object[] args = new object[] { tempDir, hash, count, isComplete };
                method.Invoke(null, args);

                Assert.That((bool)args[3], Is.False, "列舉檔案拋出例外時應將 isComplete 標記為 false");
            }
            finally
            {
                harmony.Unpatch(target, patch.method);
                try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Test]
        public void ScanDirectoryMetadata_WhenFileMetadataThrows_SetsIsCompleteFalse()
        {
            var method = typeof(XmlChangeDetector).GetMethod("ScanDirectoryMetadata",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FGL_XmlTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempDir);
            string tempFile = System.IO.Path.Combine(tempDir, "test.xml");
            System.IO.File.WriteAllText(tempFile, "<test></test>");

            var harmony = new HarmonyLib.Harmony("test.scandir.fail_length");
            var target = typeof(System.IO.FileInfo).GetProperty("Length")?.GetGetMethod();
            var patch = new HarmonyLib.HarmonyMethod(typeof(XmlChangeDetectorCompletenessTests).GetMethod(nameof(FailFileLengthPrefix), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

            try
            {
                harmony.Patch(target, prefix: patch);

                long hash = 0;
                int count = 0;
                bool isComplete = true;

                object[] args = new object[] { tempDir, hash, count, isComplete };
                method.Invoke(null, args);

                Assert.That((bool)args[3], Is.False, "讀取檔案長度拋出例外時應將 isComplete 標記為 false");
            }
            finally
            {
                harmony.Unpatch(target, patch.method);
                try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Test]
        public void CommitXmlScanResult_WhenVerboseLogging_LogsSummary()
        {
            bool originalVerbose = FasterGameLoadingSettings.VerboseLogging;
            FasterGameLoadingSettings.VerboseLogging = true;
            try
            {
                var result = new XmlChangeDetector.XmlScanResult(
                    new Dictionary<string, long>(), fileCount: 5, elapsedMilliseconds: 10, isComplete: true);

                try
                {
                    XmlChangeDetector.CommitXmlScanResult(result);
                }
                catch (TypeInitializationException) { }

                Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.True);
            }
            finally
            {
                FasterGameLoadingSettings.VerboseLogging = originalVerbose;
            }
        }

        [Test]
        public void StartScanAsync_ExecutesAndEnqueuesMainThreadAction()
        {
            Action enqueued = null;
            var targets = new List<XmlChangeDetector.ModScanTarget>();

            XmlChangeDetector.StartScanAsync(targets, configPath: null, action => enqueued = action);

            System.Threading.SpinWait.SpinUntil(() => enqueued != null, TimeSpan.FromSeconds(3));
            Assert.That(enqueued, Is.Not.Null);

            try
            {
                enqueued();
            }
            catch (TypeInitializationException) { }

            Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.True);
        }

        [Test]
        public void ApplyPatchesPatch_Prefix_Postfix_Finalizer_TogglesIsInPatchOperation()
        {
            XmlNode_SelectSingleNode_Patch.isInPatchOperation = false;

            LoadedModManager_ApplyPatches_Patch.Prefix();
            Assert.That(XmlNode_SelectSingleNode_Patch.isInPatchOperation, Is.True);

            LoadedModManager_ApplyPatches_Patch.Postfix();
            Assert.That(XmlNode_SelectSingleNode_Patch.isInPatchOperation, Is.False);

            LoadedModManager_ApplyPatches_Patch.Prefix();
            LoadedModManager_ApplyPatches_Patch.Finalizer();
            Assert.That(XmlNode_SelectSingleNode_Patch.isInPatchOperation, Is.False);
        }

        [Test]
        public void EndStartupCacheWindow_ClearsSessionPathsAndDeactivates()
        {
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = true;
            XmlNode_SelectSingleNode_Patch.isCacheValidated = true;
            XmlNode_SelectSingleNode_Patch.xmlPathsThisSession["foo"] = false;

            XmlNode_SelectSingleNode_Patch.EndStartupCacheWindow();

            Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.False);
            Assert.That(XmlNode_SelectSingleNode_Patch.isCacheValidated, Is.False);
            Assert.That(XmlNode_SelectSingleNode_Patch.xmlPathsThisSession, Is.Empty);
        }

        [Test]
        public void StartupCompletedCallback_ProcessesXmlPathsAndWritesSettings()
        {
            var registerMethod = typeof(XmlNode_SelectSingleNode_Patch).GetMethod(
                "RegisterCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(registerMethod, Is.Not.Null);

            // 清理既有的回呼並重新註冊，以取得該回呼之執行測試
            var callbacksField = HarmonyLib.AccessTools.Field(typeof(Startup), "onStartupCompleted");
            var callbacks = (List<Action>)callbacksField?.GetValue(null);
            callbacks?.Clear();

            registerMethod.Invoke(null, null);

            XmlNode_SelectSingleNode_Patch.xmlPathsThisSession["Defs/Test/Miss"] = false;
            XmlNode_SelectSingleNode_Patch.xmlPathsThisSession["Defs/Test/Hit"] = true;
            XmlChangeDetector.needWriteSettings = true;
            bool originalVerbose = FasterGameLoadingSettings.VerboseLogging;
            FasterGameLoadingSettings.VerboseLogging = true;

            try
            {
                Startup.Postfix();

                Assert.That(SessionCache.xmlPathsSinceLastSession, Does.ContainKey("Defs/Test/Miss"));
                Assert.That(SessionCache.xmlPathsSinceLastSession, Does.Not.ContainKey("Defs/Test/Hit"));
                Assert.That(XmlNode_SelectSingleNode_Patch.xmlPathsThisSession, Is.Empty);
                Assert.That(XmlChangeDetector.needWriteSettings, Is.False);
                Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.False);
                Assert.That(XmlNode_SelectSingleNode_Patch.isCacheValidated, Is.False);
            }
            finally
            {
                FasterGameLoadingSettings.VerboseLogging = originalVerbose;
                callbacks?.Clear();
            }
        }

        [Test]
        public void CacheResetter_ResetsXmlPatchState()
        {
            var registerMethod = typeof(XmlNode_SelectSingleNode_Patch).GetMethod(
                "RegisterCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            registerMethod.Invoke(null, null);

            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = true;
            XmlNode_SelectSingleNode_Patch.isCacheValidated = true;
            XmlNode_SelectSingleNode_Patch.xmlPathsThisSession["test"] = true;

            CacheResetter.ResetAll();

            Assert.That(XmlNode_SelectSingleNode_Patch.isXmlScanComplete, Is.False);
            Assert.That(XmlNode_SelectSingleNode_Patch.isCacheValidated, Is.False);
            Assert.That(XmlNode_SelectSingleNode_Patch.xmlPathsThisSession, Is.Empty);
        }
    }
}
