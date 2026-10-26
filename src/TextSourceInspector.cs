using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
#if IL2CPP
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
#endif

#if !IL2CPP
[BepInPlugin("local.xunity.textsourceinspector", "Text Source Inspector", "0.2.4")]
[BepInDependency("gravydevsupreme.xunity.autotranslator")]
public sealed class TextSourceInspector : BaseUnityPlugin
#else
public sealed class TextSourceInspector
#endif
{
#if IL2CPP
    private readonly ConfigFile Config;
    private readonly ManualLogSource Logger;
    private UnityEngine.Events.UnityAction<Scene, Scene> sceneCallback;
    public TextSourceInspector(ConfigFile config, ManualLogSource logger) { Config = config; Logger = logger; }
    public void Initialize() { Awake(); Start(); }
    public void Tick() { Update(); }
    public void Draw() { OnGUI(); }
    public void Shutdown() { OnDestroy(); }
#endif
    private sealed class Row
    {
        public Component Component;
        public string Path, Original, Displayed;
    }
    private sealed class Turn { public string Speaker, Text; }
    private readonly Dictionary<Type, PropertyInfo> frameworks = new Dictionary<Type, PropertyInfo>();
    private readonly List<Row> rows = new List<Row>();
    private readonly List<Turn> history = new List<Turn>();
    private ConfigEntry<string> dialoguePath, speakerPath;
    private ConfigEntry<bool> showPanel;
    private MethodInfo getInfo;
    private PropertyInfo originalProperty;
    private Font uiFont;
    private GUIStyle normal, small, wrap;
    private Vector2 scroll;
    private float nextScan, changedAt;
    private string pendingText = "", lastCaptured = "", status = "選擇對話框與姓名欄後，會預覽最近四段原文。";
    private bool includeOriginal;
    private Rect window = new Rect(20, 50, 760, 640);

    private void Awake()
    {
        dialoguePath = Config.Bind<string>("Selection", "DialoguePath", "", "Exact scene/component path of the dialogue text.");
        speakerPath = Config.Bind<string>("Selection", "SpeakerPath", "", "Exact scene/component path of the speaker label; optional.");
        showPanel = Config.Bind<bool>("UI", "ShowPanel", true, "Show the inspector panel. The small toggle remains available.");
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (string name in new string[] { "TMPro.TMP_Text", "UnityEngine.UI.Text" })
            {
                Type t = a.GetType(name, false);
                if (t != null && !frameworks.ContainsKey(t))
                {
                    PropertyInfo p = t.GetProperty("text");
                    if (p != null && p.PropertyType == typeof(string)) frameworks[t] = p;
                }
            }
            Type ext = a.GetType("XUnity.AutoTranslator.Plugin.Core.Extensions.ComponentExtensions", false);
            if (ext != null) getInfo = ext.GetMethod("GetTextTranslationInfo", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { typeof(object) }, null);
            Type info = a.GetType("XUnity.AutoTranslator.Plugin.Core.TextTranslationInfo", false);
            if (info != null) originalProperty = info.GetProperty("OriginalText");
        }
        includeOriginal = getInfo != null && originalProperty != null;
        if (!includeOriginal) Logger.LogWarning("XUnity original-text accessor unavailable; history preview is disabled to avoid recording translations as source.");
#if IL2CPP
        sceneCallback = new Action<Scene, Scene>(SceneChanged);
        SceneManager.activeSceneChanged += sceneCallback;
#else
        SceneManager.activeSceneChanged += SceneChanged;
#endif
        DialogueContextBridge.Initialize(Config, Logger, delegate { return dialoguePath.Value; },
            delegate { return speakerPath.Value; }, FullPath);
        Logger.LogInfo("Text source inspector ready: " + frameworks.Count + " frameworks, original source available=" + includeOriginal);
    }

    private void Start()
    {
        // Only the diagnostic panel uses this font; game text is untouched.
        string fontName = "LXGW.ttf";
        string cfg = Path.Combine(Paths.ConfigPath, "com.github.you9you.ttfloader.cfg");
        if (File.Exists(cfg)) foreach (string line in File.ReadAllLines(cfg))
        {
            string[] p = line.Split(new char[] { '=' }, 2);
            if (p.Length == 2 && p[0].Trim() == "FontFile") fontName = p[1].Trim();
        }
        try
        {
            string path = Path.Combine(Paths.GameRootPath, fontName);
            uiFont = File.Exists(path) ? new Font(path) : Font.CreateDynamicFontFromOSFont("Microsoft JhengHei", 16);
            if (uiFont != null) UnityEngine.Object.DontDestroyOnLoad(uiFont);
        }
        catch (Exception ex) { Logger.LogWarning("Inspector font: " + ex.Message); }
    }

    private void SceneChanged(Scene oldScene, Scene newScene) { ClearHistory(); }
    private void OnDestroy()
    {
#if IL2CPP
        if (sceneCallback != null) SceneManager.activeSceneChanged -= sceneCallback;
#else
        SceneManager.activeSceneChanged -= SceneChanged;
#endif
    }
    private void ClearHistory()
    {
        DialogueContextBridge.Clear();
        history.Clear(); pendingText = ""; lastCaptured = "";
        changedAt = Time.realtimeSinceStartup;
    }

    private static string FullPath(Component c)
    {
        List<string> parts = new List<string>();
        Transform t = c.transform;
        while (t != null)
        {
            parts.Add(t.name + "[" + t.GetSiblingIndex() + "]"); t = t.parent;
        }
        parts.Reverse();
        return c.gameObject.scene.name + ":/" + string.Join("/", parts.ToArray());
    }

    private void Update()
    {
        DialogueContextBridge.FlushPreviousFrame();
        if (Time.realtimeSinceStartup < nextScan) return;
        nextScan = Time.realtimeSinceStartup + 0.25f;
        try
        {
            rows.Clear();
            foreach (KeyValuePair<Type, PropertyInfo> f in frameworks)
                foreach (UnityEngine.Object item in FindTextObjects(f.Key))
                {
                    object typed = TypedText(item, f.Key);
                    Behaviour c = typed as Behaviour;
                    if (c == null || !c.isActiveAndEnabled) continue;
                    string displayed = (string)f.Value.GetValue(typed, null);
                    string original = null;
                    if (includeOriginal)
                    {
                        object info = getInfo.Invoke(null, new object[] { c });
                        if (info != null) original = originalProperty.GetValue(info, null) as string;
                    }
                    rows.Add(new Row { Component = c, Path = FullPath(c), Displayed = displayed ?? "", Original = original });
                }
            rows.Sort(delegate(Row a, Row b) { return string.CompareOrdinal(a.Path, b.Path); });
            CaptureHistory();
        }
        catch (Exception ex) { status = "掃描失敗：" + ex.Message; Logger.LogWarning(status); }
    }

    private void CaptureHistory()
    {
        if (!includeOriginal || string.IsNullOrEmpty(dialoguePath.Value)) return;
        Row dialogue = rows.Find(delegate(Row r) { return r.Path == dialoguePath.Value; });
        if (dialogue == null || string.IsNullOrWhiteSpace(dialogue.Displayed))
        {
            pendingText = ""; lastCaptured = ""; return;
        }
        // Never substitute the translated display text when original metadata is absent.
        string text = dialogue.Original;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text != pendingText) { pendingText = text; changedAt = Time.realtimeSinceStartup; return; }
        if (text == lastCaptured || Time.realtimeSinceStartup - changedAt < 0.5f) return;
        Row speaker = rows.Find(delegate(Row r) { return r.Path == speakerPath.Value; });
        history.Add(new Turn { Text = text, Speaker = speaker == null ? "" : speaker.Original ?? speaker.Displayed });
        if (history.Count > 4) history.RemoveAt(0);
        lastCaptured = text;
    }

    private static string Short(string s, int length)
    {
        s = (s ?? "").Replace("\r", "").Replace("\n", " / ");
        return s.Length <= length ? s : s.Substring(0, length) + "…";
    }

    private void OnGUI()
    {
        if (normal == null)
        {
            normal = CopyStyle(GUI.skin.label); normal.fontSize = 16; BlackText(normal);
            small = CopyStyle(normal); small.fontSize = 12; small.wordWrap = true;
            wrap = CopyStyle(normal); wrap.wordWrap = true;
            if (uiFont != null) { normal.font = uiFont; small.font = uiFont; wrap.font = uiFont; }
        }
        GUIStyle button = CopyStyle(GUI.skin.button); button.font = uiFont; button.fontSize = 15; BlackText(button);
        if (GUI.Button(new Rect(10, 10, 135, 32), "文字來源檢查", button))
        { showPanel.Value = !showPanel.Value; Config.Save(); }
        if (!showPanel.Value) return;
        window.width = Mathf.Min(800, Screen.width - 30);
        window.height = Mathf.Min(670, Screen.height - 65);
        GUIStyle windowStyle = CopyStyle(GUI.skin.window); BlackText(windowStyle);
#if IL2CPP
        window = GUILayout.Window(0x58435458, window, new Action<int>(DrawPanel), "Text Source Inspector 0.2.4", windowStyle);
#else
        window = GUILayout.Window(GetInstanceID(), window, DrawPanel, "Text Source Inspector 0.2.4", windowStyle);
#endif
    }

    private static void BlackText(GUIStyle style)
    {
        style.normal.textColor = Color.black;
        style.hover.textColor = Color.black;
        style.active.textColor = Color.black;
        style.focused.textColor = Color.black;
        style.onNormal.textColor = Color.black;
        style.onHover.textColor = Color.black;
        style.onActive.textColor = Color.black;
        style.onFocused.textColor = Color.black;
    }

    private static GUIStyle CopyStyle(GUIStyle source)
    {
#if IL2CPP
        // Some games strip GUIStyle's copy constructor; use available properties.
        GUIStyle copy = new GUIStyle();
        copy.font = source.font; copy.fontSize = source.fontSize; copy.fontStyle = source.fontStyle;
        copy.alignment = source.alignment; copy.wordWrap = source.wordWrap; copy.clipping = source.clipping;
        copy.stretchWidth = source.stretchWidth; copy.stretchHeight = source.stretchHeight;
        copy.fixedWidth = source.fixedWidth; copy.fixedHeight = source.fixedHeight;
        copy.contentOffset = source.contentOffset; copy.imagePosition = source.imagePosition;
        copy.normal.background = source.normal.background; copy.hover.background = source.hover.background;
        copy.active.background = source.active.background; copy.focused.background = source.focused.background;
        copy.onNormal.background = source.onNormal.background; copy.onHover.background = source.onHover.background;
        copy.onActive.background = source.onActive.background; copy.onFocused.background = source.onFocused.background;
        CopyOffset(copy.border, source.border); CopyOffset(copy.padding, source.padding);
        CopyOffset(copy.margin, source.margin); CopyOffset(copy.overflow, source.overflow);
        return copy;
#else
        return new GUIStyle(source);
#endif
    }
#if IL2CPP
    private static void CopyOffset(RectOffset target, RectOffset source)
    { target.left = source.left; target.right = source.right; target.top = source.top; target.bottom = source.bottom; }
#endif

    private static IEnumerable<UnityEngine.Object> FindTextObjects(Type type)
    {
#if IL2CPP
        return UnityEngine.Object.FindObjectsOfType(Il2CppType.From(type));
#else
        return UnityEngine.Object.FindObjectsOfType(type);
#endif
    }
    private static readonly Dictionary<Type, MethodInfo> casts = new Dictionary<Type, MethodInfo>();
    private static object TypedText(UnityEngine.Object item, Type type)
    {
#if IL2CPP
        MethodInfo cast;
        if (!casts.TryGetValue(type, out cast))
        {
            cast = typeof(Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(type);
            casts[type] = cast;
        }
        return cast.Invoke(item, null);
#else
        return item;
#endif
    }

    private void DrawPanel(int id)
    {
        GUIStyle button = CopyStyle(GUI.skin.button); button.font = uiFont; button.fontSize = 14; BlackText(button);
        GUILayout.Label(DialogueContextBridge.Ready ? "已連接 Sakura：只有選定對話帶入前四段原文，UI 不帶前文。" : "Sakura 上下文連接未啟用或初始化失敗，請查看記錄。", wrap);
        GUILayout.Label(DialogueContextBridge.LastRequestSummary, small);
        GUILayout.Label("對話：" + (dialoguePath.Value == "" ? "尚未指定" : dialoguePath.Value), small);
        GUILayout.Label("姓名：" + (speakerPath.Value == "" ? "未指定（可略過）" : speakerPath.Value), small);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("清除歷史", button)) ClearHistory();
        if (GUILayout.Button("清除選取", button))
        { dialoguePath.Value = ""; speakerPath.Value = ""; Config.Save(); ClearHistory(); }
        if (GUILayout.Button("匯出文字清單", button)) Export();
        if (GUILayout.Button("收起視窗", button)) { showPanel.Value = false; Config.Save(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("最近四段原文（診斷預覽，含目前已穩定顯示的段落）：", normal);
        if (history.Count == 0) GUILayout.Label(includeOriginal ? "尚未收集；請選擇對話框並推進劇情。" : "無法取得原文，已停用歷史收集。", small);
        foreach (Turn turn in history) GUILayout.Label(Short(turn.Speaker, 15) + "：" + Short(turn.Text, 85), small);
        GUILayout.Label("目前啟用的文字元件：" + rows.Count, normal);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(100, window.height - 355)));
        foreach (Row row in rows)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("對話", button, GUILayout.Width(55)))
            { dialoguePath.Value = row.Path; Config.Save(); ClearHistory(); }
            if (GUILayout.Button("姓名", button, GUILayout.Width(55)))
            { speakerPath.Value = row.Path; Config.Save(); ClearHistory(); }
            GUILayout.BeginVertical();
            GUILayout.Label("顯示：" + Short(row.Displayed, 95), wrap);
            GUILayout.Label("原文：" + (row.Original == null ? "尚無 XUnity 原文記錄" : Short(row.Original, 95)), small);
            GUILayout.Label(row.Path, small);
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            GUILayout.Space(5);
        }
        GUILayout.EndScrollView();
        GUILayout.Label(status, small);
        GUI.DragWindow(new Rect(0, 0, window.width, 20));
    }

    private static string Cell(string value)
    { return (value ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", "\\n"); }

    private void Export()
    {
        try
        {
            string directory = Path.Combine(Paths.BepInExRootPath, "diagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "TextSources-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tsv");
            StringBuilder sb = new StringBuilder("Path\tComponentType\tOriginal\tDisplayed\n");
            foreach (Row row in rows) sb.AppendLine(Cell(row.Path) + "\t" + Cell(row.Component.GetType().FullName) + "\t" + Cell(row.Original) + "\t" + Cell(row.Displayed));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            status = "已匯出：" + path;
        }
        catch (Exception ex) { status = "匯出失敗：" + ex.Message; }
    }
}
