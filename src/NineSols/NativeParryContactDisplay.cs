using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Incoming-hit parries use the health receiver; outward effects have their own
// dealers. Show both read-only, only during the native parry state.
internal sealed class NativeParryContactDisplay
{
    private readonly (EffectDealer Dealer, Collider2D Collider)[] sources;
    private readonly Player player;
    private readonly (DamageReceiver Damage, EffectReceiver Receiver, Collider2D Collider)[] incoming;
    private readonly List<float[]> paths=new List<float[]>(8);
    private readonly Action<string> log;
    private bool omissionLogged;
    private bool wasParrying;
    private int samples;
    public NativeParryContactDisplay(Player player,Action<string> log)
    {
        this.log=log;
        this.player=player;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var receiverField=typeof(DamageReceiver).GetField("bindReceiver",flags)!;
        var colliderField=typeof(EffectReceiver).GetField("myCollider2D",flags)!;
        incoming=player.GetComponentsInChildren<DamageReceiver>(true).Where(d=>d.bindHealth==player.health)
            .Select(d=>(Damage:d,Receiver:(EffectReceiver)receiverField.GetValue(d)))
            .Where(s=>s.Receiver!=null && s.Receiver.effectType==EffectType.EnemyAttack)
            .Select(s=>(s.Damage,s.Receiver,Collider:(Collider2D)colliderField.GetValue(s.Receiver)))
            .Where(s=>s.Collider!=null && s.Collider.transform.IsChildOf(player.transform)).ToArray();
        var state=player.FindState(PlayerStateType.Parry) as PlayerParryState;
        var ground=state?.parryEffectDealer;
        // Pinned sibling in the same authored attack root; never search scenes
        // or admit unrelated dealers by a broad name/type scan.
        var air=ground==null?null:ground.transform.parent?.Find("ParryAreaAir")?.GetComponent<EffectDealer>();
        sources=new[]{ground,air,state?.JumpSpinKickDealer}.Where(d=>d!=null && d.transform.IsChildOf(player.transform)).Distinct()
            .Select(d=>(Dealer:d!,Collider:d!.MyCollider ?? d.GetComponent<Collider2D>()))
            .Where(s=>s.Collider!=null).ToArray();
        log("NS_PARRY_DISPLAY_BOUND incomingReceivers="+incoming.Length+" nativeStateDealers="+sources.Length+" sources="+string.Join(",",sources.Select(s=>s.Dealer.name+":"+s.Collider.GetType().Name))+" activeOnly=True");
    }
    private void Omitted(string reason) {if(!omissionLogged){omissionLogged=true;log("NS_PARRY_DISPLAY_OMITTED reason="+reason+" noFallbackGeometry=True");}}
    public float[][] Sample(CoordinateMap map)
    {
        paths.Clear();
        bool parrying=player.CurrentStateType==PlayerStateType.Parry;
        if(!parrying){wasParrying=false;return Array.Empty<float[]>();}
        bool trace=!wasParrying && samples++<16;wasParrying=true;
        foreach(var source in incoming) {
            var c=source.Collider;
            if(c==null || source.Damage==null || source.Receiver==null)continue;
            bool active=ParryContactContours.Active(parrying,source.Damage.isActiveAndEnabled && source.Receiver.isActiveAndEnabled,
                c.enabled,c.gameObject.activeInHierarchy,!player.health.IsDead && player.mainAbilities.ParryAbility.IsAcquired);
            if(trace)log("NS_PARRY_DISPLAY_INCOMING source="+c.name+" active="+active+" colliderEnabled="+c.enabled+" nativeParryState=True");
            if(active)SampleCollider(c,map);
        }
        foreach(var source in sources) {
            var dealer=source.Dealer;
            if(dealer==null)continue;
            var c=dealer.MyCollider ?? source.Collider;
            if(trace)log("NS_PARRY_DISPLAY_SAMPLE source="+dealer.name+" collider="+c?.GetType().Name+" dealerEnabled="+dealer.isActiveAndEnabled+" colliderEnabled="+c?.enabled+" hierarchy="+c?.gameObject.activeInHierarchy+" nativeParryState=True");
            if(c==null || !ParryContactContours.Active(parrying,
                dealer.isActiveAndEnabled,c.enabled,c.gameObject.activeInHierarchy,dealer.IsValid))continue;
            SampleCollider(c,map);
        }
        return paths.Count==0?Array.Empty<float[]>():paths.ToArray();
    }
    private void SampleCollider(Collider2D c,CoordinateMap map)
    {
        if(c is PolygonCollider2D polygon) {
            for(int path=0;path<polygon.pathCount;path++) {
                var points=polygon.GetPath(path);
                if(points.Length<3 || points.Length>64){Omitted("polygon-capacity");continue;}
                var coordinates=new float[points.Length*2];
                for(int i=0;i<points.Length;i++){coordinates[i*2]=points[i].x+c.offset.x;coordinates[i*2+1]=points[i].y+c.offset.y;}
                Add(c,coordinates,map);
            }
        }
        else if(c is BoxCollider2D box) {
            if(box.edgeRadius==0)Add(c,ParryContactContours.Box(c.offset.x,c.offset.y,box.size.x,box.size.y),map);
            else Omitted("rounded-box");
        }
        else if(c is CircleCollider2D circle) Add(c,ParryContactContours.Circle(c.offset.x,c.offset.y,circle.radius),map);
        else if(c is CapsuleCollider2D capsule) Add(c,ParryContactContours.Capsule(c.offset.x,c.offset.y,capsule.size.x,capsule.size.y,capsule.direction==CapsuleDirection2D.Vertical),map);
        else Omitted("unsupported-collider");
    }
    private void Add(Collider2D collider,float[] coordinates,CoordinateMap map)
    {
        if(paths.Count>=8){Omitted("path-capacity");return;}
        for(int i=0;i<coordinates.Length;i+=2) {
            var world=collider.transform.TransformPoint(new Vector3(coordinates[i],coordinates[i+1],0));
            var host=map.ToSource(new Point(world.x,world.y));
            if(float.IsNaN(host.X) || float.IsNaN(host.Y) || float.IsInfinity(host.X) || float.IsInfinity(host.Y) || Math.Abs(host.X)>100000 || Math.Abs(host.Y)>100000){Omitted("invalid-coordinate");return;}
            coordinates[i]=host.X;coordinates[i+1]=host.Y;
        }
        paths.Add(coordinates);
    }
}
