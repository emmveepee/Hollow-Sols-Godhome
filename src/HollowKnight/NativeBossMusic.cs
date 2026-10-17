using System;
using System.Linq;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine.SceneManagement;
namespace YiGodhome;

// Reuse the exact initialized scene's native cue; never import or replace audio.
internal sealed class NativeBossMusic : IDisposable
{
    private static NativeBossMusic? current;
    private readonly Harmony harmony=new Harmony("local.yigodhome.boss-music");
    private readonly Action<string> log;
    internal NativeBossMusic(Action<string> log) {
        if(current!=null) throw new InvalidOperationException("Duplicate native boss music adapter.");
        this.log=log;current=this;
        harmony.Patch(AccessTools.Method(typeof(ApplyMusicCue),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeBossMusic),nameof(Apply)));
    }
    private static bool Apply(ApplyMusicCue __instance) {
        var adapter=current;var manager=GameManager.instance;
        if(adapter==null || manager?.profileID!=4 || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="GG_God_Tamer"
            || __instance.Owner!=manager.sm?.gameObject || __instance.State.Name!="Boss Statue" || __instance.musicCue.Value?.name!="None") return true;
        var cue=__instance.Fsm.Variables.ObjectVariables.SingleOrDefault(v=>v.Name=="MusicCue")?.Value as MusicCue;
        if(cue==null || cue.name!="GG Heavy") throw new InvalidOperationException("Native God Tamer scene music contract changed.");
        manager.AudioManager.ApplyMusicCue(cue,__instance.delayTime.Value,__instance.transitionTime.Value,false);
        __instance.Finish();
        adapter.log("HK_GOD_TAMER_MUSIC_RESTORED cue="+cue.name.Replace(' ','_')+" nativeSceneAsset=True nativeAudioManager=True nativeSnapshotPreserved=True");
        return false;
    }
    public void Dispose(){harmony.UnpatchSelf();if(current==this)current=null;}
}
