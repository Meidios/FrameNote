using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FrameNote;

internal partial class ShortcutDialog : Window
{
    readonly ShortcutMap edited;
    readonly EditorPreferences initialPreferences;
    readonly Action<EditorPreferences,bool,string,OutputSettings> save;
    readonly Desk desk;
    readonly Dictionary<string,Button> rows=new();
    readonly Dictionary<string,DockPanel> groupHeaders=new();
    Shortcut? searchBinding;
    readonly List<Shortcut> working=[];
    readonly List<string> presetColors=[];
    readonly List<ToolbarEntry> toolbar=[];
    ShortcutAction? action;
    int editIndex=-1;
    Shortcut? draft;
    Button? capture,accept;
    bool updating;
    bool Recording=>action!=null && editIndex>=0;

    internal ShortcutDialog(Desk owner,EditorPreferences current,bool automatic,string language,OutputSettings output,Action<EditorPreferences,bool,string,OutputSettings> save){
        InitializeComponent();
        IssueButton.ToolTip=Updates.NewIssue;
        HomeButton.ToolTip=Updates.Home;
        var icon=new IconBitmapDecoder(new Uri("pack://application:,,,/FrameNote;component/Assets/FrameNote.ico"),
            BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        AboutIcon.Source=icon.Frames.MaxBy(frame=>frame.PixelWidth);
        Owner=desk=owner;this.save=save;initialPreferences=current;edited=new(current.Bindings);
        DefaultRulerUnit.SelectedValue=current.RulerUnit;
        for(double zoom=1;zoom<=Drawing.MaxZoom;zoom+=.5)
            DefaultLensZoom.Items.Add(new ComboBoxItem{Tag=zoom,Content=zoom.ToString("0.#")+"×"});
        DefaultLensZoom.SelectedValue=current.LensZoom;
        DefaultOpacity.Text=current.DefaultOpacity.ToString();toolbar.AddRange(current.Toolbar);RefreshToolbar();
        presetColors.AddRange(current.PresetColors);RefreshPresetRows();
        AutomaticUpdates.IsChecked=automatic;
        InterfaceLanguage.SelectedValue=language;
        ExportDirectory.Text=output.ExportDirectory;SetExportBar(output.ExportBar);SetRecordingFields(output);
        foreach(var column in ExcelReview.Columns){
            var check=new CheckBox{Tag=column.Field,Content=L.Text(column.Label),Margin=new Thickness(0,5,6,5)};
            AutomationProperties.SetAutomationId(check,"ExcelColumn"+column.Field);
            ExcelColumnChoices.Children.Add(check);
        }
        SetExcelTemplate(output.Excel);
        CurrentVersion.Text=L.Text("当前版本  ")+Updates.Running;
        AboutVersion.Text=L.Format($"版本 {Updates.Running}  ·  Windows x64");
        AboutDescription.Text=Desk.ProductDescription;AboutAuthor.Text=Desk.Author;
        Height=Math.Min(700,SystemParameters.WorkArea.Height);
        DeskTheme.Dialog(this);InputMethod.SetIsInputMethodEnabled(this,false);
        FindKeyIcon.Content=MakeIcon("keyboard",15);ClearSearch.Content=MakeIcon("close",14);
        PlusIcon.Content=MakeIcon("plus");CloseEditorButton.Content=MakeIcon("close");
        foreach(var (label,bit) in new[]{("Ctrl",2),("Alt",1),("Shift",4),("Win",8)}){
            var check=new CheckBox{Content=label,Tag=bit,Style=(Style)FindResource("ModifierChip")};
            check.Checked+=GestureChanged;check.Unchecked+=GestureChanged;ModifierChoices.Children.Add(check);
        }
        foreach(var group in Shortcuts.Actions.GroupBy(a=>a.Group)){
            var header=new DockPanel{Margin=new Thickness(0,10,4,5)};
            var reset=IconButton("reset",L.Format($"恢复「{group.Key}」默认快捷键"),()=>Reset(group));
            DockPanel.SetDock(reset,Dock.Right);header.Children.Add(reset);
            header.Children.Add(new TextBlock{Text=group.Key,FontSize=15,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
            Groups.Children.Add(header);groupHeaders.Add(group.Key,header);
            foreach(var a in group){
                var row=new Button{Style=(Style)FindResource("ActionRow")};
                AutomationProperties.SetAutomationId(row,a.Id);AutomationProperties.SetName(row,a.Name);
                row.Click+=(_,_)=>OpenEditor(a);rows.Add(a.Id,row);Groups.Children.Add(row);
            }
        }
        RefreshRows();
        PreviewKeyDown+=OnEditorKey;
    }

    void ResizeUpdates(object sender,SizeChangedEventArgs e){
        // When the cards leave little space for the notes, scroll the whole page instead.
        bool compact=e.NewSize.Height<380;
        UpdatesContent.Height=compact?double.NaN:e.NewSize.Height;
        ReleaseScroll.VerticalScrollBarVisibility=compact?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
    }

    void PageChanged(object sender,SelectionChangedEventArgs e){
        if(e.OriginalSource!=Pages||ResetButton==null)return;
        if(ReaperTab.IsSelected)ReaperScriptAction(_=>{});
        ResetButton.Visibility=GeneralTab.IsSelected||AnnotationTab.IsSelected||ShortcutsTab.IsSelected||RecordingTab.IsSelected||ExcelTab.IsSelected?Visibility.Visible:Visibility.Collapsed;
        if(UpdatesTab.IsSelected && ReleaseVersions.ItemsSource==null){
            Desk.BindReleaseNotes(ReleaseVersions,ReleaseText,()=>{
                ReleaseScroll.ScrollToTop();
                UpdatesScroll.ScrollToTop();
            });
        }
        if(!GuideTab.IsSelected)return;
        GuideOverviewTitle.Text=Desk.GuideTitle;
        GuideOverviewText.Text=Desk.GuideSummary;
        GuideSections.ItemsSource=Desk.Guide(edited).Select(section=>new {
            Icon=(Geometry)FindResource("sc-"+section.Icon),section.Title,section.Topics
        }).ToArray();
    }

    void ReaperScriptAction(Action<ReaperIntegration> action){
        var scripts=desk.ReaperScripts;
        try{
            action(scripts);
            var folder=scripts.TargetFolder;
            var target=System.IO.Path.Combine(folder,ReaperIntegration.ActionName);
            var path=scripts.Installed??target;
            bool present=File.Exists(path)||File.Exists(path+".app");
            bool current=present && scripts.Current(path);
            ReaperStatus.Text=L.Text(current?"已安装，脚本为最新版本":present?"脚本需要更新或修复":"尚未安装脚本");
            ReaperInstalledPath.Text=present?path:"";
            ReaperInstalledPath.Visibility=present?Visibility.Visible:Visibility.Collapsed;
            ReaperUninstall.IsEnabled=present;
            bool same=string.Equals(target,path,StringComparison.OrdinalIgnoreCase);
            ReaperInstall.Content=L.Text(!present?"安装脚本":!same?"迁移到所选目录":"更新或修复脚本");
            ReaperFolder.Text=folder;
            ReaperAuto.IsEnabled=scripts.CustomFolder!=null;
            ReaperFolderMode.Text=L.Text(ReaperAuto.IsEnabled?"手动选择的目录":"自动识别的目录");
            ReaperError.Text="";
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){
            ReaperError.Text=ex.Message;
        }
    }
    void ChooseReaperFolder(object sender,RoutedEventArgs e)=>ReaperScriptAction(scripts=>{
        var picker=new Microsoft.Win32.OpenFolderDialog{Title=L.Text("选择 REAPER 脚本安装目录"),InitialDirectory=scripts.TargetFolder};
        if(picker.ShowDialog(this)==true)scripts.SetFolder(picker.FolderName);
    });
    void AutoReaperFolder(object sender,RoutedEventArgs e)=>ReaperScriptAction(scripts=>scripts.SetFolder(null));
    void InstallReaperScript(object sender,RoutedEventArgs e)=>ReaperScriptAction(scripts=>scripts.Install(scripts.TargetFolder));
    void UninstallReaperScript(object sender,RoutedEventArgs e)=>ReaperScriptAction(scripts=>
        scripts.Uninstall(scripts.Installed??System.IO.Path.Combine(scripts.TargetFolder,ReaperIntegration.ActionName)));

    Viewbox MakeIcon(string id,int size=16){
        var path=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("sc-"+id),StrokeThickness=1.7,
            StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
        path.SetBinding(Shape.StrokeProperty,new System.Windows.Data.Binding(){Path=new PropertyPath("(0)",System.Windows.Documents.TextElement.ForegroundProperty),RelativeSource=System.Windows.Data.RelativeSource.Self});
        var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(path);
        return new Viewbox{Width=size,Height=size,Child=canvas,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
    }
    Button IconButton(string icon,string label,Action click){
        var button=new Button{Style=(Style)FindResource("SettingsIconButton"),Content=MakeIcon(icon,15),ToolTip=label};
        AutomationProperties.SetName(button,label);button.Click+=(_,_)=>click();return button;
    }
    void RefreshRows(){
        foreach(var a in Shortcuts.Actions){
            var layout=new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(.95,GridUnitType.Star)});
            var name=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            name.Children.Add(new TextBlock{Text=a.Name,VerticalAlignment=VerticalAlignment.Center});
            var icon=MakeIcon(a.Id=="zoomVideo"?"zoomTimeline":a.Id,15);icon.Margin=new Thickness(7,0,0,0);icon.Opacity=.78;name.Children.Add(icon);layout.Children.Add(name);
            var keys=edited[a.Id];
            var badge=new Grid{MinWidth=86,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)};
            var outline=new Rectangle{RadiusX=5,RadiusY=5,StrokeThickness=1,StrokeDashArray=[3,2]};
            outline.SetResourceReference(Shape.StrokeProperty,keys.Length==0?"border":"link");
            if(keys.Length>0)outline.Opacity=.62;
            badge.Children.Add(outline);
            var label=new DockPanel{Margin=new Thickness(9,3,0,3)};
            label.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,keys.Length==0?"muted":"link");
            bool keyboard=a.Kind==InputKind.Keyboard;
            var device=MakeIcon(keyboard?"keyboard":"mouse",15);
            // The mouse outline starts three viewBox units farther right than the keyboard.
            if(!keyboard)device.RenderTransform=new TranslateTransform(-3d*device.Width/24,0);
            label.Children.Add(device);
            // Balance the gap to the visible icon stroke, including the whitespace inside its view box.
            var keyPadding=keyboard?new Thickness(7,0,8,0):new Thickness(5.5,0,9.5,0);
            label.Children.Add(new TextBlock{Text=keys.Length==0?L.Text("未设置"):keys.Label(a.Kind),
                Padding=keyPadding,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap});
            badge.Children.Add(label);Grid.SetColumn(badge,1);layout.Children.Add(badge);rows[a.Id].Content=layout;
            AutomationProperties.SetHelpText(rows[a.Id],keys.Length==0?L.Text("未设置"):keys.Label(a.Kind));
        }
        FilterShortcuts();
    }
    void FilterShortcuts(){
        bool byKey=FindByKey.IsChecked==true;
        string query=ShortcutSearch.Text.Trim();
        var visibleGroups=new HashSet<string>();
        foreach(var a in Shortcuts.Actions){
            bool match=byKey
                ? searchBinding is not {} binding || edited[a.Id].Any(k=>k==binding || binding.Vk==Shortcut.Wheel && a.Kind==InputKind.Wheel && k.Mods==binding.Mods)
                : a.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase);
            rows[a.Id].Visibility=match?Visibility.Visible:Visibility.Collapsed;
            if(match)visibleGroups.Add(a.Group);
        }
        foreach(var (group,header) in groupHeaders)header.Visibility=visibleGroups.Contains(group)?Visibility.Visible:Visibility.Collapsed;
        NoSearchResults.Visibility=visibleGroups.Count==0?Visibility.Visible:Visibility.Collapsed;
        bool empty=ShortcutSearch.Text.Length==0;
        SearchHint.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        ClearSearch.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
    }
    void SearchTextChanged(object sender,TextChangedEventArgs e){
        FilterShortcuts();ShortcutList.ScrollToTop();
    }
    void ChangeSearchMode(object sender,RoutedEventArgs e){
        bool byKey=FindByKey.IsChecked==true;
        ShortcutSearch.IsReadOnly=byKey;InputMethod.SetIsInputMethodEnabled(ShortcutSearch,!byKey);
        ContextMenuService.SetIsEnabled(ShortcutSearch,!byKey);
        SearchHint.Text=byKey?L.Text("请按下快捷键或使用鼠标手势"):L.Text("输入操作名称");
        ClearShortcutSearch(sender,e);
    }
    void ClearShortcutSearch(object sender,RoutedEventArgs e){
        searchBinding=null;ShortcutSearch.Clear();ShortcutSearch.Focus();
    }
    void ShowSearchBinding(Shortcut key){
        searchBinding=key;ShortcutSearch.Text=key.Label(InputKind.Keyboard);
    }
    internal bool FindGlobalShortcut(Shortcut key){
        if(!IsActive || FindByKey.IsChecked!=true || !ShortcutSearch.IsKeyboardFocused)return false;
        ShowSearchBinding(key);return true;
    }
    void SearchKeyDown(object sender,KeyEventArgs e){
        if(FindByKey.IsChecked!=true){
            if(e.Key==Key.Escape && ShortcutSearch.Text.Length>0){ShortcutSearch.Clear();e.Handled=true;}
            return;
        }
        if(e.Key==Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)return;
        e.Handled=true;
        if(e.IsRepeat)return;
        var key=Shortcut.From(e);
        if(key.Vk is 16 or 17 or 18 or 91 or 92)ShowSearchBinding(new(key.Mods,0));
        else ShowSearchBinding(WithHeldKey(key,searchBinding));
    }
    static Shortcut WithHeldKey(Shortcut key,Shortcut? previous){
        if(previous is {With:0,Vk:>=8 and <255} first && first.Mods==key.Mods && first.Vk!=key.Vk){
            var held=KeyInterop.KeyFromVirtualKey(first.Vk);
            if(held!=Key.None && Keyboard.IsKeyDown(held))return Shortcut.Pair(first,key.Vk);
        }
        return key;
    }
    static int MouseVirtualKey(MouseButton button)=>button switch{
        MouseButton.Left=>1,MouseButton.Right=>2,MouseButton.Middle=>4,MouseButton.XButton1=>5,MouseButton.XButton2=>6,_=>0
    };
    void SearchMouseDown(object sender,MouseButtonEventArgs e){
        if(FindByKey.IsChecked!=true || Keyboard.Modifiers==ModifierKeys.None)return;
        ShortcutSearch.Focus();ShowSearchBinding(new((int)Keyboard.Modifiers,MouseVirtualKey(e.ChangedButton)));e.Handled=true;
    }
    void SearchWheel(object sender,MouseWheelEventArgs e){
        if(FindByKey.IsChecked!=true)return;
        ShortcutSearch.Focus();ShowSearchBinding(new((int)Keyboard.Modifiers,Shortcut.Wheel));e.Handled=true;
    }
    void Reset(IEnumerable<ShortcutAction> group){
        var restored=new ShortcutMap(edited);
        foreach(var a in group)restored[a.Id]=a.Defaults;
        if(Shortcuts.Conflict(restored) is {} conflict){Message(SaveError,conflict);return;}
        foreach(var a in group)edited[a.Id]=restored[a.Id];
        Message(SaveError,null);RefreshRows();
    }
    static void Message(TextBlock block,string? text){block.Text=text??"";block.Visibility=text==null?Visibility.Collapsed:Visibility.Visible;}
    void SetRecordingFields(OutputSettings options){
        RecordingFps.SelectedValue=options.Fps==30?"30":"60";RecordingQuality.SelectedValue=options.Quality;
        SaveCopy.IsChecked=options.SaveCopy;CopyDirectory.Text=options.CopyDirectory;
        HideWhileRecording.IsChecked=options.HideWhileRecording;
    }
    void SetExcelTemplate(ExcelTemplate template){
        ExcelShowSource.IsChecked=template.ShowSource;ExcelZip.IsChecked=template.Zip;
        foreach(CheckBox check in ExcelColumnChoices.Children)check.IsChecked=template.Columns.HasFlag((ExcelColumns)check.Tag);
    }
    ExcelTemplate ReadExcelTemplate(){
        ExcelColumns columns=0;
        foreach(CheckBox check in ExcelColumnChoices.Children)
            if(check.IsChecked==true)columns|=(ExcelColumns)check.Tag;
        if(columns==0){
            ExcelTab.IsSelected=true;
            ExcelColumnChoices.Children[0].Focus();
            throw new InvalidDataException(L.Text("请至少选择一列导出。"));
        }
        return new(columns,ExcelShowSource.IsChecked==true,ExcelZip.IsChecked==true);
    }
    void SetExportBar(ExportBarOptions bar){
        ExportBarEnabled.IsChecked=bar.Enabled;
        SetColorButton(ExportBarBackground,bar.Background);SetColorButton(ExportBarFill,bar.Fill);
    }
    static void SetColorButton(Button button,string hex){
        button.Tag=hex;
        var swatch=new Border{Width=15,Height=15,CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,6,0),
            Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),BorderThickness=new Thickness(1)};
        swatch.SetResourceReference(Border.BorderBrushProperty,"border");
        var content=new StackPanel{Orientation=Orientation.Horizontal};
        content.Children.Add(swatch);content.Children.Add(new TextBlock{Text=hex});button.Content=content;
        AutomationProperties.SetHelpText(button,hex);
    }
    sealed class ColorDialogOwner(IntPtr handle):System.Windows.Forms.IWin32Window { public IntPtr Handle=>handle; }
    string? PickColor(string initial){
        using var picker=new System.Windows.Forms.ColorDialog{FullOpen=true,Color=System.Drawing.ColorTranslator.FromHtml(initial)};
        return picker.ShowDialog(new ColorDialogOwner(new WindowInteropHelper(this).Handle))==System.Windows.Forms.DialogResult.OK
            ?$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}":null;
    }
    void ChooseBarColor(object sender,RoutedEventArgs e){
        var button=(Button)sender;
        if(PickColor((string)button.Tag) is {} color)SetColorButton(button,color);
    }
    void EditPreset(int index){
        bool add=index==presetColors.Count;
        if(PickColor(add?"#FFFFFF":presetColors[index]) is not {} picked)return;
        string color=picked.ToLowerInvariant();
        int existing=presetColors.IndexOf(color);
        if(existing>=0 && existing!=index){
            Message(SaveError,L.Text("预设中已有这个颜色。"));return;
        }
        if(add)presetColors.Add(color);else presetColors[index]=color;
        RefreshPresetRows();
    }
    void AddPresetColor(object sender,RoutedEventArgs e)=>EditPreset(presetColors.Count);
    void MovePreset(int index,int other){
        (presetColors[index],presetColors[other])=(presetColors[other],presetColors[index]);
        RefreshPresetRows();
    }
    void EnableRowDrag<T>(DockPanel row,List<T> items,int index,Action refresh){
        Point? start=null;
        row.Background=Brushes.Transparent;
        var grip=new Border{Width=20,Cursor=Cursors.SizeNS,Background=Brushes.Transparent,Child=MakeIcon("grip",13),ToolTip=L.Text("拖动排序")};
        row.Children.Add(grip);
        row.PreviewMouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(this);if(grip.IsMouseOver)grip.CaptureMouse();};
        row.PreviewMouseLeftButtonUp+=(_,_)=>{start=null;if(grip.IsMouseCaptured)grip.ReleaseMouseCapture();};
        row.PreviewMouseMove+=(_,e)=>{
            if(e.LeftButton!=MouseButtonState.Pressed){start=null;return;}
            if(start is not {} from)return;
            var delta=e.GetPosition(this)-from;
            if(Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance && Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance)return;
            start=null;Mouse.Capture(null);e.Handled=true;
            SettingsRowDrag.Run(row,index,e.GetPosition(row).Y-delta.Y,destination=>{
                T item=items[index];items.RemoveAt(index);items.Insert(destination,item);refresh();
            });
        };
    }
    void RefreshPresetRows(){
        Message(SaveError,null);
        PresetRows.Children.Clear();AddPreset.IsEnabled=presetColors.Count<10;
        for(int i=0;i<presetColors.Count;i++){
            int index=i;
            var row=new DockPanel{Margin=new Thickness(0,2,0,2)};
            EnableRowDrag(row,presetColors,index,RefreshPresetRows);
            void Command(string icon,string label,bool enabled,Action click){
                var button=IconButton(icon,label,click);button.IsEnabled=enabled;
                DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);
            }
            Command("trash",L.Text("删除颜色"),presetColors.Count>1,()=>{presetColors.RemoveAt(index);RefreshPresetRows();});
            Command("nextNote",L.Text("下移颜色"),i+1<presetColors.Count,()=>MovePreset(index,index+1));
            Command("prevNote",L.Text("上移颜色"),i>0,()=>MovePreset(index,index-1));
            var swatch=new Button{HorizontalAlignment=HorizontalAlignment.Left};
            SetColorButton(swatch,presetColors[i]);swatch.Click+=(_,_)=>EditPreset(index);
            AutomationProperties.SetName(swatch,L.Text("修改颜色")+" "+presetColors[i]);
            row.Children.Add(swatch);PresetRows.Children.Add(row);
        }
    }
    void OpacityWheel(object sender,MouseWheelEventArgs e){
        if(int.TryParse(DefaultOpacity.Text,out int value))
            DefaultOpacity.Text=Math.Clamp((long)value+5*Math.Sign(e.Delta),0,100).ToString();
        e.Handled=true;
    }
    void AddSeparator(object sender,RoutedEventArgs e){toolbar.Add(new(ToolbarEntry.Separator));RefreshToolbar();((FrameworkElement)ToolbarRows.Children[toolbar.Count-1]).BringIntoView();}
    void RefreshToolbar(){
        ToolbarRows.Children.Clear();
        for(int i=0;i<toolbar.Count;i++){
            int index=i;
            var entry=toolbar[i];
            var row=new DockPanel{Margin=new Thickness(0,2,0,2)};
            EnableRowDrag(row,toolbar,index,RefreshToolbar);
            void Command(string icon,string label,bool enabled,Action action){
                var button=IconButton(icon,label,action);button.IsEnabled=enabled;
                DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);
            }
            void Move(int other){(toolbar[index],toolbar[other])=(toolbar[other],toolbar[index]);RefreshToolbar();}
            Command("nextNote",L.Text("下移"),i<toolbar.Count-1,()=>Move(index+1));
            Command("prevNote",L.Text("上移"),i>0,()=>Move(index-1));
            if(entry.Id==ToolbarEntry.Separator){
                Command("trash",L.Text("删除分隔线"),true,()=>{toolbar.RemoveAt(index);RefreshToolbar();});
                row.Children.Add(new TextBlock{Text=L.Text("分隔线"),VerticalAlignment=VerticalAlignment.Center});
            }else{
                var wide=new CheckBox{Content=L.Text("双宽"),IsChecked=entry.Wide,Margin=new Thickness(5,0,12,0),VerticalAlignment=VerticalAlignment.Center};
                void SetWidth(object sender,RoutedEventArgs e)=>toolbar[index]=toolbar[index] with{Wide=wide.IsChecked==true};
                wide.Checked+=SetWidth;wide.Unchecked+=SetWidth;
                DockPanel.SetDock(wide,Dock.Right);row.Children.Add(wide);
                var visible=new CheckBox{Content=L.Text(ToolbarEntry.Names[entry.Id]),IsChecked=entry.Visible,VerticalAlignment=VerticalAlignment.Center};
                void SetVisible(object sender,RoutedEventArgs e)=>toolbar[index]=toolbar[index] with{Visible=visible.IsChecked==true};
                visible.Checked+=SetVisible;visible.Unchecked+=SetVisible;
                string icon=entry.Id switch{"point"=>"point","toolRuler"=>"ruler","voiceRecord"=>"mic",_=>"tool-"+entry.Id};
                var graphic=MakeIcon(icon,16);graphic.Margin=new Thickness(3,0,7,0);
                row.Children.Add(graphic);row.Children.Add(visible);
            }
            ToolbarRows.Children.Add(row);
        }
    }
    void ResetPage(object sender,RoutedEventArgs e){
        if(ExcelTab.IsSelected){SetExcelTemplate(ExcelTemplate.Defaults);Message(SaveError,null);return;}
        if(RecordingTab.IsSelected){SetRecordingFields(OutputSettings.Defaults);return;}
        if(ShortcutsTab.IsSelected){Reset(Shortcuts.Actions);return;}
        if(AnnotationTab.IsSelected){
            DefaultRulerUnit.SelectedValue=RulerScale.DefaultUnit;
            DefaultLensZoom.SelectedValue=Drawing.DefaultZoom;DefaultOpacity.Text="80";
            presetColors.Clear();presetColors.AddRange(Note.DefaultColors);RefreshPresetRows();
            toolbar.Clear();toolbar.AddRange(ToolbarEntry.Defaults());RefreshToolbar();return;
        }
        if(GeneralTab.IsSelected){
            InterfaceLanguage.SelectedValue=L.DefaultLanguage;
            ExportDirectory.Text=OutputSettings.Defaults.ExportDirectory;
            SetExportBar(ExportBarOptions.Defaults);
        }
    }
    void ChooseDirectory(object sender,RoutedEventArgs e){
        if(sender is not Button {Tag:TextBox field})return;
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title=System.Windows.Automation.AutomationProperties.GetName(field)};
        if(Directory.Exists(field.Text))dialog.InitialDirectory=field.Text;
        if(dialog.ShowDialog(this)==true)field.Text=dialog.FolderName;
    }
    void OpenDefaultApps(object sender,RoutedEventArgs e)=>desk.OpenDefaultApps();
    void OpenHome(object sender,RoutedEventArgs e)=>Desk.OpenProjectHome();
    void OpenFeedbackTarget(object sender,RoutedEventArgs e){
        try{
            Message(SaveError,null);
            if(sender==IssueButton)Desk.Browse(Updates.NewIssue);
            else Desk.Browse(Directory.CreateDirectory(RuntimeLog.DirectoryPath).FullName);
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException){
            Serilog.Log.Error(ex,"Could not open feedback destination");
            Message(SaveError,sender==IssueButton?L.Text("无法打开 GitHub Issues，请检查默认浏览器。"):L.Text("无法打开日志目录，请检查目录权限。"));
        }
    }
    async void CheckVersion(object sender,RoutedEventArgs e){
        CheckButton.IsEnabled=false;CheckButton.Content=L.Text("正在检查…");
        try{await desk.CheckUpdates(true,this);}
        finally{CheckButton.Content=L.Text("检查更新");CheckButton.IsEnabled=true;}
    }
    void CancelSettings(object sender,RoutedEventArgs e)=>Close();
    void SaveSettings(object sender,RoutedEventArgs e){
        try{
            bool copy=SaveCopy.IsChecked==true;
            var output=new OutputSettings(Equals(RecordingFps.SelectedValue,"30")?30:60,(string)RecordingQuality.SelectedValue,copy,
                copy?OutputSettings.DirectoryPath(CopyDirectory.Text):CopyDirectory.Text,OutputSettings.DirectoryPath(ExportDirectory.Text),
                new(ExportBarEnabled.IsChecked==true,(string)ExportBarBackground.Tag,(string)ExportBarFill.Tag),ReadExcelTemplate(),HideWhileRecording.IsChecked==true);
            if(!int.TryParse(DefaultOpacity.Text,out int opacity) || opacity is <0 or >100)throw new InvalidDataException(L.Text("不透明度须为 0 到 100 之间的整数。"));
            save(initialPreferences with{Bindings=edited,RulerUnit=(string)DefaultRulerUnit.SelectedValue,LensZoom=(double)DefaultLensZoom.SelectedValue,
                    PresetColors=[..presetColors],DefaultOpacity=opacity,Toolbar=[..toolbar]},
                AutomaticUpdates.IsChecked==true,(string)InterfaceLanguage.SelectedValue,output);DialogResult=true;
        }catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException){Message(SaveError,ex.Message);}
    }

    void OpenEditor(ShortcutAction a){
        action=a;working.Clear();working.AddRange(edited[a.Id]);editIndex=-1;draft=null;
        string type=a.Kind switch{InputKind.Keyboard=>L.Text("按键"),InputKind.Modifier=>L.Text("修饰键"),_=>L.Text("鼠标")};
        EditorTitle.Text=L.Format($"「{a.Name}」的{type}配置");
        SettingsPage.IsEnabled=false;Overlay.Visibility=Visibility.Visible;
        if(working.Count==0)StartBinding(0);
        else{RenderEditor();FocusBinding(((DockPanel)BindingRows.Children[0]).Children.OfType<Button>().Last());}
    }
    void EndEditor(){
        if(action==null)return;
        var row=rows[action.Id];action=null;editIndex=-1;
        Overlay.Visibility=Visibility.Collapsed;SettingsPage.IsEnabled=true;
        if(row.IsVisible)row.Focus();else ShortcutSearch.Focus();
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
            var command=active?IconButton("check",L.Text("确认此绑定"),()=>{
                if(CommitDraft()){RenderEditor();AddBinding.Focus();}
            }):IconButton("trash",L.Text("删除 ")+working[i].Label(action.Kind),()=>Remove(index));
            command.IsEnabled=active||!Recording;
            command.SetResourceReference(ForegroundProperty,active?"link":"danger");
            if(active)accept=command;
            DockPanel.SetDock(command,Dock.Right);row.Children.Add(command);
            var key=new Button{HorizontalAlignment=HorizontalAlignment.Left,MaxWidth=310,IsEnabled=active||!Recording};
            if(active){
                capture=key;key.SetResourceReference(BorderBrushProperty,"link");
                AutomationProperties.SetAutomationId(key,"recordBinding");
                key.PreviewMouseDown+=(_,e)=>{
                    if(action?.Kind is not (InputKind.Click or InputKind.Drag) || Keyboard.Modifiers==ModifierKeys.None)return;
                    draft=new((int)Keyboard.Modifiers,MouseVirtualKey(e.ChangedButton));e.Handled=true;RenderEditor();FocusBinding(capture!);
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
            int[] choices=action.Kind==InputKind.Wheel?[Shortcut.Wheel,Shortcut.ReverseWheel]:modifierOnly?[]:[1,2,4,5,6];
            foreach(int vk in choices)MouseChoice.Items.Add(new ComboBoxItem{Content=new Shortcut(0,vk).Label(action.Kind),Tag=vk});
            MouseChoice.SelectedIndex=draft is {} saved?Array.IndexOf(choices,saved.Vk):action.Kind==InputKind.Wheel?0:-1;
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
        var current=action!;
        if(Shortcuts.Problem(current,key) is {} invalid)return invalid;
        if(working.Where((_,i)=>i!=editIndex).Any(k=>Shortcuts.Overlap(current,k,current,key)))return L.Text("这个操作已使用此快捷键。");
        var owner=Shortcuts.Actions.FirstOrDefault(a=>a!=current && edited[a.Id].Any(other=>Shortcuts.Overlap(a,other,current,key)));
        return owner==null?null:L.Format($"此快捷键已用于「{owner.Name}」，请换一个。");
    }
    void UpdateDraft(){
        string? error=Recording && draft is {} key?Problem(key):null;
        Message(EditorError,error);
        bool valid=draft!=null && error==null;
        if(capture!=null){
            capture.Content=draft?.Label(action!.Kind) is {Length:>0} text?text:action!.Id=="rateReset"?L.Text("按一个键，或同时按两个键"):action.Kind==InputKind.Keyboard?L.Text("请按下按键"):L.Text("请选择组合");
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
            draft=action.Id=="rateReset"?WithHeldKey(key,draft):key;
            UpdateDraft();
        }else if(e.Key==Key.Escape && !MouseChoice.IsDropDownOpen){e.Handled=true;EndEditor();}
    }
}
