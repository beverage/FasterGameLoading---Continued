using System.Collections.Generic;
using System.IO;
using System.Xml;
using RimTestRedux;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// FGL 的翻譯放在原版不會掃描的 LanguageData/，由 TranslationInjector 在 CallAll 後手動注入目前語言；
    /// 該語言沒有對應資料夾時退回英文。第 1 輪（英文）走直接對應，第 2 輪切換到沒有 FGL 翻譯的語言，走英文退回。
    /// </summary>
    [TestSuite]
    internal static class TranslationTests
    {
        [Test]
        public static void FglKeyedStringsAreInjectedForActiveLanguage()
        {
            var language = LanguageDatabase.activeLanguage;
            var keyedDir = ExpectedKeyedDirectory(language);
            Assert.That(Directory.Exists(keyedDir)).Is.True();

            var failures = new List<string>();
            int injectedFromFgl = 0;
            foreach (var file in Directory.GetFiles(keyedDir, "*.xml"))
            {
                var doc = new XmlDocument();
                doc.Load(file);
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element) continue;
                    if (!language.keyedReplacements.TryGetValue(node.Name, out var replacement))
                    {
                        failures.Add($"{node.Name} missing");
                        continue;
                    }
                    // 其他來源先定義的鍵刻意不覆寫，只檢查由 FGL 檔案注入的值；路徑正規化後比對，不依賴注入端的字串格式。
                    if (replacement.fileSource == null || NormalizedFullPath(replacement.fileSource) != NormalizedFullPath(file)) continue;
                    injectedFromFgl++;
                    if (replacement.value != node.InnerText)
                    {
                        failures.Add($"{node.Name}: '{replacement.value}', file '{node.InnerText}'");
                    }
                }
            }
            FglState.AssertNone(failures, $"FGL keyed strings not injected for {language.folderName}");
            Assert.That(injectedFromFgl).Is.GreaterThan(0);
        }

        private static string NormalizedFullPath(string path)
            => Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();

        /// <summary>與 TranslationInjector 相同的選擇規則：語言資料夾存在就用它，否則用英文。</summary>
        private static string ExpectedKeyedDirectory(LoadedLanguage language)
        {
            var languageData = Path.Combine(FasterGameLoadingMod.Instance.Content.RootDir, "LanguageData");
            var candidate = Path.Combine(languageData, language.folderName);
            var folder = Directory.Exists(candidate) ? candidate : Path.Combine(languageData, "English");
            return Path.Combine(folder, "Keyed");
        }
    }
}
