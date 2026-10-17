namespace YiGodhome.Bridge;

// A title and the HUD must both reveal before native bar entry is authorized.
// Each authored title phase gets its own lease, even in the same native room.
public sealed class BossTitleGate
{
    private int? room;
    private BossTitleVariant phase;
    private bool shown,admitted;
    private void Observe(int handle,BossTitleVariant title) {
        if(room!=handle || phase!=title){room=handle;phase=title;shown=admitted=false;}
    }
    public bool Ready(int handle,BossTitleVariant title=BossTitleVariant.Native,bool hudShown=true) {
        Observe(handle,title);
        if(shown && hudShown)admitted=true;
        return admitted;
    }
    public void Reveal(int handle,BossTitleVariant title=BossTitleVariant.Native) {Observe(handle,title);shown=true;}
}

// Native Enter is restarted exactly once when a retained widget is re-admitted.
public sealed class BossBarEntry
{
    private bool visible;
    public bool Update(bool show) {bool enter=show && !visible;visible=show;return enter;}
}

// Admit the paired phase as one presentation batch; losing one brother later
// must not hide the surviving bar or restart its entrance.
public sealed class BossBarBatch
{
    private bool paired;
    public bool Ready(BossTitleVariant title,bool titleReady,int liveBars) {
        if(title!=BossTitleVariant.BrothersOroMato){paired=false;return titleReady;}
        if(titleReady && liveBars==2)paired=true;
        return titleReady && paired;
    }
}

public static class BossBarNames
{
    public static string Sisters(BossTitleVariant title)=>title==BossTitleVariant.SistersOfBattle?"Sister's of Battle":"Mantis Lords";
    public static BossTitleVariant SistersTitle(string nativeMainKey)=>nativeMainKey=="SISTERS_MAIN"?BossTitleVariant.SistersOfBattle:BossTitleVariant.MantisLords;
}
