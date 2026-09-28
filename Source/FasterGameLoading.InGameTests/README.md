# 遊戲內整合測試（RimTest Redux）

驗證 FGL 在真實遊戲載入完成後的狀態，補足 `FasterGameLoading.Tests`（headless NUnit，Unity 大量被 stub）測不到的部分：
Harmony patch 實際套用結果、延遲圖形／圖示／音效是否全部完成、靜態圖集烘焙結果、型別快取與原版解析的一致性、提早載入的時序。

## 需求

- 已訂閱 [RimTest Redux](https://steamcommunity.com/sharedfiles/filedetails/?id=3762405308)、ilyvion's Laboratory、Harmony
- rimworld-mod-mcp（自動啟動隔離的遊戲 session 並讀回結果）

## 執行

1. 建置（參考 `Assemblies/FasterGameLoading.dll`，不會重建 FGL；要測修改後的 FGL 請先自行建置它）：
   `dotnet build Source/FasterGameLoading.InGameTests/FasterGameLoading.InGameTests.csproj -c Release`
2. `run_test_cycle`：`path` 指向 `Source/FasterGameLoading.InGameTests/Mod`，
   `companion_mods` 為 `brrainz.harmony`、`ilyvion.laboratory`、`ilyvion.rimtestredux`、`Taranchuk.FasterGameLoading`。
   - 預設設定：不帶 `seed_config`。
   - 全開（延遲圖形 + 自適應圖集）：`seed_config` 帶 `Profiles/AllOn/` 下的設定檔。
3. 結果寫在遊戲 log 的 `[RimTest Redux] TESTING START … TESTING END` 之間；失敗項目以 Error 輸出，`list_test_diagnostics` 可直接讀到。

## 設計重點

- RimTest Redux 內建的「啟動時執行」會在 FGL 延遲管線跑完前觸發，因此由 `TestRunDriver` 改為等 `DelayedActions.PerformActions` 結束後才執行（不需要地圖，quicktest 可關）。
- 本 mod 刻意排在 FGL **之前**載入：`ContentLoadProbe` 必須在 FGL 建構子啟動提早載入前就位；
  也藉此把被 `seed_config` 改名成本 mod 資料夾的 FGL 設定檔複製回 FGL 的檔名。
- DLL 輸出到 `Mod/Assemblies/`，不進 FGL 本體；`Source/` 已被 `_PublisherPlus.xml` 排除，不會上傳工作坊。
