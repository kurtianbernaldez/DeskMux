namespace DeskMux.Core;

public sealed record HotkeyDefinition(string Id, string Label, CommandGesture Default, bool Picker = false, bool Repeat = false, bool Global = false);
public static class Hotkeys
{
    public static readonly IReadOnlyList<HotkeyDefinition> All = Build();
    private static IReadOnlyList<HotkeyDefinition> Build()
    {
        var list = new List<HotkeyDefinition>();
        void Add(string id, string label, int key, PrefixModifiers mods = PrefixModifiers.None, bool picker = false, bool repeat = false, bool global = false)
            => list.Add(new(id,label,new(key,mods),picker,repeat,global));
        for(int i=1;i<=9;i++) Add("Session"+i,"Switch to session "+i,0x30+i);
        Add("Next","Next session",0x4A); Add("Previous","Previous session",0x4B);
        Add("Picker","Open session switcher",0x57,picker:true);
        Add("Create","Create session",0x43); Add("Rename","Rename session",0x52);
        Add("Add","Add focused window",0x41);
        Add("SplitRight","Open pane picker; split left / right",0xDC,picker:true);
        Add("SplitRightAlt","Split right (alternate)",0xDC,PrefixModifiers.Shift,picker:true);
        Add("SplitBelow","Open pane picker; split top / bottom",0xBD,picker:true);
        Add("SplitBelowAlt","Split below (alternate)",0xDE,PrefixModifiers.Shift,picker:true);
        foreach(var (key,name) in new[]{(0x25,"Left"),(0x26,"Up"),(0x27,"Right"),(0x28,"Down")}) {
            Add("Focus"+name,"Focus pane "+name.ToLowerInvariant(),key);
            Add("Resize"+name,"Resize pane "+name.ToLowerInvariant()+" by about 5%",key,PrefixModifiers.Control,repeat:true);
        }
        Add("SwapPrevious","Swap with previous pane",0xDB,PrefixModifiers.Shift);
        Add("SwapNext","Swap with next pane",0xDD,PrefixModifiers.Shift);
        Add("Zoom","Zoom / restore pane",0x5A);
        Add("Undo","Undo pane layout change",0x55);
        Add("Release","Release pane to floating (keep in session)",0x46);
        Add("Remove","Remove window from session without closing",0x58);
        Add("Move","Move window to another session",0x4D,picker:true);
        Add("Detach","Detach session",0x44); Add("Last","Previous active session",0x4C);
        Add("Manager","Open manager",0x53); Add("Cancel","Cancel command mode",0x1B);
        Add("Emergency","Emergency: show all windows (global, even when paused)",0x7B,PrefixModifiers.Control|PrefixModifiers.Alt|PrefixModifiers.Shift,global:true);
        return list;
    }
    public static CommandGesture Get(AppSettings settings, HotkeyDefinition definition)
    {
        if(settings.CommandBindings.TryGetValue(definition.Id,out var value))return value;
        // Introducing Undo must not invalidate a user's previously saved U binding.
        if(definition.Id=="Undo" && settings.CommandBindings.Values.Contains(definition.Default))
            return Enumerable.Range(0x70,24).Select(k=>new CommandGesture(k,PrefixModifiers.Control|PrefixModifiers.Shift)).First(g=>!settings.CommandBindings.Values.Contains(g)&&g!=new CommandGesture(settings.PrefixVirtualKey,settings.PrefixModifiers));
        return definition.Default;
    }
    public static HotkeyDefinition? Resolve(AppSettings settings, CommandGesture gesture, bool global = false) => All.FirstOrDefault(d=>d.Global==global && Get(settings,d)==gesture);
    public static bool Valid(CommandGesture gesture) => gesture.VirtualKey is >= 8 and <= 254 && gesture.VirtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) && ((int)gesture.Modifiers & ~15)==0;
    public static string? Validate(AppSettings settings)
    {
        var used=new Dictionary<CommandGesture,string>();
        var prefix=new CommandGesture(settings.PrefixVirtualKey,settings.PrefixModifiers);
        var emergency=Get(settings,All.Single(d=>d.Global));
        if(!Valid(prefix) || prefix.Modifiers==PrefixModifiers.None) return "Choose a prefix with a modifier.";
        foreach(var definition in All) {
            var gesture=Get(settings,definition);
            if(!Valid(gesture)) return "Choose a valid shortcut for "+definition.Label+".";
            if(gesture==prefix) return definition.Label+" conflicts with the prefix.";
            if(!definition.Global && gesture==emergency) return definition.Label+" conflicts with Emergency.";
            if(!definition.Global && !used.TryAdd(gesture,definition.Label)) return definition.Label+" conflicts with "+used[gesture]+".";
        }
        return null;
    }
}
