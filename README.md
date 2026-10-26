# TextSourceInspector

本專案替 XUnity.AutoTranslator 的 Sakura 翻譯加入**選定對話的前文、說話者與請求紀錄**。
程式透過 **GPT6.1 SOL** 協助建置與編譯，是附加插件，不是完整翻譯器。


## 依賴專案

| 專案 | 本插件使用的功能 |
|---|---|
| [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) | 遊戲文字擷取與翻譯流程 |
| [fkiliver/SakuraTranslator](https://github.com/fkiliver/SakuraTranslator) | Sakura 翻譯端點 |

TextSourceInspector 是獨立附加插件，透過 Harmony 連接上述翻譯流程，擴充選定對話的上下文與請求紀錄。本插件未附帶上述專案的 DLL，請依各專案說明另行安裝。

## 1. 安裝前先準備

1. 適用遊戲的 BepInEx 環境。
2. [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) 與 [fkiliver/SakuraTranslator](https://github.com/fkiliver/SakuraTranslator)。
3. 已啟動且可連線的 Sakura 翻譯 API 服務。

**請先確認遊戲使用 Sakura 已能正常翻譯，再安裝本插件。**

若原本還不能翻譯，先完成基礎環境：將 SakuraTranslate 端點放在 XUnity 的 `Translators` 資料夾，
啟動一次遊戲產生設定，再關閉遊戲設定 `BepInEx/config/AutoTranslatorConfig.ini`。

設定內容的重點如下，請修改既有檔案的對應項目，不要只用這幾行覆蓋整份設定：

```ini
[Service]
Endpoint=SakuraTranslate

[General]
Language=zh-TW
FromLanguage=ja

[Sakura]
Endpoint=http://127.0.0.1:8080/v1/chat/completions
ModelName=Sakura
ModelVersion=1.0
```

上面的網址只是本機服務使用 8080 埠的例子，請填你實際使用的 API 位址。模型與其他 Sakura 參數依所用端點及服務設定；
本插件不會替你下載模型或啟動 API。確認基礎翻譯正常後再繼續。

### 端點版本與繁簡體

目前的上下文提示測試以 `ModelName=Sakura`、`ModelVersion=1.0` 的端點格式為基準。其他模型模板或版本需另外確認，不能只依 DLL 檔名認定相容。

SakuraTranslator 官方目前的提示詞要求簡體中文；先前在特定環境使用的端點可能已修改提示詞。`Language=zh-TW` 是 XUnity 的語系設定，**不代表 SakuraTranslate 會自動改寫自己的提示詞**。

本插件只替換選定對話的 system，保留原端點的 user 訊息與其他 UI 提示。若原端點的 user 或 UI 提示仍要求簡體，可能出現繁簡體不一致；請以實際請求 LOG 為準。此處列出的安裝設定不代表已解決端點提示衝突。

## 2. 選擇並下載 DLL

到 [v0.2.4 下載頁](https://github.com/guanting916/TextSourceInspector/releases/tag/v0.2.4)，在 **Assets** 下載符合遊戲環境的一份 DLL。一般使用不需下載 `Source code` 或自行編譯。

| 遊戲環境 | 檔案 |
|---|---|
| BepInEx 5 Mono | `TextSourceInspector.dll` |
| BepInEx 6 IL2CPP / Il2CppInterop 1.5.3 | `TextSourceInspectorIL2CPP.dll` |

遊戲根目錄有 `GameAssembly.dll` 通常是 IL2CPP；有 `遊戲名_Data/Managed/Assembly-CSharp.dll` 通常是 Mono。
不確定時先確認遊戲適用的 BepInEx 類型，不要兩版都裝。

## 3. 放置檔案

「遊戲根目錄」是遊戲 `.exe` 所在的資料夾。關閉遊戲，將對應 DLL 放進 `BepInEx/plugins`。

```text
遊戲資料夾/
├── Game.exe
└── BepInEx/
    ├── plugins/
    │   ├── 對應的文字來源插件.dll
    │   └── XUnity.AutoTranslator/
    │       └── Translators/
    │           └── SakuraTranslate.dll
    ├── config/
    │   ├── AutoTranslatorConfig.ini
    │   └── local.xunity.textsourceinspector.cfg
    └── TranslationLogs/
```

`Game.exe` 與「對應的文字來源插件.dll」是示意名稱。不同 XUnity 套件的資料夾結構可能略有差異，請以原套件的 `Translators` 位置為準，不要為了符合圖示隨意移動既有檔案。

設定檔會在插件首次載入時自動建立；也可將附件的空白 `.cfg` 範本放入 `BepInEx/config`。**若已有設定，請保留，不要用空白範本覆蓋。**

## 4. 首次選取對話與姓名

1. 啟動遊戲，進入實際顯示角色台詞的畫面。
2. 點左上角「文字來源檢查」，展開視窗。
3. 從文字元件清單比對畫面中的台詞，找到對話元件後按「對話」。
4. 找到顯示角色名字的元件後按「姓名」。如果遊戲沒有獨立姓名欄，可略過。
5. 收起視窗，推進到還沒翻譯過的新台詞。

選取會保存到 `BepInEx/config/local.xunity.textsourceinspector.cfg`，同一遊戲下次通常不用重選。不同遊戲不能直接共用來源路徑；對話框或場景結構改變時可能要重新選取。

選取前不會自動知道哪個文字框是對話。選取後只有該對話來源附加上下文；按鈕、選單等 UI 不會加入對話歷史。

## 5. 設定項目

```ini
[Selection]
DialoguePath =
SpeakerPath =

[Context]
Enabled = true
MaxHistoryLines = 4
MaxHistoryCharacters = 120
LogRequests = true

[Diagnostics]
DetailedSakuraLog = true

[UI]
ShowPanel = true
```

- `DialoguePath`／`SpeakerPath`：由選取視窗自動填入，通常不用手寫。空白表示尚未選取。
- `Enabled`：是否附加對話上下文。
- `MaxHistoryLines`：最多帶入幾段先前原文，預設 `4`，不包含當前句。
- `MaxHistoryCharacters`：前文合計最多 `120` 字，姓名標籤另附；前文太長時可能少於四段。
- `LogRequests`：在 BepInEx LOG 記錄段數與耗時等摘要。
- `DetailedSakuraLog`：儲存完整請求與回覆，預設 `true`。
- `ShowPanel`：是否展開來源視窗；收起後仍可透過小按鈕打開。

手動修改設定後重新啟動遊戲。通常先保留預設值，只完成來源選取即可。

## 6. 如何確認上下文及查看 LOG

詳細紀錄位於遊戲目錄下：

```text
BepInEx/TranslationLogs/Sakura-日期-時間-工作階段.txt
BepInEx/TranslationLogs/Sakura-日期-時間-工作階段.jsonl
```

TXT 適合用記事本閱讀；JSONL 用來保留完整資料。請查看最新紀錄中的「附帶上下文」、姓名、前文、當前原文與實際請求 JSON，確認元件選取正確。每次啟動建立新的工作階段紀錄。

只翻譯 user 中的當前文本；前文及姓名動態放入 system 作為參考，不會內建固定劇情。括號段落僅標為「括號文字／發話者」，不判定為旁白或內心話。

記錄中的譯文是 XUnity 後處理之前的結果，可能與畫面最終文字略有不同。

## 常見問題

| 狀況 | 檢查方式 |
|---|---|
| 沒有左上角按鈕或設定檔 | 查看 `BepInEx/LogOutput.log`，確認插件、BepInEx 類型與依賴已載入。 |
| 遊戲完全沒有翻譯 | 先確認 XUnity、SakuraTranslate、API 位址及服務可用；本插件不負責建立基礎翻譯服務。 |
| 請求沒有前文 | 確認 `Enabled = true` 且選對了對話元件；剛啟動或清除歷史後，前文也可能不足四段。 |
| 沒有新的請求紀錄 | 已翻過的文字可能直接命中 XUnity 快取，不會再次請求 Sakura；請使用未翻譯的新台詞檢查。 |
| 更新提示後仍看到舊譯 | 本插件不會清除既有快取，舊譯可能繼續被使用。 |
| 姓名或內容不對 | 重新比對畫面與選定元件；來源欄應是角色姓名，不是其他 UI。 |
| LOG 顯示掛接失敗 | XUnity、SakuraTranslate 或 Unity 的版本介面可能不同，請依 LOG 處理，不要只更改提示詞。 |

## 現有行為與限制

- Unity 主要場景切換時，清除記憶體中的歷史。
- 同一 Unity 場景內的劇情切換、讀檔、新遊戲沒有專用清除機制。
- 不改快取鍵，不會自動重翻舊譯；模型仍可能翻錯人稱。
- 來源以目前選定的元件為準，尚非自動支援所有對話框或所有 Unity/TMP 版本。

## 更新、停用及移除

更新時先關閉遊戲，替換對應 DLL 並保留設定。要恢復原本翻譯流程，可先關閉遊戲，移走本插件 DLL；不要因此移除 XUnity 或整個 BepInEx。

## 開發與授權

原始碼及建置方式請參閱 [BUILDING.md](BUILDING.md)。本插件透過 Harmony 連接既有翻譯流程，不改寫 XUnity 或 SakuraTranslate 的原始 DLL。

本儲存庫尚未指定自己的開源授權。相關依賴適用各自授權；本儲存庫不重新授權依賴組件。
