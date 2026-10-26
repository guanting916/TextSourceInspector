# TextSourceInspector

TextSourceInspector 的目標是讓 Unity 遊戲的 Sakura 翻譯能參考**對話前文與說話者**，並留下可檢查的翻譯請求紀錄。

遊戲中的台詞常需要搭配前文和角色姓名才能理解。本專案讓使用者選定畫面上的對話與姓名元件，將相關資訊加入翻譯請求，提供模型判斷語境的參考。

程式透過 GPT6.1 SOL 協助修改、建置與驗證。

## 主要功能

- **選取文字來源**：透過遊戲內的「文字來源檢查」面板，指定對話與姓名元件，並保存選取結果。
- **附加對話上下文**：將選定對話的先前原文與說話者加入翻譯請求；按鈕、選單等 UI 文字不加入對話歷史。
- **記錄請求與回覆**：保存前文、姓名、當前原文、實際請求及回覆，方便確認上下文是否正確帶入與排查問題。
- **支援 Mono 與 IL2CPP**：提供對應 BepInEx 環境的插件版本。

## 依賴專案

本專案是獨立的附加插件，透過 Harmony 連接既有翻譯流程，搭配以下專案使用：

- [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator)：遊戲文字擷取與翻譯流程。
- [SakuraTranslator 臺灣繁體版](https://github.com/guanting916/SakuraTranslator)：Sakura 翻譯端點。

相關環境的安裝與設定請參閱各專案說明。

## 目前限制

- 對話與姓名來源需要手動選取；場景或對話框結構改變時，可能需要重新選取。
- Unity 主要場景切換時會清除對話歷史；同一場景內的讀檔、新遊戲或劇情切換尚無專用清除機制。
- 不會修改 XUnity 的翻譯快取鍵或自動重翻舊譯。
- 上下文只提供翻譯參考，模型仍可能翻錯人稱或語意。

## 開發與授權

程式透過 **GPT6.1 SOL** 協助建置與編譯。原始碼與建置方式請參閱 [BUILDING.md](BUILDING.md)。

本專案採用 [MIT License](LICENSE)，Copyright (c) 2026 guanting916。相關依賴適用各自授權。
