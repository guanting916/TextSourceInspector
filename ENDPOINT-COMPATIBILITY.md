# SakuraTranslate 端點比對（2026-10-26）

比對的是 Mono 通用套件使用的 SakuraTranslate.dll 與 fkiliver/SakuraTranslator 官方 v0.3.10 的 Mono 附件。未比對其他版本的 Release，也沒有在此散布修改版端點 DLL。

| 項目 | 套件內檔案 | 官方 v0.3.10 |
|---|---|---|
| 組件版本 | 0.3.10.0 | 0.3.10.0 |
| 大小 | 1,136,128 bytes | 19,968 bytes |
| SHA-256 | `7ad668c24da01344967ba660c2ce2bd76512674e316cfd30bede72b02654d17e` | `68146c5d08618fa2854af00f344ec6ba660b2b0487525b3528aedb14699d079b` |
| MakeSakuraPromptV1_0 | 臺灣繁體中文，附換行規則 | system 要求簡體中文，user 要求中文 |
| TaiwanChineseConverter | 新增類別、字典與 Convert 呼叫 | 無 |
| XUnity.AutoTranslator.Plugin.Core 引用 | 5.6.2.0 | 5.4.4.0 |
| XUnity.Common 引用 | 1.0.5.0 | 1.0.3.0 |

官方來源：[v0.3.10 Release](https://github.com/fkiliver/SakuraTranslator/releases/tag/v0.3.10) 的 `SakuraTranslate.dll`。下載檔的 SHA-256 已與 GitHub Release metadata 中的 digest 核對一致。

檢查方法：讀取 .NET 組件版本、引用、類別與方法 IL。套件內共 45 個方法，官方共 34 個方法；原有方法的差異出現在 MakeSakuraPromptV1_0、Translate 狀態機的 MoveNext 及 Dispose，另新增 11 個轉換相關方法。MoveNext 中已確認呼叫 TaiwanChineseConverter.Convert。

這份紀錄證明兩份二進位內容不同，不能單憑它確定由誰、何時修改，亦不表示已完成所有執行路徑的行為驗證。

本專案仍將 SakuraTranslator 列為依賴專案，並未宣稱 TextSourceInspector 是從其 Fork 而來。若要長期維護修改版端點，應另行保存端點原始碼、修改紀錄及適用的第三方授權資訊。