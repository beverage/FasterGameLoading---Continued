using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Settings
{
    [TestFixture]
    public class FasterGameLoadingSettingsTests
    {
        private bool origVerboseLogging;
        private bool origDelayGraphicLoading;
        private bool origEarlyModContentLoading;
        private bool origStaticAtlasesBaking;
        private bool origEnableMultiThreading;
        private bool origXPathCaching;

        [SetUp]
        public void SetUp()
        {
            origVerboseLogging = FasterGameLoadingSettings.VerboseLogging;
            origDelayGraphicLoading = FasterGameLoadingSettings.DelayGraphicLoading;
            origEarlyModContentLoading = FasterGameLoadingSettings.earlyModContentLoading;
            origStaticAtlasesBaking = FasterGameLoadingSettings.StaticAtlasesBaking;
            origEnableMultiThreading = FasterGameLoadingSettings.EnableMultiThreading;
            origXPathCaching = FasterGameLoadingSettings.XPathCaching;
        }

        [TearDown]
        public void TearDown()
        {
            FasterGameLoadingSettings.VerboseLogging = origVerboseLogging;
            FasterGameLoadingSettings.DelayGraphicLoading = origDelayGraphicLoading;
            FasterGameLoadingSettings.earlyModContentLoading = origEarlyModContentLoading;
            FasterGameLoadingSettings.StaticAtlasesBaking = origStaticAtlasesBaking;
            FasterGameLoadingSettings.EnableMultiThreading = origEnableMultiThreading;
            FasterGameLoadingSettings.XPathCaching = origXPathCaching;
        }

        [Test]
        public void VerboseLogging_PropertyGetSet()
        {
            FasterGameLoadingSettings.VerboseLogging = true;
            Assert.That(FasterGameLoadingSettings.VerboseLogging, Is.True);

            FasterGameLoadingSettings.VerboseLogging = false;
            Assert.That(FasterGameLoadingSettings.VerboseLogging, Is.False);
        }

        [Test]
        public void DelayGraphicLoading_PropertyGetSet()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = true;
            Assert.That(FasterGameLoadingSettings.DelayGraphicLoading, Is.True);

            FasterGameLoadingSettings.DelayGraphicLoading = false;
            Assert.That(FasterGameLoadingSettings.DelayGraphicLoading, Is.False);
        }

        [Test]
        public void EarlyModContentLoading_FieldGetSet()
        {
            FasterGameLoadingSettings.earlyModContentLoading = true;
            Assert.That(FasterGameLoadingSettings.earlyModContentLoading, Is.True);

            FasterGameLoadingSettings.earlyModContentLoading = false;
            Assert.That(FasterGameLoadingSettings.earlyModContentLoading, Is.False);
        }

        [Test]
        public void StaticAtlasesBaking_PropertyGetSet()
        {
            FasterGameLoadingSettings.StaticAtlasesBaking = true;
            Assert.That(FasterGameLoadingSettings.StaticAtlasesBaking, Is.True);

            FasterGameLoadingSettings.StaticAtlasesBaking = false;
            Assert.That(FasterGameLoadingSettings.StaticAtlasesBaking, Is.False);
        }

        [Test]
        public void EnableMultiThreading_PropertyGetSet()
        {
            FasterGameLoadingSettings.EnableMultiThreading = true;
            Assert.That(FasterGameLoadingSettings.EnableMultiThreading, Is.True);

            FasterGameLoadingSettings.EnableMultiThreading = false;
            Assert.That(FasterGameLoadingSettings.EnableMultiThreading, Is.False);
        }

        [Test]
        public void XPathCaching_PropertyGetSet()
        {
            FasterGameLoadingSettings.XPathCaching = true;
            Assert.That(FasterGameLoadingSettings.XPathCaching, Is.True);

            FasterGameLoadingSettings.XPathCaching = false;
            Assert.That(FasterGameLoadingSettings.XPathCaching, Is.False);
        }

        [Test]
        public void ExposeData_ExecutesSuccessfully()
        {
            var settings = new FasterGameLoadingSettings();
            Assert.DoesNotThrow(() => settings.ExposeData());
        }

        [Test]
        public void ExposeData_WhenCacheManagerPresent_ExposesResizedTextureCache()
        {
            var settings = new FasterGameLoadingSettings();
            var mod = (FasterGameLoadingMod)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(FasterGameLoadingMod));
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fgl_settings_test_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempDir);
            try
            {
                var mgr = new TextureCacheManager(tempDir);
                // 設為 null，以測試 if (cacheManager.resizedTextureCache == null) 分支
                mgr.resizedTextureCache = null;

                HarmonyLib.AccessTools.PropertySetter(typeof(FasterGameLoadingMod), nameof(FasterGameLoadingMod.CacheManager))
                    ?.Invoke(mod, new object[] { mgr });
                HarmonyLib.AccessTools.PropertySetter(typeof(FasterGameLoadingMod), nameof(FasterGameLoadingMod.Instance))
                    ?.Invoke(null, new object[] { mod });

                settings.ExposeData();

                Assert.That(mgr.resizedTextureCache, Is.Not.Null);
            }
            finally
            {
                HarmonyLib.AccessTools.PropertySetter(typeof(FasterGameLoadingMod), nameof(FasterGameLoadingMod.Instance))
                    ?.Invoke(null, new object[] { null });
                try { System.IO.Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static bool Prefix_Skip() => false;
        private static bool Prefix_CheckboxLabeled(ref bool checkOn) => false;
        private static bool Prefix_ButtonText(ref bool __result)
        {
            __result = true;
            return false;
        }
        private static bool Prefix_GetRect(ref UnityEngine.Rect __result)
        {
            __result = new UnityEngine.Rect(0, 0, 100, 20);
            return false;
        }
        private static bool Prefix_CalcHeight(ref float __result)
        {
            __result = 20f;
            return false;
        }
        private static bool Prefix_WindowStackAdd(Verse.Window window)
        {
            if (window is Verse.Dialog_MessageBox box)
            {
                // 執行確認委派以覆蓋確認按鈕的處理邏輯
                var confirmedAct = (Action)HarmonyLib.AccessTools.Field(typeof(Verse.Dialog_MessageBox), "confirmedAct")?.GetValue(box);
                try { confirmedAct?.Invoke(); } catch { }
            }
            return false;
        }
        private static bool Prefix_WindowStack(ref Verse.WindowStack __result)
        {
            __result = (Verse.WindowStack)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.WindowStack));
            return false;
        }

        [Test]
        public void DrawOptions_InvokesCheckboxLabeledSafely()
        {
            var harmony = new HarmonyLib.Harmony("test.settings.drawoptions");

            foreach (var m in typeof(Verse.Listing_Standard).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name == nameof(Verse.Listing_Standard.CheckboxLabeled))
                {
                    harmony.Patch(m, prefix: new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(FasterGameLoadingSettingsTests), nameof(Prefix_Skip))));
                }
            }

            try
            {
                var ls = (Verse.Listing_Standard)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Listing_Standard));
                var drawLoading = typeof(FasterGameLoadingSettings).GetMethod("DrawLoadingOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var drawDiag = typeof(FasterGameLoadingSettings).GetMethod("DrawDiagnosticsOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                Assert.DoesNotThrow(() => drawLoading?.Invoke(null, new object[] { ls }));
                Assert.DoesNotThrow(() => drawDiag?.Invoke(null, new object[] { ls }));
            }
            finally
            {
                harmony.UnpatchAll("test.settings.drawoptions");
            }
        }
    }
}
