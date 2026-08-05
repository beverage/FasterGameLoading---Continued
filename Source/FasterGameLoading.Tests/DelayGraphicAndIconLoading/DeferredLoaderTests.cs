using System.Collections.Generic;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading
{
    [TestFixture]
    public class DeferredLoaderTests
    {
        [Test]
        public void LoadDeferredGraphicsCoroutine_ExposesEnumeratorEntryPoint()
        {
            var iterator = DeferredLoader.LoadDeferredGraphicsCoroutine(
                null, new List<ThingDef>());

            Assert.That(iterator, Is.Not.Null);
        }

        [Test]
        public void LoadDeferredIconsCoroutine_ExposesEnumeratorEntryPoint()
        {
            var iterator = DeferredLoader.LoadDeferredIconsCoroutine(null);

            Assert.That(iterator, Is.Not.Null);
        }

        [Test]
        public void ResolveSubSoundDefsCoroutine_ExposesEnumeratorEntryPoint()
        {
            var iterator = DeferredLoader.ResolveSubSoundDefsCoroutine(null);

            Assert.That(iterator, Is.Not.Null);
        }
    }
}
