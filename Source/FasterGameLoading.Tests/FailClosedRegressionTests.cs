using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FasterGameLoading.Tests
{
    /// <summary>
    /// Regression tests for issue #6: a failed validation scan must not
    /// enable the persisted XPath miss cache. The cache may only be used
    /// after a scan has successfully committed a baseline (fail-closed).
    /// </summary>
    [TestFixture]
    public class FailClosedRegressionTests
    {
        [SetUp]
        public void SetUp()
        {
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            SessionCache.xmlPathsSinceLastSession.Clear();
            SessionCache.xmlCombinedHashSinceLastSession = 0;
            XmlChangeDetector.needWriteSettings = false;
        }

        [TearDown]
        public void TearDown()
        {
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            SessionCache.xmlPathsSinceLastSession.Clear();
        }

        /// <summary>
        /// A scan that failed with an exception must clear the persisted
        /// miss cache and leave the cache un-validated, so the session
        /// behaves as a cold start instead of trusting stale state.
        /// </summary>
        [Test]
        public void CommitXmlScanResult_OnException_FailsClosed()
        {
            SessionCache.xmlPathsSinceLastSession.TryAdd("Defs/StaleNode", 0);

            try
            {
                XmlChangeDetector.CommitXmlScanResult(
                    new XmlChangeDetector.XmlScanResult(null, 0, 0, new Exception("simulated I/O failure")));
            }
            catch (TypeInitializationException)
            {
                // FGLLog → Verse.Log is unavailable outside the game runtime.
                // The fail-closed state changes happen before logging, so the
                // assertions below hold regardless; the finally has run too.
            }

            Assert.IsTrue(XmlNode_SelectSingleNode_Patch.isXmlScanComplete,
                "Scan lifecycle must still complete so nothing waits forever.");
            Assert.IsFalse(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                "A failed scan must not validate the cache.");
            Assert.AreEqual(0, SessionCache.xmlPathsSinceLastSession.Count,
                "Persisted misses must be cleared on scan failure (fail-closed).");
        }

        /// <summary>
        /// A successful scan must validate the cache for this session.
        /// </summary>
        [Test]
        public void CommitXmlScanResult_OnSuccess_ValidatesCache()
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FGL_Test_FailClosed_" + Guid.NewGuid());
            string defsPath = System.IO.Path.Combine(tempDir, "Defs");
            System.IO.Directory.CreateDirectory(defsPath);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(defsPath, "test.xml"),
                "<Defs><ThingDef><defName>Mock</defName></ThingDef></Defs>");

            try
            {
                XmlChangeDetector.ScanXmlFiles(new List<string> { tempDir });

                Assert.IsTrue(XmlNode_SelectSingleNode_Patch.isXmlScanComplete);
                Assert.IsTrue(XmlNode_SelectSingleNode_Patch.isCacheValidated,
                    "A successful scan must validate the cache.");
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// Even with the scan marked complete and a persisted miss present,
        /// the Prefix must not short-circuit until the cache is validated.
        /// </summary>
        [Test]
        public void Prefix_WithoutValidation_DoesNotShortCircuit()
        {
            SessionCache.xmlPathsSinceLastSession.TryAdd("Defs/NeverPresentNode", 0);
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = true;
            XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
            FasterGameLoadingSettings.XPathCaching = true;

            System.Xml.XmlNode result = null;
            bool runOriginal = XmlNode_SelectSingleNode_Patch.Prefix("Defs/NeverPresentNode", ref result);

            Assert.IsTrue(runOriginal,
                "Unvalidated cache must not short-circuit queries — the original must run.");

            XmlNode_SelectSingleNode_Patch.isCacheValidated = true;
            runOriginal = XmlNode_SelectSingleNode_Patch.Prefix("Defs/NeverPresentNode", ref result);

            Assert.IsFalse(runOriginal,
                "Once validated, a persisted miss should short-circuit as before.");
        }
    }
}
