using System;
namespace YiGodhome;

// Sample native collapse and rise clips at their authored rates. The collapse's
// last fallen frame remains visible through the hold without completion events.
internal readonly struct CounterStunFrame
{
    internal readonly int Index;
    internal readonly bool Rising, Complete;
    internal CounterStunFrame(int index,bool rising,bool complete){Index=index;Rising=rising;Complete=complete;}
}
internal static class CounterStunTimeline
{
    internal static CounterStunFrame Sample(float elapsed,float hold,float fps,int frames)
        =>Sample(elapsed,hold,fps,frames,fps,frames);
    internal static CounterStunFrame Sample(float elapsed,float hold,float fps,int frames,float riseFps,int riseFrames)
    {
        if(fps<=0 || frames<1 || riseFps<=0 || riseFrames<1)throw new ArgumentOutOfRangeException(nameof(frames));
        float duration=frames/fps, turn=Math.Max(hold,duration);
        bool rising=elapsed>=turn;
        int index=Math.Min((rising?riseFrames:frames)-1,Math.Max(0,(int)((rising?elapsed-turn:elapsed)*(rising?riseFps:fps))));
        return new CounterStunFrame(index,rising,elapsed>=turn+riseFrames/riseFps);
    }
}
