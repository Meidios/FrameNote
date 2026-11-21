using System.IO;
using ClosedXML.Excel;

namespace FrameNote;

internal static class ExcelReview
{
    internal const string WorkbookName="批注清单.xlsx";
    internal static string MediaFile(Note note)=>note.EndUs.HasValue?$"media/Region-{note.Number}.gif":$"media/Mark-{note.Number}.png";
    // ClosedXML consumes one leading apostrophe as Excel's text-entry prefix.
    static string Literal(string value)=>value.StartsWith('\'')?"'"+value:value;

    internal static void Write(string directory,string previews,string source,IReadOnlyList<Note> notes,CancellationToken token){
        using var book=new XLWorkbook();
        var sheet=book.AddWorksheet("批注清单");
        sheet.ShowGridLines=false;
        sheet.Style.Font.FontName="Microsoft YaHei";
        sheet.Style.Font.FontSize=10;
        var title=sheet.Range("A1:G1").Merge();
        title.FirstCell().Value=Literal(source);
        title.Style.Font.Bold=true;title.Style.Font.FontSize=14;
        title.Style.Font.FontColor=XLColor.White;
        title.Style.Fill.BackgroundColor=XLColor.FromHtml("#253449");
        sheet.Row(1).Height=32;
        string[] headers=["编号","类型","开始时间","结束时间","批注内容","画面预览","媒体文件"];
        double[] widths=[9,12,17,17,52,27,17];
        for(int i=0;i<headers.Length;i++){
            sheet.Cell(2,i+1).Value=headers[i];sheet.Column(i+1).Width=widths[i];
        }
        var heading=sheet.Range("A2:G2");
        heading.Style.Fill.BackgroundColor=XLColor.FromHtml("#DEE7F1");
        heading.Style.Font.Bold=true;heading.Style.Font.FontColor=XLColor.FromHtml("#24384A");
        sheet.Row(2).Height=24;
        sheet.SheetView.FreezeRows(2);
        sheet.Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
        for(int i=0;i<notes.Count;i++){
            token.ThrowIfCancellationRequested();
            var note=notes[i];int row=i+3;bool region=note.EndUs.HasValue;
            var badge=sheet.Cell(row,1);
            badge.Value=note.Number;badge.Style.NumberFormat.Format="\"#\"0";
            var color=XLColor.FromHtml(note.DisplayColor());
            badge.Style.Fill.BackgroundColor=color;
            var rgb=color.Color;
            badge.Style.Font.FontColor=299*rgb.R+587*rgb.G+114*rgb.B<126000?XLColor.White:XLColor.Black;
            badge.Style.Font.Bold=true;badge.Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
            sheet.Cell(row,2).Value=region?"Region":"Mark";
            sheet.Cell(row,3).Value=TimeSpan.FromTicks(note.StartUs*10);
            if(note.EndUs is long end)sheet.Cell(row,4).Value=TimeSpan.FromTicks(end*10);
            sheet.Range(row,3,row,4).Style.NumberFormat.Format="[h]:mm:ss.000";
            var body=sheet.Cell(row,5);
            body.Value=Literal(note.Text);body.Style.Alignment.WrapText=true;
            string relative=MediaFile(note);
            var link=sheet.Cell(row,7);
            link.Value=region?"播放 GIF":"打开 PNG";
            link.SetHyperlink(new XLHyperlink(new Uri(relative,UriKind.Relative)));
            link.Style.Font.FontColor=XLColor.FromHtml("#145A88");link.Style.Font.Underline=XLFontUnderlineValues.Single;
            sheet.Row(row).Height=Math.Clamp(16*note.Text.Split('\n').Sum(line=>Math.Max(1,(line.Length+23)/24)),74,409);
            var picture=sheet.AddPicture(Path.Combine(previews,$"{note.Number}.png"),$"Note_{note.Number}");
            var anchor=sheet.Cell(row,6);
            picture.MoveTo(anchor,6,5,anchor,6+picture.OriginalWidth,5+picture.OriginalHeight);
        }
        sheet.Range(2,1,notes.Count+2,7).SetAutoFilter();
        token.ThrowIfCancellationRequested();
        book.SaveAs(Path.Combine(directory,WorkbookName));
    }
}
