using System;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

internal sealed class BossHudPresenter : IDisposable
{
    private Texture2D? image;
    private long sequence;
    private bool visible;
    private Rect destination;
    private readonly NativeHudGate nativeHud=new();
    public void Update(VisualFrame? frame, Pose? pose)
    {
        using var cost = HostFrameProbe.Measure(16);
        visible = frame != null && pose != null && frame.GeometryFrame == pose.GeometryFrame && frame.Width > 1;
        if (!visible || frame!.Sequence == sequence) return;
        if (frame.X != 0 || frame.WorldWidth != 1 || frame.Y < 0 || frame.Y + frame.WorldHeight > 1.001f)
            throw new InvalidOperationException("Invalid native boss HUD placement.");
        if (image == null || image.width != frame.Width || image.height != frame.Height)
        { if (image != null) UnityEngine.Object.Destroy(image); image = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false); }
        image.LoadRawTextureData(frame.Rgba); image.Apply(false, false); sequence = frame.Sequence;
        destination = new Rect(0, 1 - frame.Y - frame.WorldHeight, 1, frame.WorldHeight);
    }
    public void Draw()
    {
        if(!NativeBossTitles.BossBarReady || !nativeHud.Read(out var scale))return;
        var matrix=GUI.matrix;
        var pivot=new Vector3(Screen.width*.5f,Screen.height*.5f,0);
        GUI.matrix=Matrix4x4.TRS(pivot,Quaternion.identity,new Vector3(scale.x,scale.y,1))*Matrix4x4.Translate(-pivot)*matrix;
        try {
        if (visible && image != null && (GameManager.instance?.gameState.ToString() == "PLAYING" || GameManager.instance?.gameState.ToString() == "PAUSED"))
            GUI.DrawTexture(new Rect(0, destination.y * Screen.height, Screen.width, destination.height * Screen.height), image, ScaleMode.StretchToFill, true);
        } finally {GUI.matrix=matrix;}
    }
    public void Dispose() { if (image != null) UnityEngine.Object.Destroy(image); visible = false; }
}
