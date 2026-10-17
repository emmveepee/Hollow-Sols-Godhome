using System;
using System.Collections.Generic;
using UnityEngine;

namespace YiGodhome;

// A room-owned artwork lease; real Yi physics/state and native frame timing remain authoritative.
internal sealed class NativeGesturePresentation : IDisposable
{
    private static NativeGesturePresentation? current;
    internal static IEnumerable<Renderer> Renderers=>current!=null?current.visible:Array.Empty<Renderer>();
    internal static string FrameKey=>current==null?"":current.kind+"-"+(int)(current.sampleTime*2);
    internal static string SpriteName=>current?.renderer.sprite?.name??"";
    private readonly Player player;
    private readonly NativeContentRepository.Lease lease;
    private readonly NativeGestureAsset asset;
    private readonly SpriteRenderer renderer;
    private readonly Renderer[] visible;
    private readonly bool oldHidden;
    private readonly float scale;
    private bool disposed;
    private readonly NativeContentKind kind;
    private float sampleTime;
    internal NativeGesturePresentation(Player player,NativeContentKind kind)
    {
        if(current!=null)throw new InvalidOperationException("Duplicate native gesture presentation.");
        this.player=player;this.kind=kind;lease=NativeContentRepository.Current!.Borrow(kind);asset=lease.Root.GetComponent<NativeGestureAsset>();
        var obj=new GameObject("Yi native gesture artwork");obj.transform.SetParent(lease.Root.transform,false);renderer=obj.AddComponent<SpriteRenderer>();
        visible=new Renderer[]{renderer};
        renderer.sharedMaterial=player.PlayerSprite.sharedMaterial;
        renderer.sortingLayerID=player.PlayerSprite.sortingLayerID;renderer.sortingOrder=player.PlayerSprite.sortingOrder;
        oldHidden=player.PlayerSprite.forceRenderingOff;
        scale=player.PlayerSprite.bounds.size.y/asset.ReferenceHeight;
        lease.Root.SetActive(true);current=this;
    }
    internal void Sample(float time)
    {
        if(disposed)return;
        sampleTime=time;
        renderer.sprite=asset.Sample(time);
        renderer.transform.position=player.PlayerSprite.transform.position;
        var nativeScale=asset.SampledScale;
        renderer.transform.localScale=new Vector3(scale*Mathf.Abs(nativeScale.x/asset.ReferenceScale.x),scale*Mathf.Abs(nativeScale.y/asset.ReferenceScale.y),1);
        // The approved clips face the viewer; do not inherit a previous combat flip.
        renderer.flipX=false;player.PlayerSprite.forceRenderingOff=true;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(player!=null && player.PlayerSprite!=null)player.PlayerSprite.forceRenderingOff=oldHidden;
        if(current==this)current=null;lease.Dispose();
    }
}
