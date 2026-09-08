using System.Text.Json;
namespace DeskMux.Core;
public sealed class ShortcutProfile
{
    public int Version { get; set; } = 1;
    public CommandGesture Prefix { get; set; } = new(0x42,PrefixModifiers.Control);
    public Dictionary<string,CommandGesture> Commands { get; set; } = [];
    public static string Export(AppSettings settings)
    {
        if(Hotkeys.Validate(settings) is {} error)throw new InvalidDataException(error);
        return JsonSerializer.Serialize(new ShortcutProfile{Prefix=new(settings.PrefixVirtualKey,settings.PrefixModifiers),Commands=Hotkeys.All.ToDictionary(d=>d.Id,d=>Hotkeys.Get(settings,d))},new JsonSerializerOptions{WriteIndented=true});
    }
    public static AppSettings Import(string json)
    {
        var profile=JsonSerializer.Deserialize<ShortcutProfile>(json)??throw new InvalidDataException("Empty shortcut file.");
        if(profile.Version!=1||profile.Commands==null||profile.Commands.Keys.Any(k=>!Hotkeys.All.Any(d=>d.Id==k)))throw new InvalidDataException("Unsupported shortcut profile.");
        var settings=new AppSettings{PrefixVirtualKey=profile.Prefix.VirtualKey,PrefixModifiers=profile.Prefix.Modifiers,CommandBindings=profile.Commands};
        if(Hotkeys.Validate(settings) is {} error)throw new InvalidDataException(error);
        return settings;
    }
}
