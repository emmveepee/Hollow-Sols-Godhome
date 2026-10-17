using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Reuse the host's animated mesh/atlas and native Nine Sols colors. No boss art
// or animation replacement; this presentation adapter can bind any mesh target.
internal sealed class HostAttackPresentation : IDisposable
{
    private sealed class Part
    {
        public MeshRenderer Source = null!;
        public MeshFilter Filter = null!;
        public readonly List<MeshRenderer> Copies = new List<MeshRenderer>();
    }
    private readonly List<Part> parts = new List<Part>();
    private readonly Action<string> log;
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private HealthManager? owner;
    private int mode;
    private readonly System.Collections.Generic.HashSet<int> captured = new System.Collections.Generic.HashSet<int>();
    public HostAttackPresentation(Action<string> log)
    {
        this.log = log;

    }
    public void Update(HealthManager? target, ContactFrame? attack, VisualFrame? palette)
    {
        if (owner != target)
        {
            Clear(); owner = target;
            if (target != null)
                foreach (var source in target.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = source.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var part = new Part { Source = source, Filter = filter }; parts.Add(part);
                    for (int i = 0; i < 8; i++)
                    {
                        var obj = new GameObject("YiGodhome Attack Silhouette") { layer = source.gameObject.layer };
                        obj.transform.SetParent(source.transform, false);
                        obj.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        var copy = obj.AddComponent<MeshRenderer>(); copy.sharedMaterial = source.sharedMaterial; copy.enabled = false;
                        part.Copies.Add(copy);
                    }
                }
        }
        int nextMode = attack == null || target == null || target.isDead || palette == null || palette.GeometryFrame != attack.GeometryFrame ? 0
            : attack.Kickable || attack.KickWindup ? 2 : 0;
        if (palette != null && (palette.Width != 3 || palette.Height != 1 || palette.Rgba.Length != 12)) throw new InvalidOperationException("Invalid native attack palette.");
        Color ColorAt(int i) => new Color32(palette!.Rgba[i * 4], palette.Rgba[i * 4 + 1], palette.Rgba[i * 4 + 2], 255);
        foreach (var part in parts)
        {
            for (int i = 0; i < part.Copies.Count; i++)
            {
                var copy = part.Copies[i]; if (copy == null) continue;
                bool show = nextMode != 0 && part.Source != null && part.Source.enabled && part.Source.gameObject.activeInHierarchy;
                copy.enabled = show;
                if (!copy.enabled) continue;
                copy.GetComponent<MeshFilter>().sharedMesh = part.Filter.sharedMesh;
                properties.Clear(); properties.SetTexture("_MainTex", part.Source!.sharedMaterial.mainTexture);
                var color = ColorAt(0); // Native Tai-Chi teal edge only; original boss body is untouched.
                if (!copy.sharedMaterial.HasProperty("_FlashAmount") || !copy.sharedMaterial.HasProperty("_FlashColor"))
                    throw new InvalidOperationException("Host mesh lacks native silhouette flash properties.");
                properties.SetFloat("_FlashAmount", 1); properties.SetColor("_FlashColor", color); copy.SetPropertyBlock(properties);
                float angle = i * Mathf.PI / 4;
                copy.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.055f, Mathf.Sin(angle) * 0.055f, 0.005f);
                copy.sortingLayerID = part.Source.sortingLayerID; copy.sortingOrder = part.Source.sortingOrder - 1;
            }
        }
        if (mode != nextMode) { mode = nextMode; log("HK_ATTACK_PRESENTATION mode=" + mode + " nativePalette=True meshParts=" + parts.Count + " attack=" + attack?.AttackId); }
        if (nextMode > 0 && !captured.Contains(nextMode) && target != null && Camera.main != null)
        {
            string run = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            if (YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
            {
                var obj = new GameObject("YiGodhome Effect Audit Camera");
                var camera = obj.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                var rt = new RenderTexture(512, 512, 24); var image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                var previous = RenderTexture.active;
                try
                {
                    camera.transform.position = new Vector3(target.transform.position.x, target.transform.position.y, Camera.main.transform.position.z);
                    camera.orthographicSize = 4; camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(run, "attack-effect-" + nextMode + ".rgba"), image.GetRawTextureData());
                    captured.Add(nextMode);
                }
                finally { RenderTexture.active = previous; camera.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(obj); }
            }
        }
    }
    private void Clear()
    { foreach (var part in parts) foreach (var copy in part.Copies) if (copy != null) UnityEngine.Object.Destroy(copy.gameObject); parts.Clear(); }
    public void Dispose() { Clear(); }
}
