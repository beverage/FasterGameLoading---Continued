using System;
using System.Reflection;
using NUnit.Framework;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class AccessTools_TypeByName_PatchTests
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
        public void Prefix_WhenNullOrEmptyName_ReturnsTrueAndSetsState()
        {
            Type result = null;
            string name = null;
            bool shouldRun = AccessTools_TypeByName_Patch.Prefix(ref result, out var state, ref name);

            Assert.That(shouldRun, Is.True);
            Assert.That(state.isCached, Is.False);
            Assert.That(state.originalName, Is.Null);
        }

        [Test]
        public void Prefix_WhenCachedResultsHit_SetsResultAndReturnsFalse()
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults["TestClass"] = typeof(string);

            Type result = null;
            string name = "TestClass";
            bool shouldRun = AccessTools_TypeByName_Patch.Prefix(ref result, out var state, ref name);

            Assert.That(shouldRun, Is.False);
            Assert.That(result, Is.EqualTo(typeof(string)));
            Assert.That(state.isCached, Is.True);
            Assert.That(state.originalName, Is.EqualTo("TestClass"));
        }

        [Test]
        public void Prefix_WhenSessionCacheHit_UpdatesNameToFullNameAndReturnsTrue()
        {
            SessionCache.loadedTypesByFullNameSinceLastSession["ShortClass"] = "System.Text.StringBuilder";

            Type result = null;
            string name = "ShortClass";
            bool shouldRun = AccessTools_TypeByName_Patch.Prefix(ref result, out var state, ref name);

            Assert.That(shouldRun, Is.True);
            Assert.That(name, Is.EqualTo("System.Text.StringBuilder"));
            Assert.That(state.isCached, Is.False);
            Assert.That(state.originalName, Is.EqualTo("ShortClass"));
        }

        [Test]
        public void Postfix_WhenNotCachedAndResultNotNull_CachesResults()
        {
            var state = (isCached: false, originalName: "String");
            AccessTools_TypeByName_Patch.Postfix(typeof(string), "String", state);

            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.TryGetValue("String", out var type), Is.True);
            Assert.That(type, Is.EqualTo(typeof(string)));
            Assert.That(GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults.TryGetValue("System.String", out var typeFull), Is.True);
            Assert.That(typeFull, Is.EqualTo(typeof(string)));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession.TryGetValue("String", out var fullName), Is.True);
            Assert.That(fullName, Is.EqualTo("System.String"));
        }

        [Test]
        public void Postfix_WhenAlreadyCached_DoesNotOverwriteOrThrow()
        {
            var state = (isCached: true, originalName: "String");
            Assert.DoesNotThrow(() => AccessTools_TypeByName_Patch.Postfix(typeof(string), "String", state));
        }

        [Test]
        public void Postfix_WhenResultIsNull_DoesNotThrow()
        {
            var state = (isCached: false, originalName: "NonExistentType");
            Assert.DoesNotThrow(() => AccessTools_TypeByName_Patch.Postfix(null, "NonExistentType", state));
        }

        private sealed class MockTypeWithoutFullName : TypeDelegator
        {
            public MockTypeWithoutFullName() : base(typeof(string)) { }
            public override string FullName => null;
        }

        [Test]
        public void Postfix_WhenResultFullNameIsNull_DoesNotThrowAndDoesNotCrash()
        {
            var mockType = new MockTypeWithoutFullName();
            var state = (isCached: false, originalName: "SpecialGenericParam");

            Assert.DoesNotThrow(() => AccessTools_TypeByName_Patch.Postfix(mockType, "SpecialGenericParam", state));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession.ContainsKey("SpecialGenericParam"), Is.False);
        }
    }
}
