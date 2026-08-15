using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class GenTypes_GetTypeInAnyAssemblyInt_PatchTests
    {
        [SetUp]
        public void SetUp()
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
            SessionCache.loadedTypesByFullNameSinceLastSession.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
            SessionCache.loadedTypesByFullNameSinceLastSession.Clear();
        }

        [Test]
        public void MakeCacheKey_FormatsStringCorrectly()
        {
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.MakeCacheKey(null, null), Is.EqualTo(string.Empty));
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.MakeCacheKey("MyType", null), Is.EqualTo("MyType"));
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.MakeCacheKey("MyType", string.Empty), Is.EqualTo("MyType"));
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.MakeCacheKey("MyType", "Verse"), Is.EqualTo("MyType|ns|Verse"));
        }

        [Test]
        public void Prefix_WhenCachedResultsHit_SetsResultAndStateAndReturnsFalse()
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults["TestType"] = typeof(string);

            Type result = null;
            string typeName = "TestType";

            bool shouldRunOriginal = GenTypes_GetTypeInAnyAssemblyInt_Patch.Prefix(
                ref result,
                out var state,
                ref typeName,
                null);

            Assert.That(shouldRunOriginal, Is.False);
            Assert.That(result, Is.EqualTo(typeof(string)));
            Assert.That(state.isCached, Is.True);
            Assert.That(state.originalTypeName, Is.EqualTo("TestType"));
            Assert.That(state.cacheKey, Is.EqualTo("TestType"));
        }

        [Test]
        public void Prefix_WhenCachedResultsMiss_AndSessionCacheHit_UpdatesTypeNameToFullNameAndReturnsTrue()
        {
            SessionCache.loadedTypesByFullNameSinceLastSession["ShortType"] = "System.Text.StringBuilder";

            Type result = null;
            string typeName = "ShortType";

            bool shouldRunOriginal = GenTypes_GetTypeInAnyAssemblyInt_Patch.Prefix(
                ref result,
                out var state,
                ref typeName,
                null);

            Assert.That(shouldRunOriginal, Is.True);
            Assert.That(result, Is.Null);
            Assert.That(typeName, Is.EqualTo("System.Text.StringBuilder"));
            Assert.That(state.isCached, Is.False);
            Assert.That(state.originalTypeName, Is.EqualTo("ShortType"));
        }

        [Test]
        public void Prefix_WhenCachedResultsMiss_AndSessionCacheHitWithNamespace_UpdatesTypeName()
        {
            var key = GenTypes_GetTypeInAnyAssemblyInt_Patch.MakeCacheKey("MyType", "Verse");
            SessionCache.loadedTypesByFullNameSinceLastSession[key] = "Verse.MyType";

            Type result = null;
            string typeName = "MyType";

            bool shouldRunOriginal = GenTypes_GetTypeInAnyAssemblyInt_Patch.Prefix(
                ref result,
                out var state,
                ref typeName,
                "Verse");

            Assert.That(shouldRunOriginal, Is.True);
            Assert.That(typeName, Is.EqualTo("Verse.MyType"));
            Assert.That(state.isCached, Is.False);
        }

        [Test]
        public void Postfix_WhenNotCached_WritesToCachedResultsAndLoadedTypesThisSession()
        {
            var state = (originalTypeName: "Int32", namespaceIfAmbiguous: (string)null, cacheKey: "Int32", isCached: false);

            GenTypes_GetTypeInAnyAssemblyInt_Patch.Postfix(typeof(int), state);

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.TryGetValue("Int32", out var cachedType), Is.True);
            Assert.That(cachedType, Is.EqualTo(typeof(int)));

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.TryGetValue("System.Int32", out var cachedFullNameType), Is.True);
            Assert.That(cachedFullNameType, Is.EqualTo(typeof(int)));

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession.TryGetValue("Int32", out var recordedFullName), Is.True);
            Assert.That(recordedFullName, Is.EqualTo("System.Int32"));
        }

        [Test]
        public void Postfix_WhenNotCached_AndNamespaceProvided_WritesNamespaceSpecificKeyToCachedResults()
        {
            var state = (originalTypeName: "Int32", namespaceIfAmbiguous: "System", cacheKey: "Int32|ns|System", isCached: false);

            GenTypes_GetTypeInAnyAssemblyInt_Patch.Postfix(typeof(int), state);

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.TryGetValue("Int32|ns|System", out var cachedType), Is.True);
            Assert.That(cachedType, Is.EqualTo(typeof(int)));

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.TryGetValue("System.Int32|ns|System", out var cachedFullNameNs), Is.True);
            Assert.That(cachedFullNameNs, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void Postfix_WhenAlreadyCached_DoesNotModifyCache()
        {
            var state = (originalTypeName: "Int32", namespaceIfAmbiguous: (string)null, cacheKey: "Int32", isCached: true);

            GenTypes_GetTypeInAnyAssemblyInt_Patch.Postfix(typeof(int), state);

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.ContainsKey("Int32"), Is.False);
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession.TryGetValue("Int32", out var recordedFullName), Is.True);
            Assert.That(recordedFullName, Is.EqualTo("System.Int32"));
        }

        [Test]
        public void Postfix_WhenResultIsNull_DoesNothing()
        {
            var state = (originalTypeName: "NonExistentType", namespaceIfAmbiguous: (string)null, cacheKey: "NonExistentType", isCached: false);

            Assert.DoesNotThrow(() => GenTypes_GetTypeInAnyAssemblyInt_Patch.Postfix(null, state));
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults, Is.Empty);
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession, Is.Empty);
        }

        [Test]
        public void ClearCache_RemovesAllEntries()
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults["Test"] = typeof(string);
            GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession["Test"] = "System.String";

            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults, Is.Empty);
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession, Is.Empty);
        }

        [Test]
        public void CacheResetter_ResetAll_ClearsCache()
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults["Test"] = typeof(string);
            GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession["Test"] = "System.String";

            CacheResetter.ResetAll();

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults, Is.Empty);
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.loadedTypesThisSession, Is.Empty);
        }
    }
}
