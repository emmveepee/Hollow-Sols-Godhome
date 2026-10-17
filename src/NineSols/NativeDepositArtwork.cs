using System;
using System.Collections.Generic;
using UnityEngine;
namespace YiGodhome;

// FooDeposit owns an authored renderer graph; native animation/pooling owns
// visibility. Cache that graph instead of allocating renderer arrays every frame.
internal sealed class NativeDepositArtwork
{
    private readonly Dictionary<FooDeposit,Renderer[]> renderers=new();
    internal Renderer[] Get(FooDeposit deposit)
    {
        if(renderers.TryGetValue(deposit,out var cached)) return cached;
        if(renderers.Count>=128) {
            var dead=new List<FooDeposit>();
            foreach(var owner in renderers.Keys) if(owner==null) dead.Add(owner!); // Unity destroyed-object identity remains a valid dictionary key.
            foreach(var owner in dead) renderers.Remove(owner);
            if(renderers.Count>=128) throw new InvalidOperationException("Native talisman artwork capacity exceeded.");
        }
        cached=deposit.GetComponentsInChildren<Renderer>(true);
        renderers.Add(deposit,cached);
        return cached;
    }
    internal void Clear()=>renderers.Clear();
}
