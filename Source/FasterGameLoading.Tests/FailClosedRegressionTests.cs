using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FasterGameLoading.Tests
{
    /// <summary>
    /// Regression tests for issue #6: the persisted XPath miss cache was
    /// enabled whenever the background scan FINISHED, not whenever it
    /// SUCCEEDED. A failed or bypassed scan left isXmlScanComplete true with
    /// no validated baseline, so last session's misses were replayed against
    /// XML that may have changed in the meantime.
    ///
    /// The gate is isCacheValidated, which is set only on a successful commit
    /// and reset explicitly on scan start, failure, bypass and startup
    /// completion.
    /// </summary>
    [TestFixture]
    public class FailClosedRegressionTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FGL_Test_FailClosed_" + Guid.NewGuid());
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
            SessionCache.xmlPathsSinceLastSession.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            SessionCache.xmlPathsSinceLastSession.Clear();
        }

        /// <summary>
        /// Commits a failed scan, tolerating the logger blowing up.
        ///
        /// FGLLog reaches Verse.Log and Verse.UnityData, which cannot
        /// initialise outside a running game. That is precisely why the
        /// production code performs the fail-closed clear BEFORE it logs — so
        /// swallowing the logger's exception here still leaves the safety
        /// action observable, and these tests pin that ordering.
        /// </summary>
        private static void CommitFailedScan()
        {
            try
            {
                XmlChangeDetector.CommitXmlScanResult(
                    new XmlChangeDetector.XmlScanResult(null, 0, 0, new InvalidOperationException("scan blew up")));
            }
            catch (TypeInitializationException)
            {
                // Verse static state is unavailable in the test host.
            }
        }

        /// <summary>
        /// A scan that throws must leave the cache unvalidated AND drop the
        /// persisted misses, so the session degrades to cold-start semantics
        /// instead of trusting an unverified baseline.
        /// </summary>
        [Test]
        public void FailedScan_DoesNotValidate_AndClearsPersistedMisses()
        {
            SessionCache.xmlPathsSinceLastSession.TryAdd("Defs/ThingDef[defName=\"Stale\"]/comps", 0);

            CommitFailedScan();

            Assert.IsFalse(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                "A failed scan validated the cache; stale misses would be replayed against changed XML.");
            Assert.AreEqual(0, SessionCache.xmlPathsSinceLastSession.Count,
                "A failed scan left persisted misses in place instead of falling back to cold start.");
            Assert.IsTrue(XmlNode_SelectSingleNode_Patch.isXmlScanComplete,
                "The scan must still be marked complete so nothing waits on it forever.");
        }

        /// <summary>
        /// The fail-closed clear must happen even if logging throws. The
        /// production code deliberately orders the safety action first; this
        /// pins that ordering.
        /// </summary>
        [Test]
        public void FailedScan_ClearsMisses_EvenIfLoggingThrows()
        {
            SessionCache.xmlPathsSinceLastSession.TryAdd("Defs/ThingDef[defName=\"Stale\"]/comps", 0);

            CommitFailedScan();

            Assert.IsFalse(XmlNode_SelectSingleNode_Patch.isCacheValidated);
            Assert.AreEqual(0, SessionCache.xmlPathsSinceLastSession.Count,
                "The fail-closed clear was skipped because logging threw first.");
        }

        /// <summary>
        /// A bypassed scan is equally unvalidated — but a bypass is a
        /// deliberate stand-aside, so it must NOT discard the persisted cache.
        /// </summary>
        [Test]
        public void BypassedScan_DoesNotValidate_ButKeepsPersistedMisses()
        {
            SessionCache.xmlPathsSinceLastSession.TryAdd("Defs/ThingDef[defName=\"Kept\"]/comps", 0);
            XmlNode_SelectSingleNode_Patch.isCacheValidated = true;

            XmlChangeDetector.CommitXmlScanResult(
                new XmlChangeDetector.XmlScanResult(null, 0, 0, null, bypassed: true));

            Assert.IsFalse(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                "A bypassed scan left the cache marked validated.");
            Assert.AreEqual(1, SessionCache.xmlPathsSinceLastSession.Count,
                "A bypass discarded the persisted cache; it should only stand aside.");
        }

        /// <summary>
        /// The success path must actually enable the cache, or the fix would
        /// silently disable the feature outright.
        /// </summary>
        [Test]
        public void SuccessfulScan_ValidatesCache()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(tempDir, "Defs"));
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "Defs", "a.xml"), "<Defs />");

            XmlChangeDetector.ScanXmlFiles(new List<string> { tempDir });

            Assert.IsTrue(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                "A successful scan did not validate the cache, disabling the feature entirely.");
        }

        /// <summary>
        /// Starting a scan must clear a previously validated state, so a
        /// second scan in the same process cannot inherit the first one's
        /// verdict while its own result is still pending.
        /// </summary>
        [Test]
        public void StartingScan_ResetsPreviousValidation()
        {
            XmlNode_SelectSingleNode_Patch.isCacheValidated = true;

            // Nothing to scan: this bypasses, so validation must not survive.
            XmlChangeDetector.ScanXmlFiles(new List<string>());

            Assert.IsFalse(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                "A new scan inherited the previous scan's validated state.");
        }

        /// <summary>
        /// Ending the startup window must drop validation along with the rest
        /// of the interception state, so runtime XML queries can never be
        /// answered from the cache.
        /// </summary>
        [Test]
        public void EndStartupCacheWindow_ClearsValidation()
        {
            XmlNode_SelectSingleNode_Patch.isCacheValidated = true;

            XmlNode_SelectSingleNode_Patch.EndStartupCacheWindow();

            Assert.IsFalse(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                "Validation outlived the startup window; runtime queries could be answered from cache.");
        }
    }
}
