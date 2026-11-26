using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FrameNote;

internal partial class ExportDialog : Window
{
    readonly string format;
    readonly Func<CancellationToken,Task<FileSystemInfo?>> write;
    readonly CancellationToken lifetime;
    CancellationTokenSource? attempt;
    readonly bool closeOnSuccess;
    bool starting=true,failed,committing;
    internal FileSystemInfo? Output { get; private set; }
    internal bool Cancelled { get; private set; }
    internal bool IsRunning(CancellationToken token)=>attempt?.Token==token && !token.IsCancellationRequested;

    internal ExportDialog(Window owner,string format,Func<CancellationToken,Task<FileSystemInfo?>> write,CancellationToken lifetime,Shortcut[] cancelKeys,bool closeOnSuccess=false){
        InitializeComponent();Owner=owner;this.format=format;this.write=write;this.lifetime=lifetime;
        this.closeOnSuccess=closeOnSuccess;
        CopyExport.Content=L.Text("复制");
        DeskTheme.Dialog(this);
        ShowProgress();
        ContentRendered+=async (_,_)=>{if(starting){starting=false;await RunAttempt();}};
        PreviewKeyDown+=(_,e)=>{
            if((attempt==null && e.Key==Key.Escape)||cancelKeys.Contains(Shortcut.From(e))){e.Handled=true;Close();}
        };
        Closing+=(_,e)=>{
            if(attempt!=null){e.Cancel=true;Cancel();}
            else if(starting)Cancelled=true;
        };
    }
    void SetStatusIcon(string geometry,string color){
        Glyph.Data=Geometry.Parse(geometry);
        Glyph.SetResourceReference(Shape.StrokeProperty,color);
        Badge.SetResourceReference(Shape.FillProperty,color);
    }
    void ShowProgress(){
        Title=L.Format($"正在导出 {format}");Message.Text=L.Text("正在准备导出…");
        SetStatusIcon("M12 19V5 M5 12l7-7 7 7","link");
        Bar.Value=0;Percent.Text="0%";Meter.Visibility=Visibility.Visible;
        Primary.Visibility=CopyExport.Visibility=Visibility.Collapsed;Dismiss.IsEnabled=true;Dismiss.Content=L.Text("取消导出");
        Grid.SetColumn(Actions,1);Grid.SetColumnSpan(Actions,1);
    }
    internal void Report(string text,int percent){
        if(attempt==null || attempt.IsCancellationRequested)return;
        Message.Text=text;Bar.Value=Math.Clamp(percent,(int)Bar.Value,99);Percent.Text=$"{Bar.Value:0}%";
    }
    internal void Cancel(){
        if(attempt==null || attempt.IsCancellationRequested || committing)return;
        Message.Text=L.Text("正在取消导出…");Dismiss.IsEnabled=false;attempt.Cancel();
    }
    internal void BeginCommit(){committing=true;Dismiss.IsEnabled=false;}
    async Task RunAttempt(){
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        attempt=cancel;failed=false;committing=false;Output=null;
        try{Output=await write(cancel.Token);}
        catch(OperationCanceledException) when(cancel.IsCancellationRequested){Cancelled=true;}
        catch(Exception e){failed=true;Message.Text=e.Message;Serilog.Log.Error(e,"Failed to export {Format}",format);}
        finally{attempt=null;}
        if(Cancelled||lifetime.IsCancellationRequested||closeOnSuccess && !failed){Close();return;}
        Title=failed?L.Text("导出失败"):L.Text("导出完成");
        if(!failed){
            Bar.Value=100;Percent.Text="100%";
            Message.Text=Output==null?L.Text("带批注的 GIF 已复制到粘贴板。"):L.Text("是否打开文件所在目录？");
        }
        SetStatusIcon(failed?"M18 6 6 18 M6 6l12 12":"M20 6 9 17l-5-5",failed?"danger":"link");
        Meter.Visibility=Visibility.Collapsed;
        Grid.SetColumn(Actions,0);Grid.SetColumnSpan(Actions,2);
        Primary.Content=failed?L.Text("重试"):L.Text("打开目录");
        Primary.Visibility=failed||Output!=null?Visibility.Visible:Visibility.Collapsed;
        CopyExport.Visibility=Output!=null?Visibility.Visible:Visibility.Collapsed;
        Dismiss.IsEnabled=true;Dismiss.Content=L.Text("关闭");Dismiss.Focus();
    }
    async void PrimaryClick(object sender,RoutedEventArgs e){
        if(failed){ShowProgress();await RunAttempt();}
        else if(Output!=null)DialogResult=true;
    }
    void CopyClick(object sender,RoutedEventArgs e){
        if(Output is not {} output)return;
        try{
            output.Refresh();
            if(!output.Exists){Message.Text=L.Text("找不到导出文件，可能已被移动或删除。");return;}
            Clipboard.SetFileDropList(new StringCollection{output.FullName});
            Message.Text=L.Text("已复制，可以粘贴到其他位置。");
        }catch(Exception ex) when(ex is ExternalException or IOException or UnauthorizedAccessException){
            Serilog.Log.Warning(ex,"Could not copy the export to the clipboard");
            Message.Text=L.Text("无法复制到剪贴板，请重试。");
        }
    }
    void DismissClick(object sender,RoutedEventArgs e)=>Close();
}
