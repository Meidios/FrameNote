using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FrameNote;

internal partial class ShortcutDialog : Window
{
    readonly ShortcutMap edited;
    readonly Action<ShortcutMap> save;
    readonly Action defaultsApp;
    readonly Dictionary<string,Button> rows=new();
    readonly List<Shortcut> working=[];
    ShortcutAction? action;
    int editIndex=-1;
    Shortcut? draft;
    Button? capture,accept;
    bool updating;
    bool Recording=>action!=null && editIndex>=0;

    internal ShortcutDialog(Window owner,ShortcutMap current,Action<ShortcutMap> save,Action defaultsApp){
        InitializeComponent();
        Owner=owner;this.save=save;this.defaultsApp=defaultsApp;edited=new(current);
        Height=Math.Min(770,SystemParameters.WorkArea.Height);
        DeskTheme.Window(this);InputMethod.SetIsInputMethodEnabled(this,false);
        PlusIcon.Content=MakeIcon("plus");CloseEditorButton.Content=MakeIcon("close");
        foreach(var (label,bit) in new[]{("Ctrl",2),("Alt",1),("Shift",4),("Win",8)}){
            var check=new CheckBox{Content=label,Tag=bit,Style=(Style)FindResource("ModifierChip")};
            check.Checked+=GestureChanged;check.Unchecked+=GestureChanged;ModifierChoices.Children.Add(check);
        }
        foreach(var group in Shortcuts.Actions.GroupBy(a=>a.Group)){
            var header=new DockPanel{Margin=new Thickness(0,10,4,5)};
            var reset=IconButton("reset","恢复「"+group.Key+"」默认快捷键",()=>Reset(group));
            DockPanel.SetDock(reset,Dock.Right);header.Children.Add(reset);
            header.Children.Add(new TextBlock{Text=group.Key,FontSize=15,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
            Groups.Children.Add(header);
            foreach(var a in group){
                var row=new Button{Style=(Style)FindResource("ActionRow")};
                AutomationProperties.SetAutomationId(row,a.Id);AutomationProperties.SetName(row,a.Name);
                row.Click+=(_,_)=>OpenEditor(a);rows.Add(a.Id,row);Groups.Children.Add(row);
            }
        }
        RefreshRows();
        PreviewKeyDown+=OnEditorKey;
    }

    Viewbox MakeIcon(string id,int size=16){
        var path=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("sc-"+id),StrokeThickness=1.7,
            StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
        path.SetBinding(Shape.StrokeProperty,new System.Windows.Data.Binding(){Path=new PropertyPath("(0)",System.Windows.Documents.TextElement.ForegroundProperty),RelativeSource=System.Windows.Data.RelativeSource.Self});
        var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(path);
        return new Viewbox{Width=size,Height=size,Child=canvas,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
    }
    Button IconButton(string icon,string label,Action click){
        var button=new Button{Width=29,Height=26,Padding=new Thickness(0),Content=MakeIcon(icon,15),ToolTip=label};
        AutomationProperties.SetName(button,label);button.Click+=(_,_)=>click();return button;
    }
    void RefreshRows(){
        foreach(var a in Shortcuts.Actions){
            var layout=new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(.95,GridUnitType.Star)});
            var name=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            name.Children.Add(new TextBlock{Text=a.Name,VerticalAlignment=VerticalAlignment.Center});
            var icon=MakeIcon(a.Id,15);icon.Margin=new Thickness(7,0,0,0);icon.Opacity=.78;name.Children.Add(icon);layout.Children.Add(name);
            var keys=edited[a.Id];
            var badge=new Grid{MinWidth=86,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)};
            var outline=new Rectangle{RadiusX=5,RadiusY=5,StrokeThickness=1,StrokeDashArray=[3,2]};
            outline.SetResourceReference(Shape.StrokeProperty,keys.Length==0?"border":"shortcutEdge");badge.Children.Add(outline);
            var label=new DockPanel{Margin=new Thickness(9,3,9,3),HorizontalAlignment=HorizontalAlignment.Center};
            label.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,keys.Length==0?"muted":"shortcutSet");
            var device=MakeIcon(a.Kind==InputKind.Keyboard?"keyboard":"mouse",15);device.Margin=new Thickness(0,0,6,0);label.Children.Add(device);
            label.Children.Add(new TextBlock{Text=keys.Length==0?"未设置":keys.Label(a.Kind),TextWrapping=TextWrapping.Wrap});
            badge.Children.Add(label);Grid.SetColumn(badge,1);layout.Children.Add(badge);rows[a.Id].Content=layout;
            AutomationProperties.SetHelpText(rows[a.Id],keys.Length==0?"未设置":keys.Label(a.Kind));
        }
    }
    void Reset(IEnumerable<ShortcutAction> group){
        var restored=new ShortcutMap(edited);
        foreach(var a in group)restored[a.Id]=a.Defaults;
        if(Shortcuts.Conflict(restored) is {} conflict){Message(SaveError,conflict);return;}
        foreach(var a in group)edited[a.Id]=restored[a.Id];
        Message(SaveError,null);RefreshRows();
    }
    static void Message(TextBlock block,string? text){block.Text=text??"";block.Visibility=text==null?Visibility.Collapsed:Visibility.Visible;}
    void ResetAll(object sender,RoutedEventArgs e)=>Reset(Shortcuts.Actions);
    void OpenDefaultApps(object sender,RoutedEventArgs e)=>defaultsApp();
    void CancelSettings(object sender,RoutedEventArgs e)=>Close();
    void SaveSettings(object sender,RoutedEventArgs e){
        try{save(edited);DialogResult=true;}
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException){Message(SaveError,ex.Message);}
    }

    void OpenEditor(ShortcutAction a){
        action=a;working.Clear();working.AddRange(edited[a.Id]);editIndex=-1;draft=null;
        string type=a.Kind switch{InputKind.Keyboard=>"按键",InputKind.Modifier=>"修饰键",_=>"鼠标"};
        EditorTitle.Text=$"「{a.Name}」的{type}配置";
        SettingsPage.IsEnabled=false;Overlay.Visibility=Visibility.Visible;
        if(working.Count==0)StartBinding(0);
        else{RenderEditor();FocusBinding(((DockPanel)BindingRows.Children[0]).Children.OfType<Button>().Last());}
    }
    void EndEditor(){
        if(action==null)return;
        var row=rows[action.Id];action=null;editIndex=-1;
        Overlay.Visibility=Visibility.Collapsed;SettingsPage.IsEnabled=true;row.Focus();
    }
    void CancelEditor(object sender,RoutedEventArgs e)=>EndEditor();
    void ConfirmEditor(object sender,RoutedEventArgs e){
        if(action==null || (Recording&&!CommitDraft()))return;
        edited[action.Id]=working.ToArray();Message(SaveError,null);RefreshRows();EndEditor();
    }
    void AddKey(object sender,RoutedEventArgs e)=>StartBinding(working.Count);
    void StartBinding(int index){
        editIndex=index;draft=index<working.Count && action!.Kind!=InputKind.Keyboard?working[index]:null;
        RenderEditor();FocusBinding(capture!);
    }
    void FocusBinding(Button button){
        // A newly opened overlay must be arranged before its first key can receive focus.
        Overlay.UpdateLayout();
        button.BringIntoView();
        button.Focus();
    }
    void Remove(int index){
        working.RemoveAt(index);editIndex=-1;draft=null;RenderEditor();AddBinding.Focus();
    }
    void RenderEditor(){
        if(action==null)return;
        BindingRows.Children.Clear();capture=accept=null;
        for(int i=0;i<working.Count+(editIndex==working.Count?1:0);i++){
            int index=i;
            var row=new DockPanel{Margin=new Thickness(0,0,0,8)};
            bool active=index==editIndex;
            var command=active?IconButton("check","确认此绑定",()=>{
                if(CommitDraft()){RenderEditor();AddBinding.Focus();}
            }):IconButton("trash","删除 "+working[i].Label(action.Kind),()=>Remove(index));
            command.IsEnabled=active||!Recording;
            command.SetResourceReference(ForegroundProperty,active?"shortcutSet":"danger");
            if(active)accept=command;
            DockPanel.SetDock(command,Dock.Right);row.Children.Add(command);
            var key=new Button{HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,3,10,3),
                MaxWidth=310,IsEnabled=active||!Recording};
            if(active){
                capture=key;key.SetResourceReference(BorderBrushProperty,"link");
                AutomationProperties.SetAutomationId(key,"recordBinding");
                key.PreviewMouseDown+=(_,e)=>{
                    if(action?.Kind is not (InputKind.Click or InputKind.Drag) || Keyboard.Modifiers==ModifierKeys.None)return;
                    int vk=e.ChangedButton switch{MouseButton.Left=>1,MouseButton.Right=>2,MouseButton.Middle=>4,MouseButton.XButton1=>5,MouseButton.XButton2=>6,_=>0};
                    draft=new((int)Keyboard.Modifiers,vk);e.Handled=true;RenderEditor();FocusBinding(capture!);
                };
                key.PreviewMouseWheel+=(_,e)=>{
                    if(action?.Kind!=InputKind.Wheel)return;
                    draft=new((int)Keyboard.Modifiers,Shortcut.Wheel);e.Handled=true;RenderEditor();FocusBinding(capture!);
                };
            }else{
                AutomationProperties.SetAutomationId(key,"editBinding"+i);
                key.Content=working[i].Label(action.Kind);key.Click+=(_,_)=>StartBinding(index);
            }
            row.Children.Add(key);BindingRows.Children.Add(row);
        }
        GestureOptions.Visibility=Recording && action.Kind!=InputKind.Keyboard?Visibility.Visible:Visibility.Collapsed;
        if(GestureOptions.Visibility==Visibility.Visible){
            updating=true;
            foreach(CheckBox mod in ModifierChoices.Children)mod.IsChecked=((draft?.Mods??0)&(int)mod.Tag)!=0;
            MouseChoice.Items.Clear();
            bool modifierOnly=action.Kind==InputKind.Modifier;
            MouseChoice.Visibility=GestureCaption.Visibility=modifierOnly?Visibility.Collapsed:Visibility.Visible;
            int[] choices=action.Kind==InputKind.Wheel?[Shortcut.Wheel]:modifierOnly?[]:[1,2,4,5,6];
            foreach(int vk in choices)MouseChoice.Items.Add(new ComboBoxItem{Content=new Shortcut(0,vk).Label(action.Kind),Tag=vk});
            MouseChoice.SelectedIndex=draft is {} saved?Array.IndexOf(choices,saved.Vk):choices.Length==1?0:-1;
            updating=false;ReadGesture();
        }
        AddBinding.IsEnabled=!Recording;UpdateDraft();
    }
    void GestureChanged(object sender,RoutedEventArgs e){
        if(updating || !Recording || action!.Kind==InputKind.Keyboard)return;
        ReadGesture();UpdateDraft();
    }
    void ReadGesture(){
        int mods=ModifierChoices.Children.OfType<CheckBox>().Where(b=>b.IsChecked==true).Sum(b=>(int)b.Tag);
        int? vk=action!.Kind==InputKind.Modifier?0:(int?)(MouseChoice.SelectedItem as ComboBoxItem)?.Tag;
        draft=vk==null?null:new Shortcut(mods,vk.Value);
    }
    string? Problem(Shortcut key){
        if(Shortcuts.Problem(action!,key) is {} invalid)return invalid;
        if(working.Where((_,i)=>i!=editIndex).Contains(key))return "这个操作已使用此快捷键。";
        var owner=Shortcuts.Actions.FirstOrDefault(a=>a!=action && edited[a.Id].Contains(key));
        return owner==null?null:$"此快捷键已用于「{owner.Name}」，请换一个。";
    }
    void UpdateDraft(){
        string? error=Recording && draft is {} key?Problem(key):null;
        Message(EditorError,error);
        bool valid=draft!=null && error==null;
        if(capture!=null){
            capture.Content=draft?.Label(action!.Kind) is {Length:>0} text?text:action!.Kind==InputKind.Keyboard?"请按下按键":"请选择组合";
            accept!.IsEnabled=valid;
        }
        ApplyBinding.IsEnabled=!Recording||valid;
    }
    bool CommitDraft(){
        if(draft is not {} key || Problem(key)!=null)return false;
        if(editIndex==working.Count)working.Add(key);else working[editIndex]=key;
        editIndex=-1;draft=null;return true;
    }
    void OnEditorKey(object sender,KeyEventArgs e){
        if(action==null)return;
        if(Recording && action.Kind==InputKind.Keyboard && capture!.IsKeyboardFocusWithin && e.Key!=Key.Tab){
            var key=Shortcut.From(e);e.Handled=true;
            if(e.IsRepeat)return;
            if(key.Vk is 16 or 17 or 18){
                draft=null;UpdateDraft();capture.Content=new Shortcut(key.Mods,0).Label(InputKind.Modifier)+" + …";return;
            }
            draft=key;UpdateDraft();
        }else if(e.Key==Key.Escape && !MouseChoice.IsDropDownOpen){e.Handled=true;EndEditor();}
    }
}
