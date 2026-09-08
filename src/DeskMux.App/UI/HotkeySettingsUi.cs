using System.Windows.Input;

namespace DeskMux.App.UI;
internal static class HotkeySettingsUi
{
    internal static string Label(CommandGesture gesture) => UIHelpers.PrefixLabel(new AppSettings { PrefixVirtualKey=gesture.VirtualKey, PrefixModifiers=gesture.Modifiers });
    internal static string Shortcut(AppSettings settings, string id) => Label(Hotkeys.Get(settings,Hotkeys.All.Single(d=>d.Id==id)));
    internal static UIElement Build(AppController controller)
    {
        var settings=controller.Sessions.State.Settings;
        var panel=new StackPanel();
        panel.Children.Add(UIHelpers.Text("Hotkeys",30,bold:true));
        panel.Children.Add(UIHelpers.Text("Choose a prefix, then personalize each command. Release pane keeps the application in its session and lets you move it freely. Picker navigation uses standard arrows, Enter and Escape.",color:UIHelpers.Muted));
        var fields=new Dictionary<string,Func<CommandGesture>>();
        var resetters=new List<Action>();
        var setters=new List<Action<CommandGesture>>();
        var searchable=new List<(string Label,FrameworkElement Row)>();
        var search=new TextBox{ToolTip="Search commands",Margin=new Thickness(0,8,0,12)};
        panel.Children.Add(UIHelpers.Text("Search commands"));panel.Children.Add(search);
        search.TextChanged+=(_,_)=>{foreach(var item in searchable)item.Row.Visibility=item.Label.Contains(search.Text,StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;};
        var keys=Enumerable.Range(8,247).Where(k=>Hotkeys.Valid(new(k)) && KeyInterop.KeyFromVirtualKey(k)!=Key.None).ToArray();
        var modifiers=Enumerable.Range(0,16).Select(i=>(PrefixModifiers)i).ToArray();
        Func<CommandGesture> Row(string label, CommandGesture current, CommandGesture defaults)
        {
            var section=new StackPanel(); section.Children.Add(UIHelpers.Text(label,14,bold:true));
            var row=new WrapPanel();
            var mods=new ComboBox {Width=230,ItemsSource=modifiers.Select(m=>m==PrefixModifiers.None?"None":m.ToString()).ToArray(),SelectedIndex=(int)current.Modifiers};
            var key=new ComboBox {Width=160,ItemsSource=keys.Select(UIHelpers.KeyLabel).ToArray(),SelectedIndex=Array.IndexOf(keys,current.VirtualKey)};
            row.Children.Add(mods);row.Children.Add(key);section.Children.Add(row);panel.Children.Add(section);searchable.Add((label,section));
            setters.Add(g=>{mods.SelectedIndex=(int)g.Modifiers;key.SelectedIndex=Array.IndexOf(keys,g.VirtualKey);});
            var record=new TextBox{Width=175,IsReadOnly=true,Text="Click to record keys",ToolTip="Focus here and press your shortcut"};row.Children.Add(record);
            record.GotKeyboardFocus+=(_,_)=>{controller.RecordShortcut(true);record.Text="Press a shortcut…";};
            record.LostKeyboardFocus+=(_,_)=>{controller.RecordShortcut(false);record.Text="Click to record keys";};
            record.PreviewKeyDown+=(_,e)=>{
                var pressed=e.Key==Key.System?e.SystemKey:e.Key;
                var vk=KeyInterop.VirtualKeyFromKey(pressed);
                var held=Keyboard.Modifiers;
                var m=(held.HasFlag(ModifierKeys.Control)?PrefixModifiers.Control:0)|(held.HasFlag(ModifierKeys.Alt)?PrefixModifiers.Alt:0)|(held.HasFlag(ModifierKeys.Shift)?PrefixModifiers.Shift:0)|(held.HasFlag(ModifierKeys.Windows)?PrefixModifiers.Windows:0);
                if(Hotkeys.Valid(new(vk,m))){mods.SelectedIndex=(int)m;key.SelectedIndex=Array.IndexOf(keys,vk);record.Text=Label(new(vk,m));}
                e.Handled=true;
            };
            resetters.Add(()=>{mods.SelectedIndex=(int)defaults.Modifiers;key.SelectedIndex=Array.IndexOf(keys,defaults.VirtualKey);});
            return ()=>new(key.SelectedIndex<0?0:keys[key.SelectedIndex],(PrefixModifiers)mods.SelectedIndex);
        }
        var prefix=Row("Prefix (global)",new(settings.PrefixVirtualKey,settings.PrefixModifiers),new(0x42,PrefixModifiers.Control));
        panel.Children.Add(UIHelpers.Text("AFTER THE PREFIX",11,UIHelpers.Muted,true));
        foreach(var definition in Hotkeys.All) fields.Add(definition.Id,Row(definition.Label,Hotkeys.Get(settings,definition),definition.Default));
        var error=UIHelpers.Text("",color:UIHelpers.Brush("Danger"));
        AppSettings Draft()=>new(){PrefixVirtualKey=prefix().VirtualKey,PrefixModifiers=prefix().Modifiers,CommandBindings=fields.ToDictionary(p=>p.Key,p=>p.Value())};
        var actions=new WrapPanel();
        actions.Children.Add(UIHelpers.Button("Import shortcuts",()=>{
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Shortcut profiles (*.json)|*.json"};
            if(dialog.ShowDialog()!=true)return;
            try{var imported=ShortcutProfile.Import(File.ReadAllText(dialog.FileName));setters[0](new(imported.PrefixVirtualKey,imported.PrefixModifiers));for(var i=0;i<Hotkeys.All.Count;i++)setters[i+1](Hotkeys.Get(imported,Hotkeys.All[i]));error.Text="Imported. Save shortcuts to apply.";}
            catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException){error.Text=ex.Message;}
        }));
        actions.Children.Add(UIHelpers.Button("Export shortcuts",()=>{
            try{var json=ShortcutProfile.Export(Draft());var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Shortcut profiles (*.json)|*.json",FileName="DeskMux-shortcuts.json"};if(dialog.ShowDialog()==true){File.WriteAllText(dialog.FileName,json);error.Text="Shortcuts exported.";}}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){error.Text=ex.Message;}
        }));
        actions.Children.Add(UIHelpers.Button("Save shortcuts",()=>{
            var draft=new AppSettings {PrefixVirtualKey=prefix().VirtualKey,PrefixModifiers=prefix().Modifiers,CommandBindings=fields.ToDictionary(p=>p.Key,p=>p.Value())};
            if(Hotkeys.Validate(draft) is { } problem){error.Text=problem;return;}
            settings.PrefixVirtualKey=draft.PrefixVirtualKey;settings.PrefixModifiers=draft.PrefixModifiers;settings.CommandBindings=draft.CommandBindings;
            controller.SettingsChanged();error.Text="Shortcuts saved.";
        },true));
        actions.Children.Add(UIHelpers.Button("Restore default shortcuts",()=>{foreach(var reset in resetters)reset();error.Text="Defaults selected. Save shortcuts to apply.";}));
        var paused=new CheckBox{Content="Pause keyboard shortcuts",IsChecked=settings.KeyboardPaused};
        paused.Click+=(_,_)=>{settings.KeyboardPaused=paused.IsChecked==true;controller.SettingsChanged();};panel.Children.Add(paused);
        panel.Children.Add(UIHelpers.Text("Command timeout (milliseconds)"));
        var timeout=new Slider{Minimum=500,Maximum=10000,Value=settings.CommandTimeoutMs,Width=300,HorizontalAlignment=HorizontalAlignment.Left};
        timeout.ValueChanged+=(_,_)=>{settings.CommandTimeoutMs=(int)timeout.Value;controller.SettingsChanged();};panel.Children.Add(timeout);
        var dock=new DockPanel();var footer=new StackPanel();footer.Children.Add(error);footer.Children.Add(actions);DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});return dock;
    }
}
