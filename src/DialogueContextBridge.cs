using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

// In-memory context only: existing XUnity translation keys/caches remain unchanged.
public sealed class DialogueContextTurn
{
    public string Text, Speaker;
    public List<DialogueContextTurn> Before;
}

public sealed class DialogueContextHistory
{
    private readonly List<DialogueContextTurn> turns = new List<DialogueContextTurn>();
    private string lastText = "";
    private string lastSpeaker = "";

    public void Clear() { turns.Clear(); lastText = ""; lastSpeaker = ""; }
    public void Observe(string text, string pairedSpeaker)
    {
        if (string.IsNullOrWhiteSpace(text)) { lastText = ""; lastSpeaker = ""; return; }
        pairedSpeaker = pairedSpeaker ?? "";
        if (text == lastText && pairedSpeaker == lastSpeaker) return;
        DialogueContextTurn current = new DialogueContextTurn { Text = text, Speaker = pairedSpeaker, Before = new List<DialogueContextTurn>() };
        // Only committed pairs enter history. No name update ever mutates them.
        foreach (DialogueContextTurn t in turns)
            current.Before.Add(new DialogueContextTurn { Text = t.Text, Speaker = t.Speaker });
        turns.Add(current);
        if (turns.Count > 16) turns.RemoveAt(0);
        lastText = text;
        lastSpeaker = pairedSpeaker;
    }
    public DialogueContextTurn Find(string original)
    {
        for (int i = turns.Count - 1; i >= 0; i--)
            if (Normalize(turns[i].Text) == Normalize(original)) return turns[i];
        return null;
    }
    public static string Normalize(string value)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in value ?? "") if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }
    public static List<DialogueContextTurn> SelectBefore(DialogueContextTurn turn, int maxLines, int maxChars)
    {
        List<DialogueContextTurn> selected = new List<DialogueContextTurn>();
        int used = 0;
        for (int i = turn.Before.Count - 1; i >= 0 && selected.Count < maxLines; i--)
        {
            DialogueContextTurn t = turn.Before[i];
            if (used + t.Text.Length > maxChars) break;
            selected.Insert(0, new DialogueContextTurn { Text = t.Text, Speaker = t.Speaker });
            used += t.Text.Length;
        }
        return selected;
    }
    public static string Background(List<DialogueContextTurn> previous, string currentSpeaker)
    {
        StringBuilder sb = new StringBuilder("\n以下是背景參考，不得翻譯或輸出：\n<背景>\n");
        foreach (DialogueContextTurn t in previous)
        {
            bool parenthesized = (t.Text ?? "").StartsWith("（", StringComparison.Ordinal) || (t.Text ?? "").StartsWith("(", StringComparison.Ordinal);
            sb.Append(parenthesized ? "[括號文字；發話者=" : "[對話；發話者=")
                .Append(string.IsNullOrEmpty(t.Speaker) ? "未提供" : t.Speaker)
                .Append("] ").Append(t.Text).Append('\n');
        }
        sb.Append("</背景>\n當前說話者：").Append(string.IsNullOrEmpty(currentSpeaker) ? "未提供" : currentSpeaker);
        sb.Append("。只翻譯當前文本，不輸出姓名、背景或解釋。");
        return sb.ToString();
    }
}

// Coalesce same-frame name/body setter events, independent of their order.
public sealed class DialogueTurnCollector
{
    public readonly DialogueContextHistory History = new DialogueContextHistory();
    private string speaker = "", pendingText;
    private int pendingFrame;
    private bool pending;
    public void ObserveText(string text, int frame)
    {
        Advance(frame);
        pendingText = text; pendingFrame = frame; pending = true;
    }
    public void ObserveSpeaker(string name, int frame)
    {
        // Commit the preceding frame with its own speaker before changing the name.
        Advance(frame);
        speaker = name ?? "";
    }
    public void Advance(int frame)
    {
        if (!pending || pendingFrame >= frame) return;
        History.Observe(pendingText, speaker);
        pending = false;
    }
    public void Clear() { History.Clear(); speaker = ""; pendingText = null; pending = false; }
}

public static class DialogueContextBridge
{
    public static bool Ready { get; private set; }
    public static string LastRequestSummary = "尚未送出新的對話翻譯請求；已翻過的台詞可能直接讀取快取。";
    private sealed class Snapshot
    {
        public List<DialogueContextTurn> Previous;
        public string Speaker;
        public int Characters;
        public string Original;
    }
    private sealed class Trace
    {
        public Stopwatch Timer = Stopwatch.StartNew();
        public Snapshot Snapshot;
        public object Context;
        public string Id, CurrentSource;
        public int Attempt;
        public bool Completed;
    }
    private sealed class Scope { public Snapshot PreviousSnapshot; public Trace PreviousTrace; }
    private static readonly DialogueTurnCollector collector = new DialogueTurnCollector();
    private static readonly ConditionalWeakTable<object, Snapshot> prepared = new ConditionalWeakTable<object, Snapshot>();
    private static readonly ConditionalWeakTable<object, Trace> traces = new ConditionalWeakTable<object, Trace>();
    [ThreadStatic] private static Snapshot active;
    [ThreadStatic] private static Trace activeTrace;
    private static ManualLogSource log;
    private static ConfigEntry<bool> enabled, logging;
    private static ConfigEntry<bool> detailedLogging;
    private static ConfigEntry<int> maxLines, maxChars;
    private static Func<string> dialoguePath, speakerPath;
    private static Func<Component, string> path;
    private static MethodInfo getInfo;
    private static FieldInfo coroutineContext;
    private static Harmony harmony;
    private static SakuraTraceLog detailLog;
    private static readonly string session = Guid.NewGuid().ToString("N").Substring(0, 8);
    private static long nextId;
    public static string DetailedLogPath { get { return detailLog == null ? "" : detailLog.TextPath; } }

    public static void Initialize(ConfigFile config, ManualLogSource logger, Func<string> dialogue,
        Func<string> speaker, Func<Component, string> getPath)
    {
        log = logger; dialoguePath = dialogue; speakerPath = speaker; path = getPath;
        enabled = config.Bind<bool>("Context", "Enabled", true, "Attach history only to jobs originating from the selected dialogue component.");
        maxLines = config.Bind<int>("Context", "MaxHistoryLines", 4, "Maximum previous dialogue blocks.");
        maxChars = config.Bind<int>("Context", "MaxHistoryCharacters", 120, "Maximum combined source characters in previous blocks; excludes speaker labels.");
        logging = config.Bind<bool>("Context", "LogRequests", true, "Log context counts and elapsed time, without dialogue contents.");
        detailedLogging = config.Bind<bool>("Diagnostics", "DetailedSakuraLog", true, "Save actual Sakura request JSON and returned translations under BepInEx/TranslationLogs, including game text.");
        if (!enabled.Value) return;
        try
        {
            Type extension = FindType("XUnity.AutoTranslator.Plugin.Core.Extensions.ComponentExtensions");
            getInfo = extension.GetMethod("GetTextTranslationInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Type endpoint = FindType("XUnity.AutoTranslator.Plugin.Core.Endpoints.TranslationEndpointManager");
            Type sakura = FindType("SakuraTranslate.SakuraTranslateEndpoint");
            Type coroutine = null;
            foreach (Type t in sakura.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                if (t.Name.StartsWith("<Translate>")) { coroutine = t; break; }
            if (coroutine == null || getInfo == null) throw new MissingMethodException("Context source/coroutine API not found");
            coroutineContext = coroutine.GetField("context", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (coroutineContext == null) throw new MissingFieldException("Sakura coroutine context");
            harmony = new Harmony("local.xunity.textsourceinspector.context");
            foreach (string name in new string[] { "TMPro.TMP_Text", "UnityEngine.UI.Text" })
            {
                Type textType = TryFindType(name);
                if (textType == null) continue;
                PropertyInfo prop = textType.GetProperty("text");
                MethodInfo setter = prop == null ? null : prop.GetSetMethod();
                if (setter != null) harmony.Patch(setter, prefix: Hook("TextPrefix", Priority.First));
            }
            harmony.Patch(endpoint.GetMethod("PrepareUntranslatedText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: Hook("PreparePostfix", Priority.Normal));
            harmony.Patch(coroutine.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                prefix: Hook("MovePrefix", Priority.First), postfix: Hook("MovePostfix", Priority.Last), finalizer: Hook("MoveFinalizer", Priority.Last));
            harmony.Patch(sakura.GetMethod("MakeRequestStr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                prefix: Hook("RequestPrefix", Priority.First), postfix: Hook("RequestPostfix", Priority.Last));
            harmony.Patch(sakura.GetMethod("Translate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: Hook("TranslatePostfix", Priority.Last));
            if (detailedLogging.Value)
            {
                try
                {
                    detailLog = new SakuraTraceLog(System.IO.Path.Combine(Paths.BepInExRootPath, "TranslationLogs"), session,
                        delegate(string warning) { log.LogWarning(warning); });
                    detailLog.Write(new Dictionary<string, object> { { "event", "session_start" }, { "session_id", session }, { "plugin_version", "0.2.4" },
                        { "cache_policy", "Existing caches unchanged; cache hits do not produce Sakura request records." } },
                        "Sakura 翻譯紀錄（工作階段 " + session + "）\r\n只記錄送入 Sakura 的請求與結果；命中快取而未送出的文字不在本紀錄中。\r\n姓名為實際送出的標籤，尚未驗證與畫面配對是否一致。");
                    log.LogInfo("Detailed Sakura log: " + detailLog.TextPath);
                }
                catch (Exception ex) { log.LogWarning("Cannot open detailed Sakura log; translation continues: " + ex.Message); }
            }
            Ready = true;
            log.LogInfo("Sakura context bridge ready: selected dialogue only; previous=" + maxLines.Value + ", chars=" + maxChars.Value + ". Original caches unchanged.");
        }
        catch (Exception ex)
        {
            log.LogError("Context bridge unavailable; original translation retained: " + ex);
            try { if (harmony != null) harmony.UnpatchSelf(); }
            catch (Exception cleanup) { log.LogWarning("Context hook cleanup failed: " + cleanup.Message); }
        }
    }

    public static void Clear() { collector.Clear(); }
    public static void FlushPreviousFrame()
    {
        if (Ready && enabled.Value) collector.Advance(Time.frameCount);
    }
    private static HarmonyMethod Hook(string method, int priority)
    { return new HarmonyMethod(typeof(DialogueContextBridge).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority }; }
    private static Type TryFindType(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { Type t = a.GetType(name, false); if (t != null) return t; }
        return null;
    }
    private static Type FindType(string name)
    { Type t = TryFindType(name); if (t == null) throw new TypeLoadException(name); return t; }
    private static object Get(object item, string name)
    {
        if (item == null) return null;
        PropertyInfo p = item.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return p == null ? null : p.GetValue(item, null);
    }
    private static void TextPrefix(object __instance, string __0)
    {
        if (!enabled.Value || string.IsNullOrEmpty(dialoguePath())) return;
        try
        {
            Component c = __instance as Component;
            if (c == null) return;
            string location = path(c);
            bool isDialogue = location == dialoguePath();
            bool isSpeaker = !string.IsNullOrEmpty(speakerPath()) && location == speakerPath();
            if (!isDialogue && !isSpeaker) return;
            object info = getInfo.Invoke(null, new object[] { c });
            // Ignore XUnity writing translations or toggling display back to originals.
            if (info != null && true.Equals(Get(info, "IsCurrentlySettingText"))) return;
            if (isDialogue) collector.ObserveText(__0, Time.frameCount);
            else collector.ObserveSpeaker(__0, Time.frameCount);
        }
        catch (Exception ex) { log.LogWarning("Context source capture skipped: " + ex.Message); }
    }

    private static void PreparePostfix(object __1, object __result)
    {
        if (!enabled.Value || __result == null || string.IsNullOrEmpty(dialoguePath())) return;
        try
        {
            IEnumerable components = Get(__1, "Components") as IEnumerable;
            if (components == null) return;
            bool selected = false;
            foreach (object entry in components)
            {
                Component c = Get(entry, "Item") as Component;
                // Shared UI/dialogue jobs remain context-free: no UI contamination.
                if (c == null || path(c) != dialoguePath()) return;
                selected = true;
            }
            if (!selected) return;
            string raw = Get(Get(__1, "Key"), "Original_Text") as string;
            // Associate by source now, but wait for the name/body update to settle.
            Snapshot snapshot = new Snapshot { Original = raw };
            prepared.Remove(__result); prepared.Add(__result, snapshot);
        }
        catch (Exception ex) { log.LogWarning("Context association skipped: " + ex.Message); }
    }

    private static void MovePrefix(object __instance, out object __state)
    {
        __state = new Scope { PreviousSnapshot = active, PreviousTrace = activeTrace };
        active = null; activeTrace = null;
        if (!enabled.Value) return;
        try
        {
            object context = coroutineContext.GetValue(__instance);
            object info = Get(context, "UntranslatedTextInfo");
            Snapshot snapshot = null;
            if (info != null) prepared.TryGetValue(info, out snapshot);
            if (snapshot != null && !ResolveSnapshot(snapshot)) snapshot = null;
            active = snapshot;
            Trace trace;
            if (!traces.TryGetValue(__instance, out trace))
            {
                trace = new Trace { Context = context, Snapshot = snapshot,
                    Id = session + "-" + System.Threading.Interlocked.Increment(ref nextId).ToString("D6"),
                    CurrentSource = Get(context, "UntranslatedText") as string };
                traces.Add(__instance, trace);
                if (context != null && detailLog != null && detailedLogging.Value) WrapResultCallbacks(context, trace);
                if (snapshot != null)
                {
                    LastRequestSummary = "最近一次請求：前文 " + snapshot.Previous.Count + " 段／" + snapshot.Characters + " 字，" + (string.IsNullOrEmpty(snapshot.Speaker) ? "未提供姓名。" : "已附說話者。");
                    if (logging.Value) log.LogInfo("Context request: previous=" + snapshot.Previous.Count + ", chars=" + snapshot.Characters + ", speaker=" + (!string.IsNullOrEmpty(snapshot.Speaker)) + ".");
                }
            }
            activeTrace = trace;
        }
        catch (Exception ex) { log.LogWarning("Context request scope skipped: " + ex.Message); }
    }

    private static bool ResolveSnapshot(Snapshot snapshot)
    {
        if (snapshot.Previous != null) return true;
        DialogueContextTurn turn = collector.History.Find(snapshot.Original);
        if (turn == null) return false;
        snapshot.Previous = DialogueContextHistory.SelectBefore(turn,
            Math.Max(0, Math.Min(16, maxLines.Value)), Math.Max(0, Math.Min(2000, maxChars.Value)));
        snapshot.Speaker = turn.Speaker;
        foreach (DialogueContextTurn t in snapshot.Previous) snapshot.Characters += t.Text.Length;
        return true;
    }

    private static void TranslatePostfix(object __0, ref IEnumerator __result)
    {
        if (!enabled.Value || __result == null) return;
        object info = Get(__0, "UntranslatedTextInfo");
        Snapshot snapshot;
        if (info != null && prepared.TryGetValue(info, out snapshot))
            __result = new AfterFrameEnumerator(__result, FlushPreviousFrame);
    }

    private sealed class AfterFrameEnumerator : IEnumerator, IDisposable
    {
        private readonly IEnumerator inner;
        private readonly Action flush;
        private bool delayed;
        private bool started;
        public AfterFrameEnumerator(IEnumerator source, Action afterFrame) { inner = source; flush = afterFrame; }
        public object Current { get { return started ? inner.Current : null; } }
        public bool MoveNext()
        {
            if (!delayed) { delayed = true; return true; }
            flush();
            started = true;
            return inner.MoveNext();
        }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { IDisposable d = inner as IDisposable; if (d != null) d.Dispose(); }
    }
    private static void MovePostfix(object __instance, bool __result)
    {
        if (__result) return;
        Trace trace;
        if (traces.TryGetValue(__instance, out trace))
        {
            if (logging.Value && trace.Snapshot != null) log.LogInfo("Context request completed: elapsed_ms=" + trace.Timer.ElapsedMilliseconds + ", previous=" + trace.Snapshot.Previous.Count + ".");
            if (!trace.Completed) RecordResult(trace, "ended_without_result", null, "Sakura coroutine ended without an observed completion callback.");
            traces.Remove(__instance);
        }
    }
    private static Exception MoveFinalizer(object __instance, Exception __exception, object __state)
    {
        Scope scope = __state as Scope;
        active = scope == null ? null : scope.PreviousSnapshot;
        activeTrace = scope == null ? null : scope.PreviousTrace;
        if (__exception != null)
        {
            Trace trace;
            if (traces.TryGetValue(__instance, out trace))
            {
                RecordResult(trace, "exception", null, __exception.ToString());
                traces.Remove(__instance);
            }
        }
        return __exception;
    }

    private static void RequestPrefix(object __0)
    {
        if (active == null || !enabled.Value) return;
        try
        {
            IList messages = __0 as IList;
            if (messages == null) return;
            foreach (object message in messages)
                if ((Get(message, "Role") as string) == "system")
                {
                    PropertyInfo content = message.GetType().GetProperty("Content");
                    // Replace the base rules only for a selected dialogue request.
                    content.SetValue(message, "請將日文忠實翻譯成臺灣繁體中文，只輸出譯文。保留原文的換行與空白行，譯文逐行對應，不合併或新增行。" + DialogueContextHistory.Background(active.Previous, active.Speaker), null);
                    return;
                }
        }
        catch (Exception ex) { log.LogWarning("Context prompt injection skipped: " + ex.Message); }
    }

    private static void RequestPostfix(string __result)
    {
        if (activeTrace == null) return;
        try
        {
            Trace trace = activeTrace;
            trace.Attempt++;
            if (detailLog == null || !detailedLogging.Value) return;
            Snapshot snapshot = trace.Snapshot;
            List<object> previous = new List<object>();
            StringBuilder readable = new StringBuilder("[請求 " + trace.Id + " / 嘗試 " + trace.Attempt + "]\r\n");
            readable.Append("時間 UTC：").Append(DateTime.UtcNow.ToString("o")).Append("\r\n");
            readable.Append("附帶上下文：").Append(snapshot == null ? "否（其他 UI 或未分類來源）" : "是（選定對話元件）").Append("\r\n");
            readable.Append("實際送出的姓名標籤：").Append(snapshot == null ? "未提供" : snapshot.Speaker).Append("\r\n前文：\r\n");
            if (snapshot != null) foreach (DialogueContextTurn turn in snapshot.Previous)
            {
                previous.Add(new Dictionary<string, object> { { "speaker_sent", turn.Speaker }, { "original", turn.Text } });
                readable.Append("  ").Append(turn.Speaker).Append("：").Append(turn.Text).Append("\r\n");
            }
            readable.Append("當前原文：\r\n").Append(trace.CurrentSource).Append("\r\n實際請求 JSON：\r\n").Append(__result);
            detailLog.Write(new Dictionary<string, object> {
                { "event", "request" }, { "request_id", trace.Id }, { "attempt", trace.Attempt },
                { "context_attached", snapshot != null }, { "speaker_sent", snapshot == null ? null : snapshot.Speaker },
                { "context_before", previous }, { "current_original", trace.CurrentSource }, { "request_json", __result }
            }, readable.ToString());
        }
        catch (Exception ex) { log.LogWarning("Sakura request logging skipped: " + ex.Message); }
    }

    private static void WrapResultCallbacks(object context, Trace trace)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo completeField = context.GetType().GetField("_complete", flags);
            FieldInfo failField = context.GetType().GetField("_fail", flags);
            if (completeField == null || failField == null) throw new MissingFieldException("Translation completion callbacks");
            Action<string[]> complete = completeField.GetValue(context) as Action<string[]>;
            Action<string, Exception> fail = failField.GetValue(context) as Action<string, Exception>;
            if (complete == null || fail == null) throw new InvalidOperationException("Translation callbacks unavailable");
            completeField.SetValue(context, new Action<string[]>(delegate(string[] values)
            {
                RecordResult(trace, "completed", values, null);
                complete(values);
            }));
            failField.SetValue(context, new Action<string, Exception>(delegate(string message, Exception error)
            {
                RecordResult(trace, "failed", null, message + (error == null ? "" : "\n" + error));
                fail(message, error);
            }));
        }
        catch (Exception ex) { log.LogWarning("Sakura response callback logging unavailable: " + ex.Message); }
    }
    private static void RecordResult(Trace trace, string state, string[] translations, string error)
    {
        if (trace.Completed) return;
        trace.Completed = true;
        if (detailLog == null || detailedLogging == null || !detailedLogging.Value) return;
        try
        {
            detailLog.Write(new Dictionary<string, object> {
                { "event", "result" }, { "request_id", trace.Id }, { "attempt", trace.Attempt },
                { "status", state }, { "elapsed_ms", trace.Timer.ElapsedMilliseconds },
                { "translations", translations }, { "error", error }
            }, "[結果 " + trace.Id + " / 嘗試 " + trace.Attempt + "]\r\n狀態：" + state +
                "\r\n耗時：" + trace.Timer.ElapsedMilliseconds + " ms\r\n回傳譯文（XUnity 後處理前）：\r\n" +
                (translations == null ? "" : string.Join("\r\n", translations)) + (error == null ? "" : "\r\n錯誤：" + error));
        }
        catch (Exception ex) { log.LogWarning("Sakura result logging skipped: " + ex.Message); }
    }
}
