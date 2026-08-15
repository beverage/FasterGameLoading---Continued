using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Core
{
    [TestFixture]
    public class StartupTests
    {
        private FieldInfo callbacksField;

        [SetUp]
        public void SetUp()
        {
            callbacksField = AccessTools.Field(typeof(Startup), "onStartupCompleted");
            var callbacks = (List<Action>)callbacksField?.GetValue(null);
            callbacks?.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            var callbacks = (List<Action>)callbacksField?.GetValue(null);
            callbacks?.Clear();
        }

        [Test]
        public void RegisterOnStartupCompleted_WithNull_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Startup.RegisterOnStartupCompleted(null));
        }

        [Test]
        public void RegisterOnStartupCompleted_ExecutesInRegistrationOrderOnPostfix()
        {
            var executionOrder = new List<int>();

            Startup.RegisterOnStartupCompleted(() => executionOrder.Add(1));
            Startup.RegisterOnStartupCompleted(() => executionOrder.Add(2));
            Startup.RegisterOnStartupCompleted(() => executionOrder.Add(3));

            Startup.Postfix();

            Assert.That(executionOrder, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Postfix_WhenCallbackThrows_ContinuesExecutingRemainingCallbacks()
        {
            var executionOrder = new List<int>();

            Startup.RegisterOnStartupCompleted(() => executionOrder.Add(1));
            Startup.RegisterOnStartupCompleted(() => throw new InvalidOperationException("Simulated error"));
            Startup.RegisterOnStartupCompleted(() => executionOrder.Add(3));

            Assert.DoesNotThrow(() => Startup.Postfix());

            Assert.That(executionOrder, Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void Postfix_ClearsCallbacksListAfterExecution()
        {
            var executionCount = 0;
            Startup.RegisterOnStartupCompleted(() => executionCount++);

            Startup.Postfix();
            Assert.That(executionCount, Is.EqualTo(1));

            // 第二次呼叫 Postfix 不應再次執行已清除的回呼
            Startup.Postfix();
            Assert.That(executionCount, Is.EqualTo(1));
        }

        [Test]
        public void Postfix_PopulatesModsInLastSession()
        {
            SessionCache.modsInLastSession = null;

            Startup.Postfix();

            Assert.That(SessionCache.modsInLastSession, Is.Not.Null);
        }
    }
}
