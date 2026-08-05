# NuGet Central Package Management 轉換報告

## 1. 轉換概述

- 轉換範圍：`Source/FasterGameLoading.sln`。
- 轉換專案：2 個 `net472` SDK-style 專案，包括主專案與測試專案。
- 集中管理套件：6 個 NuGet 套件。
- 略過專案或套件：無。
- MSBuild 套件版本屬性：未發現需要內嵌或移除的版本屬性。
- 新增 `Source/Directory.Packages.props`，並啟用 `ManagePackageVersionsCentrally`。
- `About/About.xml` 的 Mod 版本更新為 `2026.08.05.1`。

基線建置與轉換後建置都使用暫存輸出路徑 `C:\tmp`，避免驗證流程覆寫 Git 追蹤中的 `Assemblies` DLL。

## 2. 版本衝突處理

| 套件 | 基線宣告 | 使用專案 | 處理方式 | 實際影響 |
|---|---|---|---|---|
| `Lib.Harmony` | 主專案 `2.4.2`；測試專案 `2.*` | 主專案、測試專案 | 依決策統一至中央版本 `2.4.2` | 兩個專案基線與轉換後都解析為 `2.4.2`，沒有執行期版本變更 |

基線與轉換後套件來源均未回報已知漏洞。

## 3. 套件比較：基線與轉換後

### 變更

| 專案 | 套件 | 基線解析版本 | 轉換後解析版本 | 狀態 |
|---|---|---:|---:|---|
| — | — | — | — | 沒有解析版本、套件新增/移除或 `VersionOverride` 變更；轉換完全版本中性 |

測試專案的 `Lib.Harmony` requested version 由 `2.*` 改為中央 `2.4.2`，但 resolved version 維持不變，因此不列為解析結果變更。

### 未變更

| 專案 | 套件 | 解析版本 |
|---|---|---:|
| `FasterGameLoading.csproj` | `Krafs.Publicizer` | `2.3.0` |
| `FasterGameLoading.csproj` | `Krafs.Rimworld.Ref` | `1.6.4850` |
| `FasterGameLoading.csproj` | `Lib.Harmony` | `2.4.2` |
| `FasterGameLoading.Tests.csproj` | `Lib.Harmony` | `2.4.2` |
| `FasterGameLoading.Tests.csproj` | `Microsoft.NET.Test.Sdk` | `17.5.0` |
| `FasterGameLoading.Tests.csproj` | `NUnit` | `3.13.3` |
| `FasterGameLoading.Tests.csproj` | `NUnit3TestAdapter` | `4.4.2` |

## 4. 風險評估

**低風險**：轉換前後所有套件解析版本一致，乾淨建置成功，沒有警告或錯誤，且 62 個測試全部通過。

- 沒有使用 `VersionOverride`，所有套件版本都由中央檔案管理。
- 沒有移除仍被其他建置邏輯使用的 MSBuild 版本屬性。
- 沒有修改 C# 程式碼、公開 API 或遊戲啟動流程。
- CPM 只改變套件版本的建置管理方式；由於解析套件版本與產出程式碼未變，預期遊戲啟動速度為中性，沒有可歸因於此轉換的啟動時間變化。

## 5. 後續項目

1. [x] 已執行 `dotnet test Source/FasterGameLoading.sln`，62 個測試全部通過。
2. [ ] 未來升級套件時，先在 `Source/Directory.Packages.props` 調整中央版本，再重新檢查基線與測試結果。
3. [ ] 若日後需要讓專案使用不同套件版本，應先評估 `VersionOverride` 對集中管理的一致性影響。
4. [ ] 目前套件來源沒有回報漏洞；若套件版本日後更新，應重新執行漏洞稽核。

## 6. 產出檔案與用途

- `baseline.binlog`：CPM 轉換前的 MSBuild 二進位建置記錄，可用於手動診斷。
- `after-cpm.binlog`：CPM 轉換後的 MSBuild 二進位建置記錄，可與基線比較。
- `baseline-packages.json`：轉換前各專案的 resolved NuGet 套件快照。
- `after-cpm-packages.json`：轉換後各專案的 resolved NuGet 套件快照。
- `convert-to-cpm.md`：本轉換報告，可作為 PR 描述或團隊審查紀錄。
- `baseline-retry.binlog`：基線建置診斷重試記錄；不是版本比較必要檔案，但保留供疑難排解。

建議後續維持 `dotnet test` 驗證，以涵蓋建置成功之外的執行期行為。
