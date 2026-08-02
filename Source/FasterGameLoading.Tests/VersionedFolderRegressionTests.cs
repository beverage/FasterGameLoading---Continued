using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FasterGameLoading.Tests
{
    /// <summary>
    /// Regression tests for issue #5: XmlChangeDetector only probes
    /// &lt;modRoot&gt;/Defs and &lt;modRoot&gt;/Patches, so mods using versioned
    /// load folders (&lt;modRoot&gt;/1.6/Defs) — including all of Vanilla
    /// Expanded — hash to 0 and never invalidate the XPath miss cache.
    /// </summary>
    [TestFixture]
    public class VersionedFolderRegressionTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FGL_Test_VersionedMod_" + Guid.NewGuid());
        }

        [TearDown]
        public void TearDown()
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// A mod whose only content lives under a versioned folder
        /// (1.6/Defs, the layout used by VEF and every VE module)
        /// must still produce a non-zero metadata hash.
        /// </summary>
        [Test]
        public void ScanXmlFiles_VersionedFolderLayout_ProducesNonZeroHash()
        {
            // Arrange: content under <root>/1.6/Defs — NOT <root>/Defs
            string defsPath = System.IO.Path.Combine(tempDir, "1.6", "Defs");
            System.IO.Directory.CreateDirectory(defsPath);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(defsPath, "test.xml"),
                "<Defs><ThingDef><defName>Mock</defName></ThingDef></Defs>");

            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
            SessionCache.xmlCombinedHashSinceLastSession = 0;
            XmlChangeDetector.needWriteSettings = false;

            // Act
            XmlChangeDetector.ScanXmlFiles(new List<string> { tempDir });

            // Assert: the versioned content must be seen
            Assert.AreNotEqual(0, SessionCache.xmlCombinedHashSinceLastSession,
                "A version-foldered mod's XML content was invisible to the scanner: " +
                "its metadata hash is 0, so updates to it can never invalidate the XPath cache.");
        }

        /// <summary>
        /// Updating a file inside a versioned folder must change the
        /// combined hash, or the stale-cache guard never fires for
        /// exactly the mods most likely to be updated.
        /// </summary>
        [Test]
        public void ScanXmlFiles_VersionedFolderUpdate_ChangesHash()
        {
            // Arrange
            string patchesPath = System.IO.Path.Combine(tempDir, "1.6", "Patches");
            System.IO.Directory.CreateDirectory(patchesPath);
            string patchFile = System.IO.Path.Combine(patchesPath, "patch.xml");
            System.IO.File.WriteAllText(patchFile, "<Patch><Operation Class=\"PatchOperationConditional\" /></Patch>");

            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
            SessionCache.xmlCombinedHashSinceLastSession = 0;
            XmlChangeDetector.needWriteSettings = false;

            XmlChangeDetector.ScanXmlFiles(new List<string> { tempDir });
            long hashBefore = SessionCache.xmlCombinedHashSinceLastSession;

            // Act: simulate a Steam update — same modlist, changed content
            XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
            System.IO.File.SetLastWriteTimeUtc(patchFile, DateTime.UtcNow.AddSeconds(10));
            System.IO.File.WriteAllText(patchFile, "<Patch><Operation Class=\"PatchOperationConditional\"><match /></Operation></Patch>");

            XmlChangeDetector.ScanXmlFiles(new List<string> { tempDir });
            long hashAfter = SessionCache.xmlCombinedHashSinceLastSession;

            // Assert
            Assert.AreNotEqual(hashBefore, hashAfter,
                "An update to a version-foldered mod's Patches did not change the combined hash; " +
                "the persisted XPath miss cache will be reused against changed XML.");
        }
    }
}
