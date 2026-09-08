namespace DeskMux.App.UI;
internal static class LayoutPresetsUi
{
    internal static UIElement Build(AppController controller, Guid sessionId)
    {
        var panel=new StackPanel();panel.Children.Add(UIHelpers.Text("Saved layouts",16,bold:true));
        var presets=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Id",MinWidth=180};
        var row=new WrapPanel();var name=new TextBox{Width=150,ToolTip="Layout name (for example Coding)"};
        row.Children.Add(name);row.Children.Add(UIHelpers.Button("Save layout",()=>{if(!string.IsNullOrWhiteSpace(name.Text)){controller.Sessions.SaveLayoutPreset(sessionId,name.Text);Reload();}}));
        panel.Children.Add(row);
        panel.Children.Add(presets);
        void Reload(){presets.ItemsSource=controller.Sessions.State.LayoutPresets.Where(p=>p.SessionId==sessionId).ToArray();presets.SelectedIndex=0;}
        var actions=new WrapPanel();actions.Children.Add(UIHelpers.Button("Apply layout",()=>{if(presets.SelectedItem is LayoutPreset p){controller.Sessions.ApplyLayoutPreset(p.Id);controller.ReportError();}}));
        actions.Children.Add(UIHelpers.Button("Delete layout",()=>{if(presets.SelectedItem is LayoutPreset p){controller.Sessions.DeleteLayoutPreset(p.Id);Reload();}}));
        panel.Children.Add(actions);
        var automatic=new CheckBox{Content="Use selected layout when restoring apps"};
        presets.SelectionChanged+=(_,_)=>automatic.IsChecked=presets.SelectedItem is LayoutPreset p && controller.Sessions.State.Sessions.First(s=>s.Id==sessionId).RestorePresetId==p.Id;
        automatic.Click+=(_,_)=>{controller.Sessions.State.Sessions.First(s=>s.Id==sessionId).RestorePresetId=automatic.IsChecked==true?(presets.SelectedItem as LayoutPreset)?.Id:null;controller.SettingsChanged();};
        panel.Children.Add(automatic);Reload();return panel;
    }
}
