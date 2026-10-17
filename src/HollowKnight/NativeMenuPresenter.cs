using System;
using UnityEngine;
using UnityEngine.UI;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

internal sealed class NativeMenuPresenter : IDisposable
{
    private static NativeMenuPresenter? current;
    private readonly InventoryEscapeGate inventoryEscape=new InventoryEscapeGate();
    private float inventoryCloseAt;
    private bool inventoryOpen;
    private bool mouseBinding;
    private Vector2 lastPointer;
    private bool pointerObserved;
    private float nextPointerHover;
    internal NativeMenuPresenter()=>current=this;
    internal static bool InterceptInventoryEscape()
    {
        var owner=current;if(owner==null || !Application.isFocused)return false;
        bool pending=owner.inventoryEscape.CloseRequested;
        bool consumed=owner.inventoryEscape.Intercept(owner.inventoryOpen,Input.GetKey(KeyCode.Escape));
        if(!pending && owner.inventoryEscape.CloseRequested)owner.inventoryCloseAt=Time.realtimeSinceStartup;
        return consumed;
    }
    internal static Controls MenuInput(Controls fallback)
    {
        var owner=current;
        if(owner==null)return fallback;
        // An expired command must not strand the inventory. Renew only this
        // idempotent close until the explicit native closed image arrives.
        if(owner.inventoryEscape.CloseRequested)return new Controls(0,0,Buttons.Keyboard
            | ((Time.realtimeSinceStartup-owner.inventoryCloseAt)%.4f<.12f?Buttons.CloseInventory:Buttons.None));
        if(owner.controlsOpen && owner.mouseBinding && Application.isFocused) {
            int mask=0;for(int i=0;i<7;i++)if(Input.GetMouseButton(i))mask|=1<<i;
            owner.pointerObserved=false;
            return BindingMouse.Sample(mask,Input.mouseScrollDelta.y);
        }
        if(!owner.controlsOpen || HollowSolsNativePause.PeerBinding || !Application.isFocused || owner.image==null){owner.pointerObserved=false;return fallback;}
        var mouse=(Vector2)Input.mousePosition;
        bool click=Input.GetMouseButtonDown(0);
        bool moved=owner.pointerObserved && (mouse-owner.lastPointer).sqrMagnitude>.25f;
        if(!owner.pointerObserved){owner.pointerObserved=true;owner.lastPointer=mouse;}
        if(!click && !moved)return fallback;
        // Bound hover traffic; stationary mouse must not suppress navigation.
        if(!click && (Time.realtimeSinceStartup<owner.nextPointerHover || fallback.Buttons!=Buttons.None || Math.Abs(fallback.X)>.2f || Math.Abs(fallback.Y)>.2f))return fallback;
        owner.lastPointer=mouse;owner.nextPointerHover=Time.realtimeSinceStartup+1f/30;
        float width=Screen.width,height=Screen.height,aspect=(float)owner.image.width/owner.image.height;
        if(width/height>aspect)width=height*aspect;else height=width/aspect;
        if(!MenuPointer.Map(Input.mousePosition.x,Input.mousePosition.y,(Screen.width-width)*.5f,(Screen.height-height)*.5f,width,height,out float u,out float v))return fallback;
        return new Controls(u,v,(click?Buttons.MenuPointer:Buttons.MenuHover)|Buttons.Keyboard);
    }
    private Texture2D? image;
    private long sequence;
    private long geometry;
    private bool froze;
    private float previousScale;
    public bool Open { get; private set; }
    private bool controlsOpen;
    private bool rootOpen;
    private RawImage? overlay;
    private void OrderOverlay() {
        if(overlay==null) {
            var ui=UIManager.instance;
            var pause=ui==null?null:HarmonyLib.AccessTools.Field(typeof(UIManager),"pauseMenuScreen").GetValue(ui) as MenuScreen;
            var canvas=pause?.GetComponentInParent<Canvas>()?.rootCanvas;
            if(canvas==null)return;
            var obj=new GameObject("YiGodhome native peer menu",typeof(RectTransform),typeof(RawImage),typeof(LayoutElement));
            obj.layer=canvas.gameObject.layer;obj.transform.SetParent(canvas.transform,false);
            obj.GetComponent<LayoutElement>().ignoreLayout=true;
            overlay=obj.GetComponent<RawImage>();overlay.raycastTarget=false;
        }
        // The forwarded HUD is the first sibling of this same native canvas.
        // Keep the peer menu above it instead of mixing IMGUI/canvas order.
        overlay.transform.SetAsLastSibling();
        overlay.enabled=Open && image!=null && (GameManager.instance?.gameState.ToString()=="PLAYING" || HollowSolsNativePause.Active);
        overlay.texture=image;
        float aspect=image==null?1:(float)image.width/image.height;
        float screen=(float)Screen.width/Screen.height;
        var half=screen>aspect?new Vector2(aspect/screen*.5f,.5f):new Vector2(.5f,screen/aspect*.5f);
        var rect=overlay.rectTransform;rect.anchorMin=Vector2.one*.5f-half;rect.anchorMax=Vector2.one*.5f+half;
        rect.offsetMin=rect.offsetMax=Vector2.zero;
    }
    public void Update(VisualFrame? frame, Pose? pose)
    {
        // Missing pixels are not an acknowledgement of native menu closure.
        // Retain the current session's image until an explicit clear arrives;
        // connection teardown disposes this presenter independently of pixels.
        if(frame!=null) {
            Open=pose!=null && frame.GeometryFrame==pose.GeometryFrame && frame.Width>1;
            controlsOpen=Open && frame.ControlsMenu;
            rootOpen=Open && frame.RootMenu;
            inventoryOpen=Open && frame.InventoryMenu;
            mouseBinding=controlsOpen && frame.ControlsMouseCapture;
            HollowSolsNativePause.PeerBinding=controlsOpen && frame.ControlsBinding;
        }
        else if(pose!=null && pose.GeometryFrame!=geometry)Open=false;
        if(!Open){controlsOpen=false;rootOpen=false;inventoryOpen=false;mouseBinding=false;HollowSolsNativePause.PeerBinding=false;}
        if(!controlsOpen || HollowSolsNativePause.PeerBinding)pointerObserved=false;
        inventoryEscape.Observe(inventoryOpen,Input.GetKey(KeyCode.Escape));
        HollowSolsNativePause.PeerVisible=controlsOpen;
        // A native pause already owns the clock. Never acquire its zero scale
        // and restore that zero after the native resume coroutine completes.
        bool playing=GameManager.instance?.gameState.ToString()=="PLAYING";
        if(!playing)froze=false;
        bool pauseWorld = Open && !rootOpen;
        if (pauseWorld && !froze && playing) { previousScale = Time.timeScale>0?Time.timeScale:1; froze = true; }
        if (froze && GameManager.instance?.gameState.ToString() == "PLAYING")
        {
            Time.timeScale = pauseWorld ? 0 : previousScale;
            if (!pauseWorld) froze = false;
        }
        OrderOverlay();
        if (!Open || frame==null || frame.Sequence == sequence) return;
        geometry=frame.GeometryFrame;
        if (image == null || image.width != frame.Width || image.height != frame.Height)
        { if (image != null) UnityEngine.Object.Destroy(image); image = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false); }
        image.LoadRawTextureData(frame.Rgba); image.Apply(false, false); sequence = frame.Sequence;
        OrderOverlay();
    }
    internal void FixtureMissingPixels(Pose? pose) {
        if(!Open || image==null)throw new InvalidOperationException("Controls pixels were not present before gap injection.");
        Update(null,pose);Update(null,null);
        if(!Open || !HollowSolsNativePause.PeerVisible)throw new InvalidOperationException("Missing Controls pixels released the native menu lease.");
    }
    public void Draw()
    {
        if (overlay==null && Open && image != null && (GameManager.instance?.gameState.ToString() == "PLAYING" || HollowSolsNativePause.Active))
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), image, ScaleMode.ScaleToFit, true);
    }
    public void Dispose() { if(current==this)current=null; if(overlay!=null)UnityEngine.Object.Destroy(overlay.gameObject); if (image != null) UnityEngine.Object.Destroy(image); if (froze && GameManager.instance?.gameState.ToString() == "PLAYING") Time.timeScale = previousScale; Open = controlsOpen = rootOpen = inventoryOpen = mouseBinding = HollowSolsNativePause.PeerVisible = HollowSolsNativePause.PeerBinding = false; }
}
