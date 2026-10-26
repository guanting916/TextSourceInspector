using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

[BepInPlugin("local.xunity.textsourceinspector", "Text Source Inspector (IL2CPP)", "0.2.4")]
[BepInDependency("gravydevsupreme.xunity.autotranslator")]
public sealed class TextSourceInspectorIL2CPP : BasePlugin
{
    internal static TextSourceInspector State;
    public override void Load()
    {
        State = new TextSourceInspector(Config, Log);
        AddComponent<TextSourceInspectorHost>();
    }
}

// Only native-compatible signatures are exposed on the injected behaviour.
public sealed class TextSourceInspectorHost : MonoBehaviour
{
    private static bool initialized;
    public TextSourceInspectorHost(IntPtr pointer) : base(pointer) { }
    public void Start()
    {
        try { TextSourceInspectorIL2CPP.State.Initialize(); initialized = true; }
        catch (Exception ex) { BepInEx.Logging.Logger.CreateLogSource("Text Source Inspector").LogError(ex); }
    }
    public void Update() { if (initialized) TextSourceInspectorIL2CPP.State.Tick(); }
    public void OnGUI() { if (initialized) TextSourceInspectorIL2CPP.State.Draw(); }
    public void OnDestroy() { if (initialized) { TextSourceInspectorIL2CPP.State.Shutdown(); initialized = false; } }
}
