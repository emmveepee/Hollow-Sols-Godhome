using System;
using System.Collections.Generic;
using System.Reflection;
using YiGodhome;
using HutongGames.PlayMaker;

// Isolated fake engine/patch dispatcher, not an in-game Harmony or visual test.
// Models the inspected native DoTransition -> pending destination ->
// UpdateStateChanges contract, including calls outside the normal update loop.
namespace HarmonyLib {
    public sealed class HarmonyMethod {
        internal readonly MethodInfo Method;
        public HarmonyMethod(Type type,string name){Method=AccessTools.Method(type,name);}
    }
    public static class AccessTools {
        public static MethodInfo Method(Type type,string name)=>type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)!;
    }
    public sealed class Harmony {
        private static readonly Dictionary<MethodInfo,MethodInfo> prefixes=new();
        public Harmony(string id){}
        public void Patch(MethodInfo method,HarmonyMethod prefix){prefixes.Add(method,prefix.Method);}
        public void UnpatchSelf()=>prefixes.Clear();
        internal static bool Run(Fsm owner,string name)=>!prefixes.TryGetValue(AccessTools.Method(typeof(Fsm),name),out var prefix)
            || (bool)prefix.Invoke(null,new object[]{owner})!;
    }
}
namespace HutongGames.PlayMaker {
    public sealed class Fsm {
        public string State="Beam End";
        public string? Pending;
        public int SideEffects,Steps;
        public void Update(){if(!HarmonyLib.Harmony.Run(this,nameof(Update)))return;Steps++;UpdateStateChanges();}
        public void FixedUpdate(){if(HarmonyLib.Harmony.Run(this,nameof(FixedUpdate)))Steps++;}
        public void LateUpdate(){if(HarmonyLib.Harmony.Run(this,nameof(LateUpdate)))Steps++;}
        public void Callback(string target){Pending=target;UpdateStateChanges();}
        public void UpdateStateChanges(){if(!HarmonyLib.Harmony.Run(this,nameof(UpdateStateChanges)))return;if(Pending!=null){State=Pending;Pending=null;SideEffects++;}}
    }
}
static class Program {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main(){
        var guardian=new Fsm();var other=new Fsm();
        var lease=new NativeFsmStepLease(guardian,deferTransitions:true);
        foreach(var target in new[]{"Launch","Roar","Beam"}){
            guardian.Callback(target);guardian.Update();guardian.FixedUpdate();guardian.LateUpdate();
            Check(guardian.State=="Beam End" && guardian.SideEffects==0 && guardian.Steps==0,"Callback escaped Guardian stun into "+target);
        }
        other.Callback("Roar");Check(other.State=="Roar" && other.SideEffects==1,"Unheld boss was suppressed");
        lease.Dispose();lease.Dispose();guardian.Update();
        Check(guardian.State=="Beam" && guardian.Pending==null && guardian.SideEffects==1 && guardian.Steps==1,"Pending native recovery was lost or replayed");
        using(var ordinary=new NativeFsmStepLease(other)){
            other.Callback("Idle");Check(other.State=="Idle","Previously accepted ordinary lease changed transition policy");
        }
        using(var death=new NativeFsmStepLease(guardian,true))guardian.Callback("Dead");
        guardian.Update();Check(guardian.State=="Dead","Pending terminal transition discarded");
        NativeFsmStepLease.ReleaseHooks();
        Console.WriteLine("PASS (fake engine): callbacks cannot start jump/roar/beam during Guardian lease; other owners/ordinary holds unaffected; native pending recovery/death survives release; cleanup is idempotent.");
    }
}
