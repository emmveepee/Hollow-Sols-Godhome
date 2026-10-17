using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using InControl;
using YiGodhome.Bridge;

namespace YiGodhome;

// Supplies physical state only to the selected native binding listener.
// Native listeners still own neutral/press/release, validation and persistence.
internal static class NativeBindingInput
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    private static IntPtr hostWindow;
    private static bool requested;
    private static bool mouseRequested,mouseAvailable;
    private static int mouseMask;
    private static float mouseScroll;
    internal static bool MouseRequested=>requested && mouseRequested;
    [ThreadStatic] private static int reads;
    private static (int First,int Second)[] keys=Array.Empty<(int,int)>();
    internal static bool HostForeground=>requested && hostWindow!=IntPtr.Zero && GetForegroundWindow()==hostWindow;
    internal static bool HostReady=>HostForeground && (!mouseRequested || mouseAvailable);
    private static bool Allowed=>BindingKeyboard.Allowed(requested,NativeMenu.ControlsOpen,
        NativeInputBridge.BindingHostFresh,HostReady);
    internal static bool Begin(BindingSourceType source,Action<string> log)
    {
        if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return false;
        if(keys.Length==0) {
            var native=(Array)AccessTools.Field(typeof(UnityKeyboardProvider),"KeyMappings").GetValue(null);
            if(native.Length!=132)throw new InvalidOperationException("Native keyboard mapping changed.");
            keys=new (int,int)[native.Length];
            var type=native.GetType().GetElementType()!;
            var first=AccessTools.Field(type,"target0");var second=AccessTools.Field(type,"target1");
            for(int i=0;i<native.Length;i++) {
                var key=native.GetValue(i);
                keys[i]=(BindingKeyboard.VirtualKey(Convert.ToInt32(first.GetValue(key))),BindingKeyboard.VirtualKey(Convert.ToInt32(second.GetValue(key))));
            }
        }
        var hosts=Process.GetProcessesByName("hollow_knight");
        try {if(hosts.Length!=1 || hosts[0].MainWindowHandle==IntPtr.Zero)return false;hostWindow=hosts[0].MainWindowHandle;}
        finally {foreach(var host in hosts)host.Dispose();}
        requested=true;
        mouseRequested=source==BindingSourceType.MouseBindingSource;mouseAvailable=false;mouseMask=0;mouseScroll=0;
        log("NS_NATIVE_BINDING_HOST_INPUT source="+source+" nativeListener=True hostForegroundRequired=True windowSwitch=False");
        return true;
    }
    internal static void End(){requested=false;hostWindow=IntPtr.Zero;mouseRequested=mouseAvailable=false;mouseMask=0;mouseScroll=0;}
    internal static void ObserveMouse(Controls input)
    {
        mouseAvailable=(input.Buttons&Buttons.BindingMouse)!=0 && BindingMouse.Valid(input);
        if(mouseAvailable){mouseMask=BindingMouse.Mask(input);mouseScroll=input.Y;}
    }
    internal static bool Held(BindingSource source)
    {
        if(!requested)return source.GetState(InputManager.ActiveDevice);
        if(!Allowed)return true; // Lost focus/freshness cannot counterfeit release.
        reads++;
        try{return source.GetState(InputManager.ActiveDevice);}finally{reads--;}
    }
    private static bool Detect(PlayerAction __instance,out bool __state)
    {
        __state=false;
        if(!requested || !NativeMenu.IsBindingAction(__instance))return true;
        if(!Allowed)return false;
        reads++;__state=true;return true;
    }
    private static void DetectDone(bool __state){if(__state)reads--;}
    private static bool PhysicalKey(Key __0,ref bool __result)
    {
        if(reads==0)return true;
        __result=false;
        if(!Allowed || (int)__0<0 || (int)__0>=keys.Length)return false;
        var key=keys[(int)__0];
        __result=key.First!=0 && (GetAsyncKeyState(key.First)&0x8000)!=0
            || key.Second!=0 && (GetAsyncKeyState(key.Second)&0x8000)!=0;
        return false;
    }
    private static bool PhysicalMouse(Mouse __0,ref bool __result)
    {
        if(reads==0 || !mouseRequested)return true;
        int index=__0==Mouse.LeftButton?0:__0==Mouse.RightButton?1:__0==Mouse.MiddleButton?2
            :__0==Mouse.Button4?3:__0==Mouse.Button5?4:__0==Mouse.Button6?5:__0==Mouse.Button7?6:-1;
        __result=Allowed && BindingMouse.Held(mouseMask,index);return false;
    }
    private static bool PhysicalScroll(ref float __result)
    {
        if(reads==0 || !mouseRequested)return true;
        __result=Allowed?mouseScroll:0;return false;
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PlayerAction),"DetectBindings"),prefix:new HarmonyMethod(typeof(NativeBindingInput),nameof(Detect)),
            finalizer:new HarmonyMethod(typeof(NativeBindingInput),nameof(DetectDone)));
        harmony.Patch(AccessTools.Method(typeof(UnityKeyboardProvider),"GetKeyIsPressed"),prefix:new HarmonyMethod(typeof(NativeBindingInput),nameof(PhysicalKey)));
        harmony.Patch(AccessTools.Method(typeof(UnityMouseProvider),"GetButtonIsPressed"),prefix:new HarmonyMethod(typeof(NativeBindingInput),nameof(PhysicalMouse)));
        harmony.Patch(AccessTools.Method(typeof(UnityMouseProvider),"GetDeltaScroll"),prefix:new HarmonyMethod(typeof(NativeBindingInput),nameof(PhysicalScroll)));
    }
}
