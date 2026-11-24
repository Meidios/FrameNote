using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace FrameNote;

internal sealed class InfoDialog : Window
{
    readonly StackPanel body=new();
    readonly WrapPanel actions=new(){HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,0)};
    internal readonly TextBlock Message=new(){TextWrapping=TextWrapping.Wrap,FontSize=14,LineHeight=23};
    internal InfoDialog(Window owner,string title,string heading,string text){
        Owner=owner;Title=title;
        Width=520;Height=385;MinWidth=395;MinHeight=280;
        var panel=new DockPanel{Margin=new Thickness(24,4,24,20)};Content=panel;
        DeskTheme.Dialog(this);
        DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
        if(heading!=title){
            var headingBlock=new TextBlock{Text=heading,FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,12)};
            DockPanel.SetDock(headingBlock,Dock.Top);panel.Children.Add(headingBlock);
        }
        Message.Text=text;body.Children.Add(Message);
        panel.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    internal void Insert(UIElement control)=>body.Children.Insert(0,control);
    internal Button Action(string label,Action click){
        var button=new Button{Content=label,MinWidth=88,MinHeight=36,Padding=new Thickness(12,5,12,5),Margin=new Thickness(6,3,6,3)};
        button.Click+=(_,_)=>click();actions.Children.Add(button);return button;
    }
}
public sealed partial class Desk
{
    readonly Updates updates=new();
    readonly CancellationTokenSource lifetime=new();
    UpdateSettings prefs=null!;
    Mutex? session,installGuard;
    MenuItem autoItem=null!;
    bool reviewCanOpen;
    bool manualCheck,startedUpdates;
    CancellationTokenSource? updateCheck;
    internal static readonly string ProductDescription=L.Text("视频审阅、录屏和 REAPER 批注交付工具。"),Author=L.Text("作者：Meidios（MEI）");
    internal static void Browse(string target)=>Process.Start(new ProcessStartInfo(target){UseShellExecute=true})?.Dispose();
    internal static void OpenProjectHome()=>Browse(Updates.Home);
    static void Releases()=>Browse(Updates.Home+"/releases");
    internal static string GuideTitle=>L.Text("把反馈标在具体画面上");
    internal static string GuideSummary=>L.Text("审阅视频或图片时，用 FrameNote 标出哪一帧、画面哪里需要修改，再配上文字、参考图或语音。同事打开审阅包就能跳到对应位置，也可以直接查看你导出的带批注视频或问题清单。");
    internal sealed record GuideTopic(string Label,string Text);

    internal static (string Icon,string Title,GuideTopic[] Topics)[] Guide(ShortcutMap bindings){
        string ActionName(string id,string name){
            var action=Shortcuts.Actions.First(a=>a.Id==id);
            return L.WithShortcut(L.Text(name),bindings[id].Label(action.Kind));
        }
        return [
            ("open",L.Text("打开素材"),[
                new(L.Text("视频与审阅包"),L.Text("在欢迎页点「打开文件」，选择视频、图片或 .framenote 审阅包。视频会进入批注界面。用 Player 看原片时，点「进入批注」可从当前播放位置开始审阅。")),
                new(L.Text("图片与播放列表"),L.Format($"在空窗口用{ActionName("paste","粘贴图片")}可以审阅剪贴板里的截图。查看图片时，滚轮缩放、放大后拖动画面；两侧箭头切换同文件夹的图片。打开多份素材后，点画面左上角的播放列表图标切换文件。")),
            ]),
            ("record",L.Text("录制画面和声音"),[
                new(L.Text("选择捕获来源"),L.Text("在左侧「视频捕获」选择显示器、窗口或框选区域，再选目标。只录局部时，点「框选区域」后拖出范围。需要让对方看见指针操作时，开启「捕获鼠标」。")),
                new(L.Text("设置录音与副本"),L.Text("在「音频捕获」添加要录的系统输出，按需选择麦克风，看电平、调节每路增益或静音。在「设置 → 录制设置」选择帧率和压缩品质，开启「保存副本」并指定目录即可留存原始录制。")),
                new(L.Text("开始与停止"),L.Text("点左上角的视频或 GIF 录制按钮开始，再点一次结束。录完的视频会进入批注界面。录制的 GIF 没有声音，最长 30 秒。")),
            ]),
            ("play",L.Text("播放与定位"),[
                new(L.Text("逐帧检查"),L.Format($"拖动时间线指针找到大致位置，再用{ActionName("prev","上一帧")}和{ActionName("next","下一帧")}微调。可切换播放倍速或全屏预览。要反复核对一段动作或声音，选中区间批注，再开启「区间循环」。")),
                new(L.Text("缩放与音量"),L.Format($"在视频画面上用{ActionName("zoomVideo","缩放视频画面")}查看细节。在扬声器图标上悬停可展开音量条，滚轮或拖动滑块调节音量，点击图标静音或恢复。")),
            ]),
            ("quickArrow",L.Text("在画面上添加批注"),[
                new(L.Text("文字与图形"),L.Format($"把播放头放到问题出现的时刻，直接选箭头、框选、线或文字框，在画面上拖出形状。只想写一句话时，用{ActionName("point","点批注")}，在右侧「内容」输入。右键画面空白处也能选工具。")),
                new(L.Text("快速手势"),L.Format($"画框、画箭头或放置文字框时，也可以直接在画面上使用鼠标手势。对应操作为{ActionName("quickBox","画框")}、{ActionName("quickArrow","画箭头")}和{ActionName("quickText","输入文字批注")}，可在「快捷键」中改绑。")),
                new(L.Text("修改与附图"),L.Text("选中已有图形后，拖中间可移动，拖端点或角点可改变长度、方向或大小。右键图形可编辑、复制、删除，或更换预设色与自定义色。工具栏的「添加图片」会新建批注，要补充到已有批注，请右键它选「添加附图」。")),
                new(L.Text("语音批注"),L.Text("先在左侧「音频捕获」启用要录的声音，再点批注工具栏的麦克风。视频会从播放头开始播放，再点同一按钮结束录音。回放时，语音会和原声一起播放。")),
            ]),
            ("ruler",L.Text("测量与放大细节"),[
                new(L.Text("距离标尺"),L.Text("点击起点和终点，或从起点拖到终点。要按真实尺寸测量，先画一段已知长度，右键标尺填写距离和单位并保存。同一帧新增的标尺会沿用该比例，默认单位可在「通用」设置中选择。")),
                new(L.Text("角度与放大镜"),L.Text("依次点击端点、顶点和另一端点，第三次点击固定角度。拖动控制点或在详情和右键菜单里输入角度可调整数值。用放大镜框住细节，再把放大预览拖到空白处，取样框和预览框可以分别移动。")),
            ]),
            ("point",L.Text("定位和整理批注"),[
                new(L.Text("点与区间"),L.Text("点批注（Mark）记录一个时刻。拖时间线标签边缘可延长为区间（Region），拖中间可整体移动。要精确对时，在详情中输入起点和时长（秒）。时长设为 0 可恢复为点。语音批注的长度由录音决定。")),
                new(L.Text("编号与文字"),L.Text("悬停在图形或编号上可预览批注。点击画面编号、时间线标签或右侧列表可选中对应批注。「按时间编号」按起点先后重编号码。卡片的复制按钮可复制纯文字，右键菜单的「复制一份」可复制整条批注。")),
            ]),
            ("splitClip",L.Text("剪辑时间线"),[
                new(L.Text("分割、裁剪与联动"),L.Format($"把播放头放到切点，用{ActionName("splitClip","分割片段")}切开片段。拖片段边缘裁剪，拖中间移动，选中后可删除。开启「轨道联动」可让视频和对应原声一起调整。联动裁剪暂时隐藏的批注，在恢复该段画面后会重新出现。")),
                new(L.Text("吸附与对齐"),L.Text("拖动片段或批注时，边缘可吸附到播放头。「贴到上一片段」和「贴到下一片段」将选中片段贴齐同轨邻段。首段可向左贴到零点，末段可向右贴到时间线末尾。")),
                new(L.Text("查看长时间线"),L.Format($"在时间线上用{ActionName("zoomTimeline","缩放时间线")}放大检查相邻帧，或用{ActionName("panTimeline","平移时间线")}查看其他时间段。拖动时想让片段停在两个吸附点之间，可以用{ActionName("noSnap","拖动时暂停吸附")}。")),
            ]),
            ("save",L.Text("保存审阅包与发送到 REAPER"),[
                new(L.Text("留到下次继续编辑"),L.Format($"关闭窗口会清理本次编辑。要保留成果，请先{ActionName("save","导出审阅包")}。下次打开 .framenote 就能恢复素材、剪辑、批注和附件。继续修改后，再导出一份最新的审阅包。")),
                new(L.Text("在 REAPER 中对时"),L.Text("如需把批注带入 REAPER，先打开已导出的审阅包。首次点击 REAPER 图标并选择脚本目录完成安装。在 REAPER 选轨并放好编辑光标，再点图标发送，也可直接将图标拖到编排区的目标位置。点批注对应 Marker，区间批注对应 Region。")),
            ]),
            ("copyFrame",L.Text("导出画面、视频与清单"),[
                new(L.Text("单帧与图片"),L.Text("在视频播放栏复制当前帧或另存为 PNG，可把当前可见的批注一起发出。审阅图片时，「复制批注图」会附上批注文字清单，「另存为 PNG」保留原图尺寸。")),
                new(L.Text("MP4 与 GIF"),L.Text("导出 MP4 可分享剪辑后的画面、批注和声音。底部进度条用批注颜色标出时间位置，可在「设置 → 通用」开关或改色。要发动图，悬停在 GIF 导出图标上选择复制或另存。")),
                new(L.Text("Excel 与导出目录"),L.Text("Excel 清单每条批注占一行。在「设置 → 表格设置」选择导出列，可嵌入画面预览或附上 PNG、GIF。分享时发送整个文件夹，或开启 ZIP 导出后发送压缩包。审阅包、MP4、GIF 和 Excel 的保存位置在「设置 → 通用 → 导出路径」。")),
            ]),
            ("settings",L.Text("设置与问题反馈"),[
                new(L.Text("自定义快捷键"),L.Format($"在「快捷键」页按操作名称搜索，或切换到按键查找并按下组合键。点操作可修改键盘快捷键或鼠标手势，点「确定」再「保存」。编辑时可用{ActionName("undo","撤销")}和{ActionName("redo","重做")}回退或重做修改。")),
                new(L.Text("语言、更新与反馈"),L.Text("在「通用」中切换界面语言，保存后重启生效。「更新」页可查看版本记录并检查更新。遇到问题时，在「问题反馈」打开日志目录，或前往 GitHub Issues 提交复现步骤。")),
            ]),
        ];
    }
    void InitHelp(bool directPlayer){
        prefs=new(root);
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..16];
        session=new Mutex(false,@"Local\FrameNote.Startup."+key,out bool first);
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"unins000.exe")))installGuard=new Mutex(false,@"Local\FrameNote.Installed");
        var host=new DockPanel();
        surfaces.SetResourceReference(Panel.BackgroundProperty,"bg");
        titleBar=new DeskTitleBar(this,host);
        titleBar.SettingsGear.Click+=(_,_)=>OpenSettings();
        titleBar.SetHistoryShortcuts(shortcuts);
        titleBar.HistoryButtons.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding(nameof(IsVisible)){Source=web});
        void RequestHistory(bool redo){
            if(pending||!web.IsVisible||doc==null)return;
            if(InputManager.Current.MostRecentInputDevice is MouseDevice)web.Focus();
            Send(new{type="moveHistory",documentId=doc.Id,redo});
        }
        titleBar.UndoButton.Click+=(_,_)=>RequestHistory(false);
        titleBar.RedoButton.Click+=(_,_)=>RequestHistory(true);
        var bar=titleBar.MainMenu;
        var file=new MenuItem{Header=L.Text("文件"),RenderTransform=new TranslateTransform(4,0)};bar.Items.Add(file);
        AccessKeyManager.Register("F",file);
        var open=new MenuItem{Header=L.Text("打开文件…(_O)")};
        bool CanOpen()=>!pending && !configuring && (native?.IsVisible==true ? native.OpenButton.IsEnabled : reviewCanOpen);
        file.SubmenuOpened+=(_,_)=>open.IsEnabled=CanOpen();
        open.Click+=async (_,_)=>{if(CanOpen())await PickVideo();};
        file.Items.Add(open);
        var settings=new MenuItem{Header=L.Text("设置")};settings.Click+=(_,_)=>OpenSettings();file.Items.Add(settings);
        var exit=new MenuItem{Header=L.Text("退出(_X)")};exit.Click+=(_,_)=>Close();file.Items.Add(exit);
        var menu=new MenuItem{Header=L.Text("帮助")};bar.Items.Add(menu);
        AccessKeyManager.Register("H",menu);
        MenuItem Item(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);return item;}
        Item(L.Text("使用帮助"),()=>Simple(L.Text("使用帮助"),GuideTitle,GuideSummary+"\n\n"+
            string.Join("\n\n",Guide(shortcuts).Select(section=>section.Title+"\n"+
                string.Join("\n",section.Topics.Select(topic=>topic.Label+"  "+topic.Text))))));
        Item(L.Text("检查更新…"),async()=>await CheckUpdates(true));
        autoItem=Item(L.Text("启动时检查更新"),()=>prefs.Change(p=>p with{Automatic=autoItem.IsChecked}));
        autoItem.IsCheckable=true;menu.SubmenuOpened+=(_,_)=>autoItem.IsChecked=prefs.Change(p=>p).Automatic;
        Item(L.Text("更新记录"),()=>History());
        Item(L.Text("关于 FrameNote"),()=>OpenSettings(showAbout:true));
        DockPanel.SetDock(titleBar,Dock.Top);host.Children.Add(titleBar);InitPlaylist(host);host.Children.Add(surfaces);Content=host;
        Closed+=(_,_)=>{lifetime.Cancel();session?.Dispose();installGuard?.Dispose();};
        async Task StartupUpdates(){
            if(startedUpdates||!first)return;startedUpdates=true;
            try{
                while(currentWork is {} w)await w.Task;
                await Task.Delay(350,lifetime.Token);
                var current=Updates.Running;
                var previous=prefs.Change(p=>p with{LastRun=current.ToString()});
                var from=UpdateSettings.Upgraded(previous.LastRun,File.Exists(Path.Combine(AppContext.BaseDirectory,"unins000.exe"))?Registry.CurrentUser.OpenSubKey(@"Software\Meidios\FrameNote")?.GetValue("PreviousVersion") as string:null,current);
                if(from!=null)History(from);
                if(previous.Automatic)await CheckUpdates(false);
            }catch(OperationCanceledException){}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        if(directPlayer)ContentRendered+=async (_,_)=>await StartupUpdates();
        else web.NavigationCompleted+=async (_,e)=>{if(e.IsSuccess)await StartupUpdates();};
    }
    void Simple(string title,string heading,string message,Window? owner=null){
        var dialog=new InfoDialog(owner??this,title,heading,message);
        dialog.Action(L.Text("关闭"),dialog.Close);dialog.ShowDialog();
    }
    internal sealed record ReleaseEntry(bool Bullet,string Text);
    static IEnumerable<ReleaseEntry> ReleaseEntries(string text){
        var paragraph=new List<string>();
        bool bullet=false;
        foreach(var raw in text.Split('\n').Append("")){
            var line=raw.Trim();
            if(line.StartsWith("# FrameNote ",StringComparison.OrdinalIgnoreCase))continue;
            line=System.Text.RegularExpressions.Regex.Replace(line,@"^(?:\*\*)?中文(?:\*\*)?(?:[：:]\s*|$)","");
            bool nextBullet=line.StartsWith("- ")||line.StartsWith("* ");
            if((nextBullet||line.Length==0)&&paragraph.Count>0){
                yield return new(bullet,string.Join(" ",paragraph));
                paragraph.Clear();bullet=false;
            }
            if(line.Length==0)continue;
            if(nextBullet){bullet=true;line=line[2..];}
            var plain=System.Text.RegularExpressions.Regex.Replace(line,@"\[([^\]]+)\]\(https?://[^)]+\)","$1");
            paragraph.Add(plain.Replace("**","").Replace("`",""));
        }
    }
    internal static void BindReleaseNotes(ComboBox versions,ItemsControl entries,Action? resetScroll=null){
        System.Windows.Automation.AutomationProperties.SetAutomationId(entries,"ReleaseText");
        void Display(string text){
            entries.ItemsSource=ReleaseEntries(text).ToArray();
            resetScroll?.Invoke();
        }
        var dir=Path.Combine(AppContext.BaseDirectory,"releases");
        string[] files;
        try{
            files=Directory.Exists(dir)?Directory.GetFiles(dir,"v*.md")
                .Where(f=>Version.TryParse(Path.GetFileNameWithoutExtension(f)[1..],out _))
                .OrderByDescending(f=>Version.Parse(Path.GetFileNameWithoutExtension(f)[1..])).ToArray():[];
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException){files=[];}
        var labels=files.Select(Path.GetFileNameWithoutExtension).ToArray();
        versions.ItemsSource=labels;
        versions.IsEnabled=files.Length>0;
        if(files.Length==0){Display(L.Text("暂无可用的更新记录。"));return;}
        versions.SelectionChanged+=(_,_)=>{
            if(versions.SelectedIndex<0)return;
            string text;
            try{text=L.ReleaseNotes(File.ReadAllText(files[versions.SelectedIndex]));}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){text=L.Text("无法读取此版本的更新记录。");}
            Display(text.Length==0?L.Text("此版本未提供更新说明。"):text);
        };
        versions.SelectedIndex=Math.Max(0,Array.IndexOf(labels,"v"+Updates.Running));
    }
    void History(Version? from=null){
        var dialog=new InfoDialog(this,L.Text("更新记录"),from==null?L.Text("更新记录"):L.Format($"已从 {from} 更新到 {Updates.Running}"),"");
        var box=new ComboBox{Height=24,Margin=new Thickness(0,0,0,12)};
        var entries=new ItemsControl{ItemTemplate=(DataTemplate)FindResource("ReleaseEntry")};
        dialog.Insert(entries);dialog.Message.Visibility=Visibility.Collapsed;
        BindReleaseNotes(box,entries);
        dialog.Insert(box);dialog.Action(L.Text("关闭"),dialog.Close);dialog.ShowDialog();
    }
    internal async Task CheckUpdates(bool manual,Window? owner=null){
        if(updateCheck is {IsCancellationRequested:false}){
            if(!manual||manualCheck)return;
            updateCheck.Cancel();
        }
        using var check=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        updateCheck=check;manualCheck=manual;
        void OwnerClosed(object? sender,EventArgs e)=>check.Cancel();
        if(owner!=null)owner.Closed+=OwnerClosed;
        try{
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(check.Token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var release=await updates.Latest(timeout.Token);
            if(check.IsCancellationRequested||owner is {IsVisible:false})return;
            if(release==null || release.Version<=Updates.Running){
                if(manual)Simple(L.Text("检查更新"),L.Text("当前版本 ")+Updates.Running,release==null?L.Text("官方渠道暂无可用正式版。"):L.Text("没有比当前版本更新的正式版。"),owner:owner);return;
            }
            if(!manual){
                while(pending || recorder!=null || OwnedWindows.Count>0)await Task.Delay(400,check.Token);
                var settings=prefs.Change(p=>p);
                if(!settings.Automatic || settings.Skip==release.Version.ToString())return;
            }
            var dlg=new InfoDialog(owner??this,L.Text("发现新版本"),$"FrameNote {Updates.Running} → {release.Version}",release.Notes+"\n\n"+(release.Setup==null?L.Text("此版本没有可校验的 Windows x64 安装包。"):L.Text("安装前需要关闭 FrameNote。需要保留的批注、剪辑或临时录屏，请先导出审阅包。")));
            bool download=false;
            if(release.Setup!=null)dlg.Action(L.Text("下载并更新"),()=>{download=true;dlg.Close();});
            dlg.Action(L.Text("稍后"),dlg.Close);dlg.Action(L.Text("跳过此版本"),()=>{prefs.Change(p=>p with{Skip=release.Version.ToString()});dlg.Close();});
            dlg.Action(L.Text("打开发布页"),()=>Browse(release.Page.AbsoluteUri));dlg.ShowDialog();
            if(download)await InstallRelease(release,owner??this);
        }catch(Exception e){if(manual && !check.IsCancellationRequested && owner is not {IsVisible:false})UpdateProblem(L.Text("未能完成更新检查"),e.Message,owner);}
        finally{
            if(owner!=null)owner.Closed-=OwnerClosed;
            if(updateCheck==check)updateCheck=null;
        }
    }
    void UpdateProblem(string heading,string message,Window? owner=null){
        var dlg=new InfoDialog(owner??this,L.Text("检查更新"),heading,message+L.Text("\n\n可以稍后重新检查，或前往官方发布页。"));
        dlg.Action(L.Text("打开发布页"),Releases);dlg.Action(L.Text("关闭"),dlg.Close);dlg.ShowDialog();
    }
    async Task InstallRelease(UpdateRelease release,Window owner){
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancel.CancelAfter(TimeSpan.FromMinutes(30));
        var dlg=new InfoDialog(owner,L.Text("下载更新"),L.Text("正在下载 ")+release.Version,"0%");
        var progress=new ProgressBar{Minimum=0,Maximum=100,Height=8,Margin=new Thickness(0,6,0,12)};dlg.Insert(progress);
        bool completed=false, userCancelled=false;dlg.Closing+=(_,_)=>{if(!completed){userCancelled=true;cancel.Cancel();}};dlg.Action(L.Text("取消下载"),dlg.Close);
        owner.IsEnabled=false;dlg.Show();
        string? installer=null;
        try{
            installer=await updates.Download(release,Path.Combine(root,"Updates"),new Progress<int>(p=>{progress.Value=p;dlg.Message.Text=$"{p}%";}),cancel.Token);
        }catch(Exception e) when(!lifetime.IsCancellationRequested){
            completed=true;dlg.Close();owner.IsEnabled=true;
            if(!userCancelled)UpdateProblem(L.Text("未安装更新"),e is OperationCanceledException?L.Text("下载超时，请重试。"):e.Message,owner);
        }finally{completed=true;if(dlg.IsVisible)dlg.Close();owner.IsEnabled=true;}
        if(installer==null || lifetime.IsCancellationRequested)return;
        try{
            bool editingNow=webReady && await web.ExecuteScriptAsync("!!editing")=="true";
            if(pending || configuring || recorder!=null || activeRecording!=null || editingNow){
                Simple(L.Text("安装更新"),L.Text("请先完成当前工作"),L.Text("请先结束录制或编辑，导出需要保留的审阅包。若正在审阅临时录屏，请切换到普通文件，再从帮助菜单检查更新。"),owner:owner);return;
            }
            bool run=false;var confirm=new InfoDialog(owner,L.Text("安装更新"),L.Text("安装包已通过校验"),L.Text("现在关闭 FrameNote 并打开安装程序？取消安装会保留当前版本。"));
            confirm.Action(L.Text("关闭并安装"),()=>{run=true;confirm.Close();});confirm.Action(L.Text("稍后"),confirm.Close);confirm.ShowDialog();
            if(!run)return;
            using var child=Process.Start(new ProcessStartInfo(installer){UseShellExecute=true})??throw new IOException(L.Text("无法启动安装程序。"));
            installer=null;Close();
        }finally{if(installer!=null)File.Delete(installer);}
    }
}
