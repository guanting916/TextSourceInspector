# 開發與建置

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
