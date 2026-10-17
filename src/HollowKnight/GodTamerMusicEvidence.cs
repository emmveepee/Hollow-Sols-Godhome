using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;

// Scoped installed-owner telemetry. No music selection or native trigger writes.
internal sealed class GodTamerMusicEvidence
{
    private readonly Action<string>? log;
    private readonly bool enabled;
    private int samples;
    private float next;
    private static readonly System.Reflection.FieldInfo sources=AccessTools.Field(typeof(AudioManager),"musicSources");
    private static readonly System.Reflection.FieldInfo cue=AccessTools.Field(typeof(SceneManager),"musicCue");
    private static readonly System.Reflection.FieldInfo regionCue=AccessTools.Field(typeof(MusicRegion),"enterMusicCue");
    internal GodTamerMusicEvidence(Action<string>? log)
    {
        this.log=log;
        enabled=EncounterProfile.Selected.Family==EncounterProfile.GodTamer && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("cheat-probe");
    }
    internal void Update()
    {
        if(!enabled || samples>=16 || Time.realtimeSinceStartup<next) return;
        next=Time.realtimeSinceStartup+1; samples++;
        var manager=GameManager.instance.AudioManager;
        var scene=GameManager.instance.sm;
        if(samples==1 && scene!=null) {
            var regions=scene.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MusicRegion>(true)).Take(8).ToArray();
            log?.Invoke("HK_GOD_TAMER_MUSIC_REGIONS scopedScene=True count="+regions.Length);
            foreach(var region in regions) log?.Invoke("HK_GOD_TAMER_MUSIC_REGION name="+region.name+" active="+region.isActiveAndEnabled+" cue="+((regionCue.GetValue(region) as MusicCue)?.name??"none")+" position="+region.transform.position+" collider="+region.GetComponent<Collider2D>()?.bounds);
        }
        var authored=scene==null?null:cue.GetValue(scene) as MusicCue;
        if(scene!=null) foreach(var fsm in scene.GetComponents<PlayMakerFSM>().Where(f=>f.FsmStates.Any(s=>s.Name=="Boss Statue") && f.FsmStates.Any(s=>s.Name=="Statue Level"))) {
            log?.Invoke("HK_GOD_TAMER_MUSIC_FSM name="+fsm.FsmName.Replace(' ','_')+" active="+fsm.isActiveAndEnabled+" state="+fsm.ActiveStateName.Replace(' ','_')
                +" flags="+string.Join(";",fsm.FsmVariables.BoolVariables.Select(v=>v.Name+":"+v.Value))
                +" cues="+string.Join(";",fsm.FsmVariables.ObjectVariables.Where(v=>v.Name.Contains("MusicCue")).Select(v=>v.Name+":"+(v.Value?.name??"none")))+" frame="+Time.frameCount);
        }
        var music=manager==null?null:sources.GetValue(manager) as AudioSource[];
        log?.Invoke("HK_GOD_TAMER_MUSIC sample="+samples+" scene="+(scene?.gameObject.scene.name??"none")+" authored="+(authored?.name??"none")+" current="+(manager?.CurrentMusicCue?.name??"none")+" sources="+(music?.Length??0)+" frame="+Time.frameCount);
        if(music!=null) foreach(var source in music) if(source!=null)
            log?.Invoke("HK_GOD_TAMER_MUSIC_SOURCE owner="+source.GetInstanceID()+" clip="+(source.clip?.name??"none")+" playing="+source.isPlaying+" mute="+source.mute+" volume="+source.volume+" active="+source.isActiveAndEnabled+" frame="+Time.frameCount);
    }
}
