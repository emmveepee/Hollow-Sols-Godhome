using System;
using System.Linq;
using UnityEngine;

namespace YiGodhome;

internal sealed class HubShallowWater : IDisposable
{
    private readonly GameObject floor;
    public const float AnkleDepth = 0.30f;
    public HubShallowWater(Action<string> log, string scene = "GG_Atrium")
    {
        string waterName = scene == "GG_Flukemarm" ? "Surface Water Region (1)" : "Surface Water Region";
        var water = Resources.FindObjectsOfTypeAll<BoxCollider2D>().Single(c =>
            c.gameObject.scene.name == scene && c.name == waterName);
        if (!water.isTrigger || Math.Abs(water.transform.eulerAngles.z) > 0.01f)
            throw new InvalidOperationException("Hub water geometry changed.");
        // Swim trigger origin is below the rendered lake surface (10.72 vs11.69).
        string artwork = scene == "GG_Atrium" ? "lake_water_v02" : "acid_water_top";
        var surface = Resources.FindObjectsOfTypeAll<Transform>().Single(t =>
            t.gameObject.scene.name == scene && t.name == artwork);
        float depth = AnkleDepth;
        float surfaceY=surface.position.y;
        if(scene=="GG_Flukemarm") {
            var waves=surface.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(r=>r.name.StartsWith("acid_top_version_03_030",StringComparison.Ordinal)).ToArray();
            if(waves.Length==0 || waves.Length>16)throw new InvalidOperationException("Flukemarm native water-wave scope changed.");
            surfaceY=waves.Average(r=>r.transform.position.y);
            // The wave pivot is not Yi's visible sole. Native actor artwork
            // sits above its grounded collision origin; 0.30 still rendered
            // him standing on the crest in the player's retest. Lower only
            // this imported pool support, retaining every raised platform.
            depth=AnkleDepth+.50f;
        }
        float top = surfaceY - depth;
        // Disabled authored triggers have empty Collider2D.bounds. Use the
        // exact local box instead; enabling the native swim trigger is not
        // necessary to export a shallow floor.
        var a = water.transform.TransformPoint(water.offset - water.size * .5f);
        var b = water.transform.TransformPoint(water.offset + water.size * .5f);
        // Uumuu's accepted surface uses its authored swim boundary. Flukemarm
        // uses the actual child wave height, not the offset parent pivot.
        if(scene=="GG_Uumuu" || scene=="GG_Uumuu_V")top=Math.Max(a.y,b.y)-depth;
        float left = Math.Min(a.x, b.x), right = Math.Max(a.x, b.x);
        if (!float.IsFinite(left) || !float.IsFinite(right) || right - left <= 0)
            throw new InvalidOperationException("Shallow water has no authored width.");
        floor = new GameObject("YiGodhome Shallow Pool Floor") { layer = 8 };
        floor.transform.position = new Vector3((left + right) * .5f, top - 0.5f, 0);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(right - left, 1);
        log("HK_HUB_SHALLOW_WATER scene=" + scene + " left=" + left + " right=" + right
            + " surface=" + surfaceY + " floor=" + top + " depth=" + depth);
    }
    public void Dispose() { if (floor != null) UnityEngine.Object.Destroy(floor); }
}
