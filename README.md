# TextSourceInspector

Unity 文字來源選取、對話上下文與 Sakura 翻譯請求記錄插件，目前版本 **0.2.4**。

這是獨立的 BepInEx 插件，依賴 [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator)。透過 Harmony 在執行時連接翻譯流程，不修改 XUnity 或 SakuraTranslate 的原始 DLL。

## 功能

- 選取對話及姓名元件，保存各遊戲自己的來源路徑。
- 僅選定對話附帶最多四段、合計 120 字的原文前文。
- 姓名與文字在畫面更新後配對，避免下一句姓名覆蓋舊內容。
- 一般前文標記為「對話／發話者」。括號段落僅標記為「括號文字／發話者」，不推定為旁白或內心話。
- 對話使用精簡 system；user 翻譯指令保持原樣。其他 UI 維持 Sakura 預設提示。
- TXT／JSONL 記錄實際請求、背景、姓名、回傳譯文、耗時及錯誤。
- Unity 主要場景切換時清除記憶體中的對話歷史。

## 原始碼

| 檔案 | 用途 |
|---|---|
| `src/TextSourceInspector.cs` | 文字來源選取視窗與元件檢查 |
| `src/DialogueContextBridge.cs` | 姓名配對、上下文、提示詞與 Sakura 掛接 |
| `src/SakuraTraceLog.cs` | 請求及結果檔案記錄 |
| `src/TextSourceInspectorIL2CPP.cs` | IL2CPP 入口與 Unity 執行緒宿主 |

## 建置

依賴 DLL 不納入版本管理，請使用自己目標環境的組件。

### Mono

需 Windows .NET Framework C# 編譯器、BepInEx 5 及目標遊戲的 Unity 組件。

```powershell
./build.ps1 -Target Mono -GameRoot 'D:\YourGame'
```

若遊戲目錄內有多個 `*_Data/Managed`，請另指定 `-ManagedDirectory`。

### IL2CPP

需 BepInEx 6、Il2CppInterop、目標遊戲產生的 `BepInEx/interop`、.NET 6 runtime DLL，以及能編譯 .NET 6 參考的 Roslyn `csc.exe`。建置時以 `IL2CPP` 編譯符號選用對應程式分支。

```powershell
./build.ps1 -Target IL2CPP -GameRoot 'D:\YourGame' -CompilerPath 'C:\BuildTools\Roslyn\csc.exe'
```

預設使用 `GameRoot/dotnet` 的 runtime DLL，可用 `-RuntimeDirectory` 改指定路徑。

輸出位於 `bin/Mono/TextSourceInspector.dll` 或 `bin/IL2CPP/TextSourceInspectorIL2CPP.dll`。

## 安裝及首次使用

1. 關閉遊戲，把對應 DLL 放入 `BepInEx/plugins`。
2. 啟動遊戲，在左上角「文字來源檢查」視窗選取對話及姓名元件。
3. 選取自動保存至 `BepInEx/config/local.xunity.textsourceinspector.cfg`。
4. 詳細記錄位於 `BepInEx/TranslationLogs`。命中既有翻譯快取的文字不會產生 Sakura 請求紀錄。

`config` 內附空白設定範本，不包含任何遊戲的元件路徑或劇情範例。

## 限制與驗證狀態

- 目前依 Unity 6 組件建置，不保證所有 Unity/TMP 版本均相容。
- Mono 已有實機請求 LOG 驗證；IL2CPP 已完成編譯與依賴檢查，尚未完成實機驗證。
- 未新增同場景劇情切換、讀檔、新遊戲的專用清除或舊請求失效處理。
- 不改翻譯快取鍵，不會自動重翻舊快取。
- 不保證模型不會翻錯人稱；括號文字標記不代表已理解文字的語意類型。
- 字型插件為另外的專案，本儲存庫不包含字型、遊戲檔案或第三方 DLL。

## 授權

本儲存庫尚未指定自己的開源授權；公開散布前需先決定授權。相關依賴適用各自的授權，本儲存庫不重新授權依賴組件。
