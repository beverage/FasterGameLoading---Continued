using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.Language
{
    [TestFixture]
    public class TranslationInjectorTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "FGL_Translations_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex)
            {
                Assert.Fail("清理翻譯測試暫存目錄失敗：" + tempDir + Environment.NewLine + ex);
            }
        }

        [Test]
        public void LoadKeyedTranslationsFromFile_LoadsElementsAndPreservesExistingKeys()
        {
            var path = Path.Combine(tempDir, "Keyed.xml");
            File.WriteAllText(path, "<LanguageData><Greeting>Hello</Greeting><Farewell>Bye <b>friend</b></Farewell></LanguageData>");
            var language = (LoadedLanguage)CreateLanguage();
            language.keyedReplacements["Greeting"] = new LoadedLanguage.KeyedReplacement
            {
                key = "Greeting",
                value = "Existing",
            };

            TranslationInjector.LoadKeyedTranslationsFromFile(path, language);

            Assert.That(language.keyedReplacements["Greeting"].value, Is.EqualTo("Existing"));
            Assert.That(language.keyedReplacements["Farewell"].value, Is.EqualTo("Bye friend"));
            Assert.That(language.keyedReplacements["Farewell"].fileSource, Is.EqualTo(path));
        }

        [Test]
        public void LoadKeyedTranslationsFromFile_WithCorruptedXml_IsSafelyIgnoredAndDoesNotThrow()
        {
            var path = Path.Combine(tempDir, "Corrupt.xml");
            File.WriteAllText(path, "<LanguageData><UnclosedTag>Content</LanguageData>");
            var language = (LoadedLanguage)CreateLanguage();

            Assert.DoesNotThrow(() => TranslationInjector.LoadKeyedTranslationsFromFile(path, language));
            Assert.That(language.keyedReplacements, Is.Empty);
        }

        [Test]
        public void LoadKeyedTranslationsFromFile_WithNonExistentFile_IsSafelyIgnored()
        {
            var path = Path.Combine(tempDir, "DoesNotExist.xml");
            var language = (LoadedLanguage)CreateLanguage();

            Assert.DoesNotThrow(() => TranslationInjector.LoadKeyedTranslationsFromFile(path, language));
            Assert.That(language.keyedReplacements, Is.Empty);
        }

        [Test]
        public void LoadKeyedTranslationsFromFile_WithCommentsAndWhitespace_OnlyExtractsElementNodes()
        {
            var path = Path.Combine(tempDir, "WithComments.xml");
            File.WriteAllText(path, "<LanguageData>\n  <!-- Comment line -->\n  <OptionA>Alpha</OptionA>\n  <!-- Another comment -->\n</LanguageData>");
            var language = (LoadedLanguage)CreateLanguage();

            TranslationInjector.LoadKeyedTranslationsFromFile(path, language);

            Assert.That(language.keyedReplacements, Has.Count.EqualTo(1));
            Assert.That(language.keyedReplacements["OptionA"].value, Is.EqualTo("Alpha"));
        }

        [Test]
        public void InjectTranslations_WhenLanguageDataMissing_ReturnsSafelyWithoutThrowing()
        {
            Assert.DoesNotThrow(() => TranslationInjector.InjectTranslations());
        }

        private static object CreateLanguage()
        {
            var language = (LoadedLanguage)FormatterServices.GetUninitializedObject(typeof(LoadedLanguage));
            language.keyedReplacements = new Dictionary<string, LoadedLanguage.KeyedReplacement>();
            return language;
        }
    }
}
