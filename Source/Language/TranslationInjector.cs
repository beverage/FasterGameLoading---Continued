using System;
using System.IO;
using System.Xml;
using Verse;

namespace FasterGameLoading
{
    internal static class TranslationInjector
    {
        /// <summary>
        /// 手動注入來自 LanguageData/ 資料夾的翻譯 Key。
        /// 
        /// 此模組可能在 Core 完成語言資料初始化之前載入；若將翻譯放在原版自動掃描的
        /// Languages/ 資料夾，便可能過早注入翻譯並影響後續的大小寫規則。
        /// 
        /// 為了避免這個問題，我們將 Languages/ 重新命名為 LanguageData/（RimWorld 不會自動載入此資料夾），
        /// 由 Startup.Postfix 呼叫此方法注入翻譯，缺少對應語系時退回英文。
        /// </summary>
        internal static void InjectTranslations()
        {
            try
            {
                var mod = LoadedModManager.GetMod<FasterGameLoadingMod>();
                if (mod?.Content == null)
                    return;

                var modContentPack = mod.Content;
                var languageDataDir = Path.Combine(modContentPack.RootDir, "LanguageData");
                if (!Directory.Exists(languageDataDir))
                    return;

                var activeLanguage = LanguageDatabase.activeLanguage;
                if (activeLanguage == null)
                    return;

                string langFolderPath = null;
                var langFolderName = activeLanguage.folderName;

                var candidatePath = Path.Combine(languageDataDir, langFolderName);
                if (Directory.Exists(candidatePath))
                {
                    langFolderPath = candidatePath;
                }
                else
                {
                    var englishPath = Path.Combine(languageDataDir, "English");
                    if (Directory.Exists(englishPath))
                    {
                        langFolderPath = englishPath;
                    }
                }

                if (langFolderPath == null)
                    return;

                var keyedDir = Path.Combine(langFolderPath, "Keyed");
                if (!Directory.Exists(keyedDir))
                    return;

                foreach (var xmlFile in Directory.GetFiles(keyedDir, "*.xml"))
                {
                    LoadKeyedTranslationsFromFile(xmlFile, activeLanguage);
                }
            }
            catch (IOException ex)
            {
                FGLLog.Error("Error injecting translations:", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                FGLLog.Error("Error injecting translations:", ex);
            }
        }

        internal static void LoadKeyedTranslationsFromFile(string filePath, LoadedLanguage language)
        {
            var doc = new XmlDocument();
            // 個別捕捉 XmlException，避免單一損毀的翻譯檔案中斷整批注入
            try
            {
                doc.Load(filePath);
            }
            catch (Exception ex)
            {
                FGLLog.Warning($"Failed to load translation file (skipped): {filePath}\n{ex.Message}");
                return;
            }
            var root = doc.DocumentElement;
            if (root == null)
                return;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType is not XmlNodeType.Element)
                    continue;

                var key = node.Name;
                var value = node.InnerText;

                if (!language.keyedReplacements.ContainsKey(key))
                {
                    language.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
                    {
                        key = key,
                        value = value,
                        fileSource = filePath,
                        fileSourceFullPath = filePath,
                        isPlaceholder = false,
                    };
                }
            }
        }
    }
}

