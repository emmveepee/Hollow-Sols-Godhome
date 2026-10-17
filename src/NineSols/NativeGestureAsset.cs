using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace YiGodhome;

// Only sampled native asset references survive acquisition. The script-free
// hierarchy evaluates curves in quarantine and is destroyed before live leases.
internal sealed class NativeGestureAsset : MonoBehaviour
{
    public AnimationClip Clip = null!;
    public GameObject Sampler = null!;
    public SpriteRenderer Sampled = null!;
    public float ReferenceHeight;
    public Vector3 ReferenceScale;
    public float[] Times=Array.Empty<float>();
    public Sprite[] Sprites=Array.Empty<Sprite>();
    public Vector3[] Scales=Array.Empty<Vector3>();
    public Vector3 SampledScale;
    internal static GameObject Extract(Scene scene, Transform storage, bool meditation)
    {
        string name=meditation?"[Cutscene] 給姬占卜":"[Cutscene] 進入無極之地";
        var found=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PlayableDirector>(true))
            .SelectMany(d=>d.playableAsset is TimelineAsset timeline
                ? timeline.GetOutputTracks().OfType<AnimationTrack>().SelectMany(t=>t.GetClips()
                    .Where(c=>c.asset is AnimationPlayableAsset a && a.clip!=null && a.clip.name==name)
                    .Select(c=>(Director:d,Track:t,Clip:((AnimationPlayableAsset)c.asset).clip)))
                : Enumerable.Empty<(PlayableDirector Director,AnimationTrack Track,AnimationClip Clip)>()).ToArray();
        if(found.Length!=1)throw new InvalidOperationException("Native gesture timeline contract changed: "+name+" matches="+found.Length);
        var source=found[0].Director.GetGenericBinding(found[0].Track) as Animator;
        if(source==null)throw new InvalidOperationException("Native gesture actor binding unavailable.");
        var root=new GameObject("Native gesture "+(meditation?"meditation":"arm inspection"));root.SetActive(false);root.transform.SetParent(storage,false);
        try {
            var asset=root.AddComponent<NativeGestureAsset>();asset.Clip=found[0].Clip;
            int count=0;
            asset.Sampler=Copy(source.transform,root.transform,ref count).gameObject;
            // Unity requires an Animator even for explicit non-legacy clip
            // sampling. It has no controller, behaviours or automatic clock.
            asset.Sampler.AddComponent<Animator>().enabled=false;
            float reference=meditation?0:15.5f;
            asset.Clip.SampleAnimation(asset.Sampler,reference);
            asset.Sampled=asset.Sampler.GetComponentsInChildren<SpriteRenderer>(true).Single(r=>r.sprite!=null && r.sprite.name=="HoHoYee_"+(meditation?"Treatment11":"Standingidle7"));
            asset.ReferenceHeight=asset.Sampled.sprite.bounds.size.y;
            asset.ReferenceScale=asset.Sampled.transform.lossyScale;
            if(asset.ReferenceHeight<=0)throw new InvalidOperationException("Native gesture reference size invalid.");
            if(Mathf.Abs(asset.ReferenceScale.x)<.0001f || Mathf.Abs(asset.ReferenceScale.y)<.0001f)throw new InvalidOperationException("Native gesture reference scale invalid.");
            asset.Sample(meditation?1.833334f:12.083334f);
            if(!asset.Sampled.sprite.name.StartsWith(meditation?"HoHoYee_Treatment":"HoHoYee_無極之地",StringComparison.Ordinal))
                throw new InvalidOperationException("Native gesture sprite binding changed.");
            var times=new System.Collections.Generic.List<float>();
            var sprites=new System.Collections.Generic.List<Sprite>();
            var scales=new System.Collections.Generic.List<Vector3>();
            float rate=asset.Clip.frameRate;
            if(rate<1 || rate>120)throw new InvalidOperationException("Native gesture frame rate outside acquisition budget.");
            var segments=meditation?new[]{(0f,1.833334f),(10.5f,12.016666f)}:new[]{(11.916667f,14.666667f)};
            foreach(var segment in segments) {
                int frames=Mathf.CeilToInt((segment.Item2-segment.Item1)*rate);
                for(int frame=0;frame<=frames;frame++) {
                    float time=Mathf.Min(segment.Item2,segment.Item1+frame/rate);
                    asset.Clip.SampleAnimation(asset.Sampler,time);
                    times.Add(time);sprites.Add(asset.Sampled.sprite);scales.Add(asset.Sampled.transform.lossyScale);
                    if(times.Count>1024)throw new InvalidOperationException("Native gesture sample budget exceeded.");
                }
            }
            asset.Times=times.ToArray();asset.Sprites=sprites.ToArray();asset.Scales=scales.ToArray();
            // Only native asset references remain in the template. Live leases
            // no longer clone hundreds of transforms or run SampleAnimation.
            UnityEngine.Object.Destroy(asset.Sampler);asset.Sampler=null!;asset.Sampled=null!;
            return root;
        } catch {UnityEngine.Object.Destroy(root);throw;}
    }
    private static Transform Copy(Transform source,Transform parent,ref int count)
    {
        if(++count>512)throw new InvalidOperationException("Native gesture visual hierarchy exceeds audited budget.");
        var obj=new GameObject(source.name);obj.transform.SetParent(parent,false);
        obj.transform.localPosition=source.localPosition;obj.transform.localRotation=source.localRotation;obj.transform.localScale=source.localScale;
        var sprite=source.GetComponent<SpriteRenderer>();
        if(sprite!=null){var target=obj.AddComponent<SpriteRenderer>();target.sprite=sprite.sprite;target.sharedMaterial=sprite.sharedMaterial;target.enabled=false;}
        foreach(Transform child in source)Copy(child,obj.transform,ref count);
        return obj.transform;
    }
    internal Sprite Sample(float time){
        if(Times.Length==0){Clip.SampleAnimation(Sampler,time);return Sampled.sprite;}
        int index=Array.BinarySearch(Times,time);
        if(index<0)index=Math.Max(0,~index-1);
        index=Math.Min(index,Times.Length-1);
        SampledScale=Scales[index];
        if(index+1<Times.Length && Times[index+1]-Times[index]<=1f/Clip.frameRate+.0001f)
            SampledScale=Vector3.Lerp(Scales[index],Scales[index+1],Mathf.InverseLerp(Times[index],Times[index+1],time));
        return Sprites[index];
    }
}
