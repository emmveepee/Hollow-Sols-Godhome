using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

// Native prompt ownership/range remains authoritative; replace only its artwork.
internal sealed class InteractionPromptPresenter : IDisposable
{
    private static readonly System.Reflection.FieldInfo visibleField = AccessTools.Field(typeof(PromptMarker), "isVisible");
    private static InteractionPromptPresenter? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.prompt-presentation");
    private readonly HashSet<PromptMarker> markers = new HashSet<PromptMarker>();
    private readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
    private readonly List<Vector3> positions = new List<Vector3>();
    private readonly List<(int Layer, int Order)> bands = new();
    private Texture2D? image;
    private Sprite? sprite;
    private readonly List<SpriteRenderer> glyphs = new List<SpriteRenderer>();
    private uint hash;
    private float labelHeight=1;
    private readonly Action<string> log;
    private int lastCount = -1;
    private int measuredHeight;
    public InteractionPromptPresenter(Action<string> log)
    {
        this.log = log; current = this;
        foreach (var marker in Resources.FindObjectsOfTypeAll<PromptMarker>()) markers.Add(marker);
        harmony.Patch(AccessTools.Method(typeof(PromptMarker), "Show"),
            postfix: new HarmonyMethod(typeof(InteractionPromptPresenter), nameof(Shown)));
    }
    private static void Shown(PromptMarker __instance) => current?.markers.Add(__instance);
    public void Update(VisualFrame? frame, Pose? pose, bool menuOpen, Vector3? rest)
    {
        positions.Clear();
        bands.Clear();
        bool ready = frame != null && pose != null && frame.GeometryFrame == pose.GeometryFrame;
        if (ready && (image == null || hash != frame!.Hash))
        {
            if (image == null)
            {
                image = new Texture2D(frame!.Width, frame.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            }
            image.LoadRawTextureData(frame!.Rgba); image.Apply(false, false); hash = frame.Hash;
            int left=frame.Width,right=-1,bottom=frame.Height,top=-1;
            for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)if(frame.Rgba[(y*frame.Width+x)*4+3]>8){left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
            if(sprite!=null)UnityEngine.Object.Destroy(sprite);
            sprite=right<left?null:Sprite.Create(image,new Rect(left,bottom,right-left+1,top-bottom+1),new Vector2(.5f,.5f),image.height);
            labelHeight=Mathf.Clamp(frame.WorldHeight,.4f,1f);
            foreach(var glyph in glyphs)if(glyph!=null)glyph.sprite=sprite;
        }
        if (!ready) { Restore(); Present(); return; }
        markers.RemoveWhere(m => m == null);
        foreach (var marker in markers)
        {
            if (!marker.gameObject.activeInHierarchy || !(bool)visibleField.GetValue(marker)) continue;
            foreach (var renderer in marker.GetComponentsInChildren<Renderer>(true))
            {
                if (!hidden.ContainsKey(renderer)) hidden.Add(renderer, renderer.enabled);
                renderer.enabled = false;
            }
            if (!menuOpen) {
                var position = marker.transform.position;
                int layer = 0, order = 0;
                var statue = marker.GetComponentInParent<BossStatue>();
                var renderers = statue != null && statue.statueStatePD == "statueStateRadiance"
                    ? statue.GetComponentsInChildren<Renderer>(true) : marker.GetComponentsInChildren<Renderer>(true);
                foreach (var part in renderers) {
                    if (SortingLayer.GetLayerValueFromID(part.sortingLayerID) > SortingLayer.GetLayerValueFromID(layer)
                        || part.sortingLayerID == layer && part.sortingOrder > order) { layer = part.sortingLayerID; order = part.sortingOrder; }
                    if (statue != null && statue.statueStatePD == "statueStateRadiance" && part.enabled)
                        position.z = Math.Min(position.z, part.bounds.min.z - .02f);
                }
                positions.Add(position); bands.Add((layer, order + 1));
            }
        }
        if (!menuOpen && rest.HasValue) { positions.Add(rest.Value); bands.Add((0, 1)); }
        if (positions.Count != lastCount)
        { lastCount = positions.Count; log("HK_INTERACT_PROMPTS visible=" + lastCount + " nativeGlyph=True nativeEligibility=True"); }
        Present();
    }
    private void Present()
    {
        var camera = Camera.main;
        int count = camera == null || sprite == null ? 0 : positions.Count;
        for (int i = 0; i < Math.Max(count, glyphs.Count); i++)
        {
            if (i == glyphs.Count) glyphs.Add(null!);
            if (i < count && glyphs[i] == null)
            {
                var obj = new GameObject("YiGodhome Interaction Glyph") { layer = HeroController.instance.gameObject.layer };
                glyphs[i] = obj.AddComponent<SpriteRenderer>();
                glyphs[i].sprite = sprite;
                log("HK_PROMPT_WORLD_RENDERER nativeModalOrdering=True");
            }
            var glyph = glyphs[i];
            if (glyph == null) continue;
            glyph.enabled = i < count;
            if (i >= count) continue;
            glyph.transform.position = positions[i];
            glyph.sortingLayerID = bands[i].Layer; glyph.sortingOrder = bands[i].Order;
            // tk2d overrides projection; orthographicSize may remain in pixel
            // units. Measure the actual viewport at this renderer's depth.
            float depth = camera!.WorldToViewportPoint(positions[i]).z;
            Vector3 bottom = camera.ViewportToWorldPoint(new Vector3(0.5f, 0, depth));
            Vector3 top = camera.ViewportToWorldPoint(new Vector3(0.5f, 1, depth));
            float size = Vector3.Distance(bottom, top) * (56f * labelHeight / 1080f) / sprite!.bounds.size.y;
            if (float.IsNaN(size) || float.IsInfinity(size) || size <= 0)
                throw new InvalidOperationException("Invalid interaction glyph projection.");
            glyph.transform.localScale = new Vector3(size, size, 1);
            if (measuredHeight != camera.pixelHeight) {
                measuredHeight = camera.pixelHeight;
                float projected = Vector3.Distance(camera.WorldToScreenPoint(glyph.bounds.min),
                    camera.WorldToScreenPoint(new Vector3(glyph.bounds.min.x, glyph.bounds.max.y, glyph.bounds.min.z)));
                log("HK_PROMPT_SIZE camera=" + camera.name + " viewportHeight=" + camera.pixelHeight
                    + " projectedPixels=" + projected.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " orthographicSize=" + camera.orthographicSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " expectedPixels=" + (camera.pixelHeight * 56f / 1080f).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
    private void Restore()
    {
        foreach (var item in hidden) if (item.Key != null) item.Key.enabled = item.Value;
        hidden.Clear();
    }
    public void Dispose()
    {
        Restore(); harmony.UnpatchSelf(); if (current == this) current = null;
        foreach (var glyph in glyphs) if (glyph != null) UnityEngine.Object.Destroy(glyph.gameObject);
        glyphs.Clear();
        if (sprite != null) UnityEngine.Object.Destroy(sprite);
        if (image != null) UnityEngine.Object.Destroy(image);
    }
}
