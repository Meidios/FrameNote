using System.IO;
using ClosedXML.Excel;

namespace FrameNote;

internal static class ExcelReview
{
    internal static readonly string WorkbookName=L.Text("批注清单.xlsx");
    internal static readonly (ExcelColumns Field,string Label,double Width)[] Columns=[
        (ExcelColumns.Number,"编号",9),(ExcelColumns.Type,"类型",12),(ExcelColumns.Start,"开始时间",17),
        (ExcelColumns.End,"结束时间",17),(ExcelColumns.Text,"批注内容",52),
        (ExcelColumns.Preview,"画面预览",27),(ExcelColumns.Media,"媒体文件",17)];
    internal static string MediaFile(Note note,bool withAudio)=>withAudio?$"media/{(note.EndUs.HasValue?"Region":"Mark")}-{note.Number}.mp4":note.EndUs.HasValue?$"media/Region-{note.Number}.gif":$"media/Mark-{note.Number}.png";
    internal static string AudioFile(Note n)=>$"audio/{n.Number}"+(n.Voice!.Original is {} file?Path.GetExtension(file.Name).ToLowerInvariant():".mp3");
    // ClosedXML consumes one leading apostrophe as Excel's text-entry prefix.
    static string Literal(string value)=>value.StartsWith('\'')?"'"+value:value;

    internal static void Write(string directory,string previews,string source,IReadOnlyList<Note> notes,ExcelTemplate template,IReadOnlySet<Guid> withAudio,CancellationToken token){
        using var book=new XLWorkbook();
        var sheet=book.AddWorksheet(L.Text("批注清单"));
        sheet.ShowGridLines=false;
        sheet.Style.Font.FontName="Microsoft YaHei";
        sheet.Style.Font.FontSize=10;
        var columns=Columns.Where(c=>template.Columns.HasFlag(c.Field)).ToArray();
        if(notes.Any(n=>n.Voice!=null))columns=[..columns,(ExcelColumns.AudioAttachment,"音频附件",30)];
        int header=template.ShowSource?2:1;
        if(template.ShowSource){
            var title=sheet.Range(1,1,1,columns.Length);
            if(columns.Length>1)title.Merge();
            title.FirstCell().Value=Literal(source);
            title.Style.Font.Bold=true;title.Style.Font.FontSize=14;
            title.Style.Font.FontColor=XLColor.White;
            title.Style.Fill.BackgroundColor=XLColor.FromHtml("#253449");
            sheet.Row(1).Height=32;
        }
        for(int i=0;i<columns.Length;i++){
            sheet.Cell(header,i+1).Value=L.Text(columns[i].Label);sheet.Column(i+1).Width=columns[i].Width;
        }
        var heading=sheet.Range(header,1,header,columns.Length);
        heading.Style.Fill.BackgroundColor=XLColor.FromHtml("#DEE7F1");
        heading.Style.Font.Bold=true;heading.Style.Font.FontColor=XLColor.FromHtml("#24384A");
        sheet.Row(header).Height=24;
        sheet.SheetView.FreezeRows(header);
        sheet.Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
        for(int i=0;i<notes.Count;i++){
            token.ThrowIfCancellationRequested();
            var note=notes[i];int row=header+i+1;
            int lines=template.Columns.HasFlag(ExcelColumns.Text)?note.Text.Split('\n').Sum(line=>Math.Max(1,(line.Length+23)/24)):1;
            sheet.Row(row).Height=Math.Clamp(16*lines,template.Columns.HasFlag(ExcelColumns.Preview)?74:22,409);
            for(int col=0;col<columns.Length;col++){
                var cell=sheet.Cell(row,col+1);
                switch(columns[col].Field){
                    case ExcelColumns.Number:
                        cell.Value=note.Number;cell.Style.NumberFormat.Format="\"#\"0";
                        var color=XLColor.FromHtml(note.DisplayColor());
                        cell.Style.Fill.BackgroundColor=color;
                        var rgb=color.Color;
                        cell.Style.Font.FontColor=DeskTheme.UseWhiteInk(System.Windows.Media.Color.FromRgb(rgb.R,rgb.G,rgb.B))?XLColor.White:XLColor.Black;
                        cell.Style.Font.Bold=true;cell.Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
                        break;
                    case ExcelColumns.Type: cell.Value=note.EndUs.HasValue?"Region":"Mark";break;
                    case ExcelColumns.Start:
                    case ExcelColumns.End:
                        if((columns[col].Field==ExcelColumns.Start?note.StartUs:note.EndUs) is long time)
                            cell.Value=TimeSpan.FromTicks(time*10);
                        cell.Style.NumberFormat.Format="[h]:mm:ss.000";break;
                    case ExcelColumns.Text: cell.Value=Literal(note.Text);cell.Style.Alignment.WrapText=true;break;
                    case ExcelColumns.Preview:
                        var picture=sheet.AddPicture(Path.Combine(previews,$"{note.Number}.png"),$"Note_{note.Number}");
                        picture.MoveTo(cell,6,5,cell,6+picture.OriginalWidth,5+picture.OriginalHeight);break;
                    case ExcelColumns.AudioAttachment:
                        if(note.Voice==null)break;
                        cell.Value=Literal(note.Voice.Original?.Name??L.Text("播放语音 MP3"));
                        cell.SetHyperlink(new XLHyperlink(new Uri(AudioFile(note),UriKind.Relative)));
                        cell.Style.Font.FontColor=XLColor.FromHtml("#145A88");cell.Style.Font.Underline=XLFontUnderlineValues.Single;break;
                    case ExcelColumns.Media:
                        cell.Value=withAudio.Contains(note.Id)?L.Text("播放 MP4"):note.EndUs.HasValue?L.Text("播放 GIF"):L.Text("打开 PNG");
                        cell.SetHyperlink(new XLHyperlink(new Uri(MediaFile(note,withAudio.Contains(note.Id)),UriKind.Relative)));
                        cell.Style.Font.FontColor=XLColor.FromHtml("#145A88");cell.Style.Font.Underline=XLFontUnderlineValues.Single;break;
                }
            }
        }
        sheet.Range(header,1,notes.Count+header,columns.Length).SetAutoFilter();
        token.ThrowIfCancellationRequested();
        book.SaveAs(Path.Combine(directory,WorkbookName));
    }
}
