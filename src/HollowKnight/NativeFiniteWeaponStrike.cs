using System;
using UnityEngine;

namespace YiGodhome;

// Observe a finite native weapon's real root lifetime. Its changing child
// colliders remain one strike; native activation gives it the next strike.
internal sealed class NativeFiniteWeaponStrike : MonoBehaviour
{
    private long generation,observed,action;
    private Func<long>? allocate;
    private Action<string>? log;
    private int samples;
    private void OnEnable()=>generation++;
    internal void Bind(Func<long> allocate,Action<string>? log) {this.allocate=allocate;this.log=log;}
    internal long Action {
        get {
            if(generation!=observed && allocate!=null) {
                observed=generation;action=allocate();
                if(samples++<8) log?.Invoke("HK_FINITE_WEAPON_STRIKE owner="+name.Replace(' ','_')+" entity="+gameObject.GetInstanceID()+" generation="+generation+" action="+action+" nativeEnable=True frame="+Time.frameCount);
            }
            return action;
        }
    }
}
