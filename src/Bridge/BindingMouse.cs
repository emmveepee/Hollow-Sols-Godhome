using System;

namespace YiGodhome.Bridge;

// Exclusive rebinding input: X holds seven Unity mouse buttons, Y wheel direction.
public static class BindingMouse
{
    public static Controls Sample(int mask,float scroll)=>new Controls((mask&127)/127f,Math.Sign(scroll),Buttons.BindingMouse|Buttons.Keyboard);
    public static int Mask(Controls input)=>(int)Math.Round(input.X*127);
    public static bool Valid(Controls input)=>input.X>=0 && input.X<=1 && Math.Abs(input.X*127-Math.Round(input.X*127))<.0001
        && (input.Y==-1 || input.Y==0 || input.Y==1);
    public static bool Held(int mask,int buttonIndex)=>buttonIndex>=0 && buttonIndex<7 && (mask&(1<<buttonIndex))!=0;
}
