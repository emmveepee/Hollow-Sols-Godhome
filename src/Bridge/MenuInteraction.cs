using System;

namespace YiGodhome.Bridge;

// Coordinates are normalized within the displayed peer image, bottom-left origin.
public static class MenuPointer
{
    public static bool Contains(float[] quad,float u,float v)
    {
        if(quad.Length!=8 || float.IsNaN(u) || float.IsNaN(v))return false;
        float sign=0,area=0;
        for(int i=0;i<4;i++) {
            int a=i*2,b=((i+1)%4)*2;
            float cross=(quad[b]-quad[a])*(v-quad[a+1])-(quad[b+1]-quad[a+1])*(u-quad[a]);
            if(float.IsNaN(cross) || float.IsInfinity(cross))return false;
            if(Math.Abs(cross)>.0000001f){if(sign!=0 && Math.Sign(sign)!=Math.Sign(cross))return false;sign=cross;}
            area+=quad[a]*quad[b+1]-quad[b]*quad[a+1];
        }
        return Math.Abs(area)>.0000001f;
    }
    public static bool Map(float x,float y,float left,float bottom,float width,float height,out float u,out float v)
    {
        u=v=0;
        if(width<=0 || height<=0)return false;
        u=(x-left)/width;v=(y-bottom)/height;
        return !float.IsNaN(u) && !float.IsNaN(v) && !float.IsInfinity(u) && !float.IsInfinity(v) && u>=0 && u<=1 && v>=0 && v<=1;
    }
    public static bool IsExclusive(Controls input)
    {
        const Buttons metadata=Buttons.HostPaused|Buttons.Keyboard|Buttons.NativePauseMenu|Buttons.ParryDebug|Buttons.EntryRevealed|Buttons.DoorEnter|Buttons.StatueEnter;
        var command=input.Buttons&~metadata;
        if(command==Buttons.BindingMouse)return (input.Buttons&Buttons.Keyboard)!=0 && BindingMouse.Valid(input);
        if(command==Buttons.MenuPointer || command==Buttons.MenuHover)return (input.Buttons&Buttons.Keyboard)!=0 && input.X>=0 && input.Y>=0;
        if(command==Buttons.CloseInventory)return input.X==0 && input.Y==0;
        return (command&(Buttons.MenuPointer|Buttons.MenuHover|Buttons.CloseInventory|Buttons.BindingMouse))==0;
    }
}

public sealed class InventoryEscapeGate
{
    private bool swallowed;
    public bool CloseRequested {get;private set;}
    public void Observe(bool inventoryVisible,bool escapeHeld)
    {
        if(!inventoryVisible)CloseRequested=false;
        if(!escapeHeld)swallowed=false;
    }
    public bool Intercept(bool inventoryVisible,bool escapeHeld)
    {
        if(!escapeHeld)return false;
        if(!inventoryVisible && !swallowed)return false;
        swallowed=true;
        if(inventoryVisible)CloseRequested=true;
        return true;
    }
}

public sealed class MenuCommandPress
{
    private Buttons held;
    public Buttons Advance(Buttons buttons)
    {
        var next=buttons&(Buttons.MenuPointer|Buttons.CloseInventory);
        var pressed=next&~held;held=next;return pressed;
    }
}
