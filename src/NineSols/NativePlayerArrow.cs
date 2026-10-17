using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace YiGodhome;
// Borrow provenance survives the donor shooter's retirement, but never pool reuse.
internal static class NativePlayerArrow {
 private sealed class Lease { internal PoolObject Pool=null!; internal Player Player=null!; internal PlayerWeaponData Weapon=null!; }
 private static readonly Dictionary<int,Lease> leases=new();
 internal static void Retire(PoolObject pool)=>leases.Remove(pool.GetInstanceID());
 internal static void Clear()=>leases.Clear();
 internal static PoolObject[] Owned(Player player)=>leases.Values.Where(l=>l.Player==player&&l.Pool!=null&&l.Pool.isOnScene&&l.Pool.gameObject.activeInHierarchy).Select(l=>l.Pool).ToArray();
 internal static bool IsThunder(Component component,Player player) {
  string name=Resolve(component,player)?.Weapon.weaponPrefab.name??"";
  return name=="ExplodingArrow Shooter 爆破發射器 Lv1"||name=="ExplodingArrow Shooter 爆破發射器 Lv2"||name=="ExplodingArrow Shooter 爆破發射器 Lv3";
 }
 internal static bool IsPiercer(Component component,Player player) {
  var lease=Resolve(component,player);if(lease==null)return false;
  string name=lease.Weapon.weaponPrefab.name;
  return name=="NormalArrow Shoot 穿雲 Lv1"||name=="NormalArrow Shoot 穿雲 Lv2"||name=="NormalArrow Shoot 穿雲 Lv3";
 }
 internal static string WeaponName(Component component,Player player)=>Resolve(component,player)?.Weapon.name.Replace(" ","_")??"unresolved";
 internal static PoolObject? Owner(Component component,Player player)=>Resolve(component,player)?.Pool;
 private static Lease? Resolve(Component component,Player player) {
  var pool=component.GetComponentInParent<PoolObject>();if(pool==null||!pool.isOnScene)return null;
  int id=pool.GetInstanceID();
  if(leases.TryGetValue(id,out var cached)){if(cached.Pool!=pool||cached.Player!=player)throw new InvalidOperationException("Native arrow borrow identity changed.");return cached;}
  var origin=pool;PlayerWeaponData? weapon=null;
  for(int depth=0;depth<8&&pool!=null;depth++) {
   if(leases.TryGetValue(pool.GetInstanceID(),out var ancestor)&&ancestor.Pool==pool&&ancestor.Player==player && pool.isOnScene) {
    if(leases.Count>=128)foreach(var stale in leases.Where(p=>p.Value.Pool==null||!p.Value.Pool.isOnScene).Select(p=>p.Key).ToArray())leases.Remove(stale);
    if(leases.Count>=128)throw new InvalidOperationException("Native arrow borrow capacity exceeded.");
    var inherited=new Lease{Pool=origin,Player=player,Weapon=ancestor.Weapon};leases.Add(id,inherited);return inherited;
   }
   foreach(var data in player.weaponDataCollection.currentAcquiredList)
    if(data is PlayerWeaponData w && pool.OriginalPrefab==w.weaponPrefab)weapon=w;
   var source=pool.lastPlayerComponent;if(source==null)return null;
   if(source.transform.IsChildOf(player.transform)) {
    if(weapon==null)return null;
    if(leases.Count>=128)foreach(var stale in leases.Where(p=>p.Value.Pool==null||!p.Value.Pool.isOnScene).Select(p=>p.Key).ToArray())leases.Remove(stale);
    if(leases.Count>=128)throw new InvalidOperationException("Native arrow borrow capacity exceeded.");
    var lease=new Lease{Pool=origin,Player=player,Weapon=weapon};leases.Add(id,lease);return lease;
   }
   var next=source.GetComponentInParent<PoolObject>();if(next==pool)return null;pool=next;
  }
  return null;
 }
}
