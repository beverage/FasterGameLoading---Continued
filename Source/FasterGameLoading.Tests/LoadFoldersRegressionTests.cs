using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FasterGameLoading.Tests
{
    /// <summary>
    /// Regression tests for issue #5: the XML change detector used to probe
    /// only &lt;modRoot&gt;/Defs and &lt;modRoot&gt;/Patches, so a mod whose content
    /// lives under a versioned folder (1.6/Defs) or an arbitrarily deep
    /// LoadFolders.xml path (1.6/ModSupport/Royalty/Defs) hashed to 0 and
    /// could never invalidate the XPath miss cache.
    ///
    /// The fix stops guessing the layout: content roots now come from the
    /// engine's own ModContentPack.foldersToLoadDescendingOrder. These tests
    /// therefore supply roots explicitly, exactly as the engine would.
    /// </summary>
    [TestFixture]
    public class LoadFoldersRegressionTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FGL_Test_LoadFolders_" + Guid.NewGuid());
        }

        [TearDown]
        public void TearDown()
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }

        private void WriteXml(string relativeDir, string fileName, string content)
        {
            string dir = System.IO.Path.Combine(tempDir, relativeDir);
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, fileName), content);
        }

        private static XmlChangeDetector.ModScanTarget Target(string key, params string[] roots)
        {
            return new XmlChangeDetector.ModScanTarget(key, roots);
        }

        /// <summary>
        /// The layout used by VEF and every Vanilla Expanded module.
        /// </summary>
        [Test]
        public void Scan_VersionedFolderRoot_ProducesNonZeroHash()
        {
            WriteXml(System.IO.Path.Combine("1.6", "Defs"), "test.xml",
                "<Defs><ThingDef><defName>Mock</defName></ThingDef></Defs>");

            var result = XmlChangeDetector.ScanXmlMetadata(new List<XmlChangeDetector.ModScanTarget>
            {
                Target("mod", tempDir, System.IO.Path.Combine(tempDir, "1.6"))
            });

            Assert.AreNotEqual(0, result.MetadataHashes["mod"],
                "A version-foldered mod's XML was invisible to the scanner, so updates to it " +
                "could never invalidate the XPath cache.");
        }

        /// <summary>
        /// The case PR #7's one-level probe still could not reach: a
        /// LoadFolders.xml entry with more than one path segment. Real mods
        /// ship content at exactly this depth.
        /// </summary>
        [Test]
        public void Scan_DeepLoadFoldersPath_ProducesNonZeroHash()
        {
            WriteXml(System.IO.Path.Combine("1.6", "ModSupport", "Royalty", "Defs"), "compat.xml",
                "<Defs><ThingDef><defName>Deep</defName></ThingDef></Defs>");

            var deepRoot = System.IO.Path.Combine(tempDir, "1.6", "ModSupport", "Royalty");

            var result = XmlChangeDetector.ScanXmlMetadata(new List<XmlChangeDetector.ModScanTarget>
            {
                Target("mod", tempDir, deepRoot)
            });

            Assert.AreNotEqual(0, result.MetadataHashes["mod"],
                "Content under a multi-segment LoadFolders path was invisible to the scanner.");
        }

        /// <summary>
        /// An update inside a deep load folder must move the hash, or the
        /// stale-cache guard never fires for the mods most likely to update.
        /// </summary>
        [Test]
        public void Scan_DeepLoadFoldersUpdate_ChangesHash()
        {
            string relDir = System.IO.Path.Combine("1.6", "ModSupport", "Royalty", "Patches");
            WriteXml(relDir, "patch.xml", "<Patch><Operation Class=\"PatchOperationConditional\" /></Patch>");

            string patchFile = System.IO.Path.Combine(tempDir, relDir, "patch.xml");
            var deepRoot = System.IO.Path.Combine(tempDir, "1.6", "ModSupport", "Royalty");
            var targets = new List<XmlChangeDetector.ModScanTarget> { Target("mod", tempDir, deepRoot) };

            long before = XmlChangeDetector.ScanXmlMetadata(targets).MetadataHashes["mod"];

            // Simulate a Steam update: same modlist, changed content.
            System.IO.File.SetLastWriteTimeUtc(patchFile, DateTime.UtcNow.AddSeconds(10));
            System.IO.File.WriteAllText(patchFile,
                "<Patch><Operation Class=\"PatchOperationConditional\"><match /></Operation></Patch>");

            long after = XmlChangeDetector.ScanXmlMetadata(targets).MetadataHashes["mod"];

            Assert.AreNotEqual(before, after,
                "An update inside a deep load folder did not change the hash; the persisted " +
                "XPath miss cache would be reused against changed XML.");
        }

        /// <summary>
        /// The fold over roots is order-sensitive, so the scanner must sort
        /// them. Without this, a caller or filesystem returning roots in a
        /// different order produces a different hash for identical content and
        /// spuriously discards the whole cache.
        /// </summary>
        [Test]
        public void Scan_RootOrder_DoesNotAffectHash()
        {
            WriteXml(System.IO.Path.Combine("1.6", "Defs"), "a.xml", "<Defs />");
            WriteXml(System.IO.Path.Combine("Common", "Defs"), "b.xml", "<Defs />");

            string versioned = System.IO.Path.Combine(tempDir, "1.6");
            string common = System.IO.Path.Combine(tempDir, "Common");

            long forward = XmlChangeDetector.ScanXmlMetadata(new List<XmlChangeDetector.ModScanTarget>
            {
                Target("mod", tempDir, versioned, common)
            }).MetadataHashes["mod"];

            long reversed = XmlChangeDetector.ScanXmlMetadata(new List<XmlChangeDetector.ModScanTarget>
            {
                Target("mod", common, versioned, tempDir)
            }).MetadataHashes["mod"];

            Assert.AreEqual(forward, reversed,
                "Root enumeration order changed the metadata hash, which would spuriously " +
                "invalidate the XPath cache whenever the order shifted.");
        }

        /// <summary>
        /// The engine's root list always contains RootDir, and a LoadFolders
        /// entry can name it again. A duplicate root must not be hashed twice.
        /// </summary>
        [Test]
        public void Scan_DuplicateRoots_AreCountedOnce()
        {
            WriteXml("Defs", "a.xml", "<Defs />");

            long once = XmlChangeDetector.ScanXmlMetadata(new List<XmlChangeDetector.ModScanTarget>
            {
                Target("mod", tempDir)
            }).MetadataHashes["mod"];

            long twice = XmlChangeDetector.ScanXmlMetadata(new List<XmlChangeDetector.ModScanTarget>
            {
                Target("mod", tempDir, tempDir)
            }).MetadataHashes["mod"];

            Assert.AreEqual(once, twice, "A duplicated content root was hashed twice.");
        }

        /// <summary>
        /// The string-path convenience overload must keep behaving exactly as
        /// before for callers that pass a bare mod root.
        /// </summary>
        [Test]
        public void TargetsFromPaths_PreservesSingleRootBehaviour()
        {
            WriteXml("Defs", "a.xml", "<Defs />");

            var targets = XmlChangeDetector.TargetsFromPaths(new List<string> { tempDir });

            Assert.AreEqual(1, targets.Count);
            Assert.AreEqual(tempDir.ToLowerInvariant(), targets[0].Key);
            CollectionAssert.AreEqual(new List<string> { tempDir }, targets[0].Roots);
        }
    }
}
