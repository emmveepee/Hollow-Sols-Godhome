using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YiGodhome;

internal enum NativeContentKind { JiequanWarning, ClimbArtwork, HallClimbTrim, FlameBurn, ArmInspection, StatueMeditation }

// New native content supplies an extractor and inactive preparation contract.
// Its donor/type manifest is verified separately; this backend never enables donor gameplay.
internal sealed class NativeContentDefinition
{
    internal NativeContentKind Kind { get; }
    internal int SceneIndex { get; }
    internal Func<Scene, Transform, GameObject> Extract { get; }
    internal NativeContentDefinition(NativeContentKind kind, int scene, Func<Scene, Transform, GameObject> extract)
    { Kind = kind; SceneIndex = scene; Extract = extract; }
}

internal static class NativeContentCatalog
{
    internal static readonly NativeContentDefinition[] Definitions = {
        new NativeContentDefinition(NativeContentKind.ArmInspection, 56, (scene, storage)=>NativeGestureAsset.Extract(scene,storage,false)),
        new NativeContentDefinition(NativeContentKind.StatueMeditation, 53, (scene, storage)=>NativeGestureAsset.Extract(scene,storage,true)),
        new NativeContentDefinition(NativeContentKind.FlameBurn, 41, FlameBurn),
        new NativeContentDefinition(NativeContentKind.JiequanWarning, 41, Warning),
        new NativeContentDefinition(NativeContentKind.ClimbArtwork, 58, Climb)
        ,new NativeContentDefinition(NativeContentKind.HallClimbTrim, 53, CleanTrim)
    };
    private static GameObject FlameBurn(Scene source, Transform storage)
    {
        var prefabs = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FxPlayer>(true))
            .Where(f => f.EmitPoolObject != null && f.EmitPoolObject.name == "Fire_FX_damage_Long jiechuan")
            .Select(f => f.EmitPoolObject).Distinct().ToArray();
        if (prefabs.Length != 1) throw new InvalidOperationException("Native Eigong flame reference changed.");
        var area = prefabs[0].GetComponentsInChildren<RCGMaker.Core.BuffDealer>(true).Single();
        var buff = area.buffModulePrefab;
        var fire = buff.GetComponentsInChildren<RCGMaker.Core.AnimatorReferencePlayAction>(true).Single(a=>(string)HarmonyLib.AccessTools.Field(typeof(RCGMaker.Core.AbstractAnimatorPlayAction),"stateName").GetValue(a)=="hud DOT Fire");
        NativeFireHud.BindReference(fire.AnimatorReferenceData);
        var runner = buff.GetComponent<RCGMaker.Core.RealtimeUpdateRunner>();
        var dot = buff.GetComponent<EffectDealer>();
        float interval = (float)HarmonyLib.AccessTools.PropertyGetter(typeof(RCGMaker.Core.RealtimeUpdateRunner), "UpdateIntervalValue").Invoke(runner, null);
        float lifetime = (float)HarmonyLib.AccessTools.PropertyGetter(typeof(RCGMaker.Core.RealtimeUpdateRunner), "LastForSecondsValue").Invoke(runner, null);
        if (!buff.name.StartsWith("[BuffModule] Burn DOT small", StringComparison.Ordinal) || interval != .25f
            || lifetime != .75f || dot.type != EffectType.DOTDamage || dot.FinalValue != 5)
            throw new InvalidOperationException("Native burn damage/timer contract changed.");
        var root = UnityEngine.Object.Instantiate(area.gameObject, storage, false);
        root.SetActive(false); root.name = "Native Flame Burn Collision";
        AutoAttributeManager.AutoReferenceAllChildren(root);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        return root;
    }
    private static GameObject CleanTrim(Scene source, Transform storage)
    {
        var tiles = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true)).Single(t => t.name == "MachineClimbable");
        var cells = new[] { new Vector3Int(150, -327, 0), new Vector3Int(150, -326, 0), new Vector3Int(150, -325, 0), new Vector3Int(150, -324, 0) };
        var art = cells.Select(tiles.GetSprite).ToArray();
        if (art.Any(s => s == null) || art[0].name != "A0_S1_tile_32" || art[1].name != "A0_S1_tile_33" || art[2].name != "A0_S1_tile_33" || art[3].name != "A0_S1_tile_34")
            throw new InvalidOperationException("Native clean wall tile contract changed.");
        var root = new GameObject("Native Hall Climb Trim"); root.SetActive(false); root.transform.SetParent(storage, false);
        try {
            for (int i = 0; i < 10; i++) {
                int sample = i == 0 || i == 9 ? 0 : i % 2 == 0 ? 2 : 1;
                var obj = new GameObject("Native Wall Tile " + i); obj.transform.SetParent(root.transform, false);
                // The authored sprite's PPU determines the segment pitch; raw texture pixels do not.
                obj.transform.localPosition = new Vector3(0, i * art[1].bounds.size.x, 0);
                // Tile34 is an open continuation in the source room. Mirror the
                // verified closed lower end in world Y for a closed upper end.
                if (i == 9) {
                    obj.transform.localScale = new Vector3(1, -1, 1);
                    var cap = new GameObject("Native Closed Upper End"); cap.transform.SetParent(obj.transform, false);
                    obj = cap;
                }
                var matrix = tiles.GetTransformMatrix(cells[sample]);
                obj.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(matrix.m10, matrix.m00) * Mathf.Rad2Deg);
                var sprite = obj.AddComponent<SpriteRenderer>(); sprite.sprite = art[sample]; sprite.flipY = matrix.determinant < 0;
                sprite.color = tiles.GetColor(cells[sample]); sprite.sharedMaterial = tiles.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>().sharedMaterial;
            }
            return root;
        } catch { UnityEngine.Object.Destroy(root); throw; }
    }
    private static GameObject Warning(Scene source, Transform storage)
    {
        var prefabs = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FxPlayer>(true))
            .Where(f => f.name.StartsWith("DangerHintEffect", StringComparison.Ordinal) && f.EmitPoolObject != null && f.EmitPoolObject.name == "Effect_TaiDanger")
            .Select(f => f.EmitPoolObject).Distinct().ToArray();
        if (prefabs.Length != 1) throw new InvalidOperationException("Native warning donor contract changed.");
        var prefab = UnityEngine.Object.Instantiate(prefabs[0], storage); prefab.name = "Effect_TaiDanger";
        prefab.gameObject.SetActive(false);
        AutoAttributeManager.AutoReferenceAllChildren(prefab.gameObject); prefab.OnPrepare();
        return prefab.gameObject;
    }
    private static GameObject Climb(Scene source, Transform storage)
    {
        var walls = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "AT_ClimbEnergy").ToArray();
        if (walls.Length != 1) throw new InvalidOperationException("Native climb donor contract changed.");
        var root = walls[0].gameObject; root.SetActive(false); root.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(root, storage.gameObject.scene); root.transform.SetParent(storage, true);
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
            string type = component.GetType().Name;
            if (!new[] { "HierarchyCullingGroup", "_2dxFX_Distortion", "_2dxFX_Pattern", "SpriteSkin" }.Contains(type))
                throw new InvalidOperationException("Unadmitted native climb dependency: " + type);
            if (type == "HierarchyCullingGroup") component.enabled = false;
        }
        return root;
    }
}
