using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;

// Mark the same host thresholds used by the injury policy on the native bar.
// Widgets remain native; these small presentation children have no health authority.
internal sealed class NativeHealthNotchView : IDisposable
{
    private readonly List<SpriteRenderer> marks = new List<SpriteRenderer>();
    private readonly Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1);
    private Renderer? bar;
    private SpriteRendererRCGExt? fill;
    private Vector3 fullMinimum, fullMaximum;
    private bool geometryReady;
    private bool logged;
    internal void Update(UIBossHP view, ContactFrame frame, Action<string> log)
    {
        if (bar == null) {
            fill = (SpriteRendererRCGExt)AccessTools.Field(typeof(UIBossHP), "bossSpriteRenderer").GetValue(view);
            bar = fill.GetComponent<Renderer>();
            if (bar == null) throw new InvalidOperationException("Native boss bar has no notch geometry.");
        }
        while (marks.Count < frame.Notches.Length) {
            var mark = new GameObject("YiGodhome Health Threshold").AddComponent<SpriteRenderer>();
            mark.gameObject.layer = bar.gameObject.layer;
            mark.transform.SetParent(view.transform, false); mark.sprite = sprite; marks.Add(mark);
        }
        // Cache full widget extent before native HP Update shrinks the fill.
        // Keep coordinates in the view so UI relocation/scaling remains supported.
        var nativeBounds = bar.TryGetComponent<Shapes.ShapeRenderer>(out var shape) ? shape.GetWorldBounds() : bar.bounds;
        float authoredWidth = (float)AccessTools.Field(typeof(UIBossHP), "CurrentBarWidth").GetValue(view);
        if (!geometryReady && fill!.Width > 0 && authoredWidth > 0 && nativeBounds.size.x > 0 && nativeBounds.size.y > 0) {
            var maximum = nativeBounds.max;
            // Native UpdateHpFillAmount sets Width = percentage * CurrentBarWidth.
            // Normalize away entrance/fill width before retaining widget geometry.
            maximum.x = nativeBounds.min.x + nativeBounds.size.x * authoredWidth / fill.Width;
            fullMinimum = view.transform.InverseTransformPoint(nativeBounds.min);
            fullMaximum = view.transform.InverseTransformPoint(maximum);
            geometryReady = true;
        }
        if (!geometryReady) return;
        var bounds = new Bounds();
        bounds.SetMinMax(view.transform.TransformPoint(fullMinimum), view.transform.TransformPoint(fullMaximum));
        if (!logged && frame.Notches.Length > 0) {
            logged = true;
            log("NS_PHASE_NOTCH_VIEW encounter=" + frame.Encounter + " entity=" + frame.Entity + " activation=" + frame.TargetGeneration
                + " thresholds=" + string.Join(",", frame.Notches) + " width=" + bounds.size.x + " height=" + bounds.size.y + " layer=" + bar.gameObject.layer);
        }
        for (int i = 0; i < marks.Count; i++) {
            var mark = marks[i]; mark.enabled = i < frame.Notches.Length;
            if (!mark.enabled) continue;
            float fraction = HealthNotches.BarFraction(frame.Notches[i],frame.MaxHealth,frame.BarMaxHealth>0?frame.BarMaxHealth:frame.MaxHealth);
            mark.transform.position = new Vector3(bounds.min.x + bounds.size.x * fraction, bounds.center.y, bounds.center.z - .01f);
            // Boss pixels are captured at 1920px before host scaling. Align every
            // marker to that same raster so fractional thresholds cannot change
            // its apparent width/shape through bilinear sampling.
            var camera = NativeGameplayUI.Current.PlayerUI.GetComponentInParent<Camera>();
            float width = Math.Max(.025f, bounds.size.x * .006f);
            float height = bounds.size.y * 1.3f;
            if (camera != null && camera.orthographic) {
                const float rasterWidth = 1920;
                float rasterHeight = rasterWidth / camera.aspect;
                float pixel = 2 * camera.orthographicSize / rasterHeight;
                float widthPixels = Math.Max(2, Mathf.Round(width / pixel));
                float heightPixels = Math.Max(2, Mathf.Round(height / pixel));
                width = widthPixels * pixel; height = heightPixels * pixel;
                var point = camera.WorldToViewportPoint(mark.transform.position);
                point.x = (Mathf.Round(point.x * rasterWidth - widthPixels / 2) + widthPixels / 2) / rasterWidth;
                point.y = (Mathf.Round(point.y * rasterHeight - heightPixels / 2) + heightPixels / 2) / rasterHeight;
                mark.transform.position = camera.ViewportToWorldPoint(point);
            }
            var parentScale = mark.transform.parent.lossyScale;
            mark.transform.localScale = new Vector3(width / Math.Abs(parentScale.x), height / Math.Abs(parentScale.y), 1);
            mark.sortingLayerID = bar.sortingLayerID; mark.sortingOrder = bar.sortingOrder + 10;
            mark.color = frame.Health >= frame.Notches[i] ? new Color(1, .85f, .42f, 1) : new Color(.5f, .5f, .5f, .5f);
        }
        if(!logged && frame.Notches.Length==0) {
            logged=true;
            int activeMarkers=0;foreach(var mark in marks) if(mark.enabled && mark.gameObject.activeInHierarchy) activeMarkers++;
            log("NS_PHASE_NOTCH_DISABLED encounter="+frame.Encounter+" entity="+frame.Entity+" activation="+frame.TargetGeneration
                +" hostHealth="+frame.Health+" maximum="+frame.MaxHealth+" markers="+marks.Count
                +" activeMarkers="+activeMarkers+" nativeWidget=True frame="+Time.frameCount);
        }
    }
    public void Dispose() { foreach (var mark in marks) if (mark != null) UnityEngine.Object.Destroy(mark.gameObject); marks.Clear(); UnityEngine.Object.Destroy(sprite); }
}
