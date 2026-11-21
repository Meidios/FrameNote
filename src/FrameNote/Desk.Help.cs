using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace FrameNote;

internal sealed class InfoDialog : Window
{
    readonly StackPanel body=new();
    internal readonly WrapPanel Actions=new(){Margin=new Thickness(0,14,0,0)};
    internal readonly TextBlock Message=new(){TextWrapping=TextWrapping.Wrap,FontSize=14,LineHeight=23};
    internal InfoDialog(Window owner,string title,string heading,string text){
        Owner=owner;Title=title+" · FrameNote";WindowStartupLocation=WindowStartupLocation.CenterOwner;Width=520;Height=385;MinWidth=395;MinHeight=280;
        DeskTheme.Window(this);FontSize=14;
        var panel=new DockPanel{Margin=new Thickness(22)};Content=panel;
        var h=new TextBlock{Text=heading,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,LineHeight=27,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14),TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(h,Dock.Top);panel.Children.Add(h);
        DockPanel.SetDock(Actions,Dock.Bottom);panel.Children.Add(Actions);
        panel.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Message.Text=text;body.Children.Add(Message);
    }
    internal void Insert(UIElement control)=>body.Children.Insert(0,control);
    internal Button Action(string label,Action click){
        var button=new Button{Content=label,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,7,0)};
        button.Click+=(_,_)=>click();Actions.Children.Add(button);return button;
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
    bool updating,startedUpdates;
    static void Browse(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true})?.Dispose();
    static void Releases()=>Browse(Updates.Home+"/releases");
    void InitHelp(bool directPlayer){
        prefs=new(root);
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..16];
        session=new Mutex(false,@"Local\FrameNote.Startup."+key,out bool first);
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"unins000.exe")))installGuard=new Mutex(false,@"Local\FrameNote.Installed");
        var bar=menuStrip=new Menu();
        var file=new MenuItem{Header="文件(_F)"};bar.Items.Add(file);
        var open=new MenuItem{Header="打开文件…(_O)"};
        bool CanOpen()=>!pending && !configuring && (native?.IsVisible==true ? native.OpenButton.IsEnabled : reviewCanOpen);
        file.SubmenuOpened+=(_,_)=>open.IsEnabled=CanOpen();
        open.Click+=async (_,_)=>{if(CanOpen())await PickVideo();};
        file.Items.Add(open);
        var settings=new MenuItem{Header="设置"};settings.Click+=(_,_)=>OpenSettings();file.Items.Add(settings);
        var exit=new MenuItem{Header="退出(_X)"};exit.Click+=(_,_)=>Close();file.Items.Add(exit);
        var menu=new MenuItem{Header="帮助(_H)"};bar.Items.Add(menu);
        MenuItem Item(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);return item;}
        string HelpAction(string id,string name)=>shortcuts[id].Length==0?name:name+"（"+shortcuts[id].Label(InputKind.Keyboard)+"）";
        Item("使用帮助",()=>Simple("使用帮助","FrameNote 视频审阅",
            $"打开或拖入视频，定位需要反馈的画面。选工具绘图、选图或录音，就能在播放头新建批注。也可使用「{HelpAction("point","点批注")}」添加文字，或使用{HelpAction("inBtn","进点")}和{HelpAction("outBtn","出点")}标记区间。右键批注可编辑、换色、复制或删除。\n\n"+
            $"在「文件 → 设置」里可为同一个操作添加多组快捷键，也可以设置画框、画箭头和输入文字的鼠标手势。编辑时可以{HelpAction("undo","撤销")}或{HelpAction("redo","重做")}。\n\n"+
            "关闭窗口前，点「导出审阅包」保留原片、批注和剪辑。双击 .framenote 可重新审阅，点 REAPER 图标可发送至当前工程。在 VLC 播放界面点「进入批注」即可逐帧审阅。"));
        Item("检查更新…",async()=>await CheckUpdates(true));
        autoItem=Item("启动时检查更新",()=>prefs.Change(p=>p with{Automatic=autoItem.IsChecked}));
        autoItem.IsCheckable=true;menu.SubmenuOpened+=(_,_)=>autoItem.IsChecked=prefs.Change(p=>p).Automatic;
        Item("更新记录",()=>History());
        Item("关于 FrameNote",()=>Simple("关于 FrameNote","FrameNote "+Updates.Running,"视频审阅、录屏和 REAPER 批注交付工具。\n\n作者：Meidios（MEI）\nWindows x64\n\n如果这个工具对你有帮助，欢迎到 GitHub 点个 Star。",true));
        var host=new DockPanel();DockPanel.SetDock(bar,Dock.Top);host.Children.Add(bar);InitPlaylist(host);host.Children.Add(surfaces);Content=host;
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
    void Simple(string title,string heading,string message,bool home=false){
        var dialog=new InfoDialog(this,title,heading,message);
        if(home)dialog.Action("GitHub 主页",()=>Browse(Updates.Home));dialog.Action("关闭",dialog.Close);dialog.ShowDialog();
    }
    void History(Version? from=null){
        var current=Updates.Running;var dir=Path.Combine(AppContext.BaseDirectory,"releases");
        var files=Directory.Exists(dir)?Directory.GetFiles(dir,"v*.md").Where(f=>Version.TryParse(Path.GetFileNameWithoutExtension(f)[1..],out _)).OrderByDescending(f=>Version.Parse(Path.GetFileNameWithoutExtension(f)[1..])).ToArray():[];
        var dialog=new InfoDialog(this,"更新记录",from==null?"更新记录":$"已从 {from} 更新到 {current}","此版本未提供更新说明");
        var box=new ComboBox{Height=24,Margin=new Thickness(0,0,0,12),ItemsSource=files.Select(Path.GetFileNameWithoutExtension)};
        box.SelectionChanged+=(_,_)=>{if(box.SelectedIndex>=0)dialog.Message.Text=File.ReadAllText(files[box.SelectedIndex]);};box.SelectedItem="v"+current;dialog.Insert(box);dialog.Action("关闭",dialog.Close);dialog.ShowDialog();
    }
    async Task CheckUpdates(bool manual){
        if(updating)return;updating=true;
        try{
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var release=await updates.Latest(timeout.Token);
            if(lifetime.IsCancellationRequested)return;
            if(release==null || release.Version<=Updates.Running){
                if(manual)Simple("检查更新","当前版本 "+Updates.Running,release==null?"官方渠道暂无可用正式版。":"没有比当前版本更新的正式版。");return;
            }
            if(!manual && (!prefs.Change(p=>p).Automatic || prefs.Change(p=>p).Skip==release.Version.ToString()))return;
            if(!manual){while((pending || recorder!=null || OwnedWindows.Count>0) && !lifetime.IsCancellationRequested)await Task.Delay(400,lifetime.Token);}
            var dlg=new InfoDialog(this,"发现新版本",$"FrameNote {Updates.Running} → {release.Version}",release.Notes+"\n\n"+(release.Setup==null?"此版本没有可校验的 Windows x64 安装包。":"安装前需要关闭 FrameNote。需要保留的批注、剪辑或临时录屏，请先导出审阅包。"));
            bool download=false;
            if(release.Setup!=null)dlg.Action("下载并更新",()=>{download=true;dlg.Close();});
            dlg.Action("稍后",dlg.Close);dlg.Action("跳过此版本",()=>{prefs.Change(p=>p with{Skip=release.Version.ToString()});dlg.Close();});
            dlg.Action("打开发布页",()=>Browse(release.Page.AbsoluteUri));dlg.ShowDialog();
            if(download)await InstallRelease(release);
        }catch(Exception e){if(manual && !lifetime.IsCancellationRequested)UpdateProblem("未能完成更新检查",e.Message);}
        finally{updating=false;}
    }
    void UpdateProblem(string heading,string message){
        var dlg=new InfoDialog(this,"检查更新",heading,message+"\n\n可以稍后重新检查，或前往官方发布页。");
        dlg.Action("打开发布页",Releases);dlg.Action("关闭",dlg.Close);dlg.ShowDialog();
    }
    async Task InstallRelease(UpdateRelease release){
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancel.CancelAfter(TimeSpan.FromMinutes(30));
        var dlg=new InfoDialog(this,"下载更新","正在下载 "+release.Version,"0%");
        var progress=new ProgressBar{Minimum=0,Maximum=100,Height=8,Margin=new Thickness(0,6,0,12)};dlg.Insert(progress);
        bool completed=false, userCancelled=false;dlg.Closing+=(_,_)=>{if(!completed){userCancelled=true;cancel.Cancel();}};dlg.Action("取消下载",dlg.Close);
        IsEnabled=false;dlg.Show();
        string? installer=null;
        try{
            installer=await updates.Download(release,Path.Combine(root,"Updates"),new Progress<int>(p=>{progress.Value=p;dlg.Message.Text=$"{p}%";}),cancel.Token);
        }catch(Exception e) when(!lifetime.IsCancellationRequested){
            completed=true;dlg.Close();IsEnabled=true;
            if(!userCancelled)UpdateProblem("未安装更新",e is OperationCanceledException?"下载超时，请重试。":e.Message);
        }finally{completed=true;if(dlg.IsVisible)dlg.Close();IsEnabled=true;}
        if(installer==null || lifetime.IsCancellationRequested)return;
        try{
            bool editingNow=webReady && await web.ExecuteScriptAsync("!!editing")=="true";
            if(pending || configuring || recorder!=null || activeRecording!=null || editingNow){
                Simple("安装更新","请先完成当前工作","请先结束录制或编辑，导出需要保留的审阅包。若正在审阅临时录屏，请切换到普通文件，再从帮助菜单检查更新。");return;
            }
            bool run=false;var confirm=new InfoDialog(this,"安装更新","安装包已通过校验","现在关闭 FrameNote 并打开安装程序？取消安装会保留当前版本。");
            confirm.Action("关闭并安装",()=>{run=true;confirm.Close();});confirm.Action("稍后",confirm.Close);confirm.ShowDialog();
            if(!run)return;
            using var child=Process.Start(new ProcessStartInfo(installer){UseShellExecute=true})??throw new IOException("无法启动安装程序。");
            installer=null;Close();
        }finally{if(installer!=null)File.Delete(installer);}
    }
}
