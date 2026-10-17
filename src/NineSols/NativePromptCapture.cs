using System;
using System.Linq;
using InControl;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

// Transfer the installed game's genuine controller glyph, never an exported asset.
internal sealed class NativePromptCapture : IDisposable
{
    private readonly VisualEndpoint endpoint;
    private readonly Guid session;
    private readonly Camera camera;
    private readonly SpriteRenderer glyph;
    private readonly RenderTexture target;
    private readonly Texture2D pixels;
    private readonly byte[] rgba;
    private static readonly Func<InputBindingGameSetting,PlayerAction,InputDeviceStyle> NativeDeviceStyle=(Func<InputBindingGameSetting,PlayerAction,InputDeviceStyle>)Delegate.CreateDelegate(typeof(Func<InputBindingGameSetting,PlayerAction,InputDeviceStyle>),HarmonyLib.AccessTools.Method(typeof(InputBindingGameSetting),"FindDeviceStyle"));
    private float next;
    private TMPro.TextMeshPro? fallback;
    private static NativePromptCapture? current;
    private readonly Action<string> log;
    public NativePromptCapture(VisualEndpoint endpoint, Guid session, Action<string> log)
    {
        this.endpoint = endpoint; this.session = session; this.log = log;
        glyph = new GameObject("YiGodhome Native Interaction Glyph").AddComponent<SpriteRenderer>();
        glyph.gameObject.layer = 30;
        glyph.transform.position = new Vector3(100000, 100000, 0);
        camera = new GameObject("YiGodhome Prompt Camera").AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.aspect = 1;
        camera.orthographicSize = 1;
        camera.transform.position = glyph.bounds.center + Vector3.back * 10;
        camera.nearClipPlane = 0.1f; camera.farClipPlane = 20; camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.allowHDR = false; camera.allowMSAA = false;
        target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
        pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        rgba=new byte[256*256*4];glyph.enabled=false;glyph.gameObject.layer=0;current=this;
    }
    private string bindingName="",spriteName="",resolutionKey="";
    private float labelHeight=1,fallbackHeight=1;
    internal static string CurrentBinding {get;private set;}="";
    internal static bool CurrentOpaque {get;private set;}
    private void RefreshGlyph() {
        var action=ApplicationCore.Instance.playerInput.gameplayActions.Hack;
        var setting=PlayerInputBinder.Instance.InputBindingIconSetting;
        // These are the same native action-level resolvers used by
        // InputPromptUILabel, including device overrides and text fallback.
        string text=setting.FindText(action,InputBindingGameSetting.ScenarioSet.DefaultAction);
        string key=text+"|"+action.Owner.LastInputType+"|"+NativeDeviceStyle(setting,action)+"|"+ApplicationCore.Instance.playerInput.IsMouseScheme;
        if(bindingName.Length>0 && resolutionKey==key)return;
        resolutionKey=key;
        var sprite=action.Bindings.Count==0?ScriptableObjectSingleton<GameConfig>.Instance.defaultEmptyIcon:setting.FindIcon(action,InputBindingGameSetting.ScenarioSet.DefaultAction);
        string name=text,art=sprite?.name??"text";
        if(name==bindingName && art==spriteName)return;
        bindingName=name;spriteName=art;
        CurrentBinding=text.Length>1 && text[0]=='[' && text[text.Length-1]==']'?text.Substring(1,text.Length-2):text;
        if(fallback!=null)fallback.gameObject.SetActive(false);
        Array.Clear(rgba,0,rgba.Length);
        Bounds bounds;
        if(sprite!=null) {
            glyph.sprite=sprite;glyph.enabled=true;glyph.gameObject.layer=30;bounds=glyph.bounds;labelHeight=1;
        } else {
            EnsureFallback();labelHeight=fallbackHeight;fallback!.gameObject.SetActive(true);fallback.text=text;fallback.ForceMeshUpdate();
            var local=fallback.textBounds;
            bounds=new Bounds(fallback.transform.TransformPoint(local.center),Vector3.Scale(local.size,fallback.transform.lossyScale));
        }
        camera.orthographicSize=Math.Max(bounds.extents.x,bounds.extents.y)*1.08f;
        camera.transform.position=bounds.center+Vector3.back*10;
        var old=RenderTexture.active;
        try {
            camera.Render();RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,256,256),0,0,false);pixels.Apply(false,false);
            pixels.GetRawTextureData<byte>().CopyTo(rgba);ActorPixels.StraightAlpha(rgba);
        } finally {glyph.enabled=false;glyph.gameObject.layer=0;if(fallback!=null)fallback.gameObject.SetActive(false);RenderTexture.active=old;}
        CurrentOpaque=ActorPixels.Visible(rgba);
        log("NS_PROMPT_BINDING action=Hack inputType="+action.Owner.LastInputType+" binding="+name+" sprite="+art+" nativeSettings=True");
    }
    private void EnsureFallback() {
        if(fallback!=null)return;
        var owner=NativeGameplayUI.Current;
        var roots=new Component?[]{owner?.menuUI,owner?.PausePanelUI,GameCore.Instance?.savePanelUiController};
        var donor=roots.Where(r=>r!=null).SelectMany(r=>r!.GetComponentsInChildren<InputPromptUILabel>(true).Take(64))
            .Select(l=>l.iconFallbackText).FirstOrDefault(t=>t!=null && t.font!=null && t.font.material!=null);
        if(donor==null)throw new InvalidOperationException("Native prompt fallback typography unavailable.");
        log("NS_PROMPT_FALLBACK_TYPOGRAPHY font="+donor.font.name+" material="+(donor.fontSharedMaterial!=null?donor.fontSharedMaterial.name:donor.font.material.name));
        fallback=new GameObject("YiGodhome Native Prompt Text").AddComponent<TMPro.TextMeshPro>();
        fallback.gameObject.layer=30;fallback.transform.position=glyph.transform.position;
        fallback.font=donor.font;fallback.fontSharedMaterial=donor.fontSharedMaterial!=null?donor.fontSharedMaterial:donor.font.material;
        fallback.fontSize=donor.fontSize;fallback.fontStyle=donor.fontStyle;fallback.color=donor.color;
        fallback.alignment=TMPro.TextAlignmentOptions.Center;fallback.enableWordWrapping=false;
        fallback.rectTransform.sizeDelta=new Vector2(2048,256);
        // Keep the native fallback text's font proportions relative to icons.
        fallbackHeight=Mathf.Clamp(donor.fontSize/56f,.4f,1f);
    }
    internal static void FixtureNativeVariants(Action<string> log) {
        if(current==null || HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true)throw new InvalidOperationException("Prompt variant fixture is not authorized.");
        var action=ApplicationCore.Instance.playerInput.gameplayActions.Hack;var owner=action.Owner;
        var original=owner.SaveData();var input=owner.LastInputType;
        try {
            BindingSource?[] variants={new DeviceBindingSource(InputControlType.Action4),new DeviceBindingSource(InputControlType.Action3),
                new KeyBindingSource(Key.F12),new MouseBindingSource(Mouse.LeftButton),new KeyBindingSource(Key.LeftShift,Key.F12),null};
            foreach(var binding in variants) {
                action.ClearBindings();if(binding!=null)action.AddBinding(binding);
                owner.LastInputType=binding?.BindingSourceType==BindingSourceType.DeviceBindingSource?BindingSourceType.DeviceBindingSource:BindingSourceType.KeyBindingSource;
                current.bindingName="";current.RefreshGlyph();
                if(!CurrentOpaque)throw new InvalidOperationException("Native interaction prompt variant rendered no artwork: "+(binding?.Name??"unbound"));
                if(binding==null && CurrentBinding!="?")throw new InvalidOperationException("Unbound prompt advertised a default key.");
                if(binding==null && current.spriteName!=ScriptableObjectSingleton<GameConfig>.Instance.defaultEmptyIcon.name)
                    throw new InvalidOperationException("Unbound prompt did not use the native empty icon.");
                log("NS_PROMPT_VARIANT_PASS binding="+(binding?.Name??"unbound")+" nativeText="+current.bindingName+" artwork="+current.spriteName);
            }
        } finally {owner.LoadData(original);owner.LastInputType=input;current.bindingName="";current.RefreshGlyph();}
    }
    public void Capture(Pose pose)
    {
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 0.1f;
        RefreshGlyph();
        var frame = endpoint.RentFrame(rgba.Length); Array.Copy(rgba, frame.Rgba, rgba.Length);
        frame.Session = session; frame.GeometryFrame = pose.GeometryFrame; frame.NativeFrame = Time.frameCount;
        frame.CaptureTick = System.Diagnostics.Stopwatch.GetTimestamp(); frame.Width = frame.Height = 256;
        frame.WorldWidth=1;frame.WorldHeight=labelHeight;endpoint.PublishPixels(frame);
    }
    public void Dispose()
    {
        if(current==this)current=null;
        if(fallback!=null)UnityEngine.Object.Destroy(fallback.gameObject);
        UnityEngine.Object.Destroy(glyph.gameObject); UnityEngine.Object.Destroy(camera.gameObject);
        target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
    }
}
