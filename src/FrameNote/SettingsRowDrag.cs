using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameNote;

internal static class SettingsRowDrag
{
    sealed class InsertionLine(StackPanel panel) : Adorner(panel)
    {
        internal double Top {get;set;}
        protected override void OnRender(DrawingContext context){
            var pen=new Pen((Brush)panel.FindResource("accent"),2);
            context.DrawLine(pen,new Point(0,Top),new Point(panel.ActualWidth,Top));
        }
    }

    static void Slide(UIElement row,double from,double to,bool animate){
        var transform=row.RenderTransform as TranslateTransform;
        if(transform==null)row.RenderTransform=transform=new TranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty,null);
        transform.Y=to;
        if(animate&&SystemParameters.ClientAreaAnimation&&Math.Abs(from-to)>.1)
            transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(130)){
                EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }

    internal static void Run(DockPanel row,int source,double grabY,Action<int> commit){
        var panel=(StackPanel)row.Parent;
        var rows=panel.Children.Cast<DockPanel>().ToList();
        var slots=rows.Select(LayoutInformation.GetLayoutSlot).ToArray();
        double height=slots[source].Height;
        var layer=AdornerLayer.GetAdornerLayer(panel);
        var marker=new InsertionLine(panel){IsHitTestVisible=false,Top=slots[source].Top+1};
        var scroll=FindScroll(panel);
        int dest=source;
        bool dropped=false;
        // Hit test the original row midpoints; animated rows must never move their own hit thresholds.
        void Position(DragEventArgs e,bool autoScroll){
            if(autoScroll&&scroll!=null){
                double y=e.GetPosition(scroll).Y;
                if(y<22)scroll.ScrollToVerticalOffset(scroll.VerticalOffset-12);
                else if(y>scroll.ActualHeight-22)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+12);
            }
            double pointer=e.GetPosition(panel).Y;
            int next=0;
            for(int i=0;i<slots.Length;i++)
                if(i!=source&&pointer>slots[i].Top+slots[i].Height/2)next++;
            double offset=Math.Clamp(pointer-grabY-slots[source].Top-row.Margin.Top,-slots[source].Top,panel.ActualHeight-slots[source].Bottom);
            Slide(row,0,offset,false);
            if(dest!=next){
                dest=next;
                for(int i=0;i<rows.Count;i++){
                    if(i==source)continue;
                    double to=i>source&&i<=dest?-height:i>=dest&&i<source?height:0;
                    if(rows[i].RenderTransform is TranslateTransform t&&Equals(t.GetAnimationBaseValue(TranslateTransform.YProperty),to))continue;
                    Slide(rows[i],rows[i].RenderTransform.Value.OffsetY,to,true);
                }
            }
            marker.Top=(dest>source?slots[dest].Bottom-height:slots[dest].Top)+1;
            marker.Visibility=Visibility.Visible;marker.InvalidateVisual();
            e.Effects=DragDropEffects.Move;e.Handled=true;
        }
        void Over(object sender,DragEventArgs e)=>Position(e,true);
        void Drop(object sender,DragEventArgs e){Position(e,false);dropped=true;}
        void Leave(object sender,DragEventArgs e){
            if(!new Rect(panel.RenderSize).Contains(e.GetPosition(panel)))marker.Visibility=Visibility.Hidden;
        }
        panel.AllowDrop=true;panel.Background=Brushes.Transparent;
        panel.PreviewDragOver+=Over;panel.PreviewDrop+=Drop;panel.PreviewDragLeave+=Leave;
        Panel.SetZIndex(row,1);row.SetResourceReference(Panel.BackgroundProperty,"card");row.Opacity=.86;
        layer?.Add(marker);
        try{DragDrop.DoDragDrop(row,new DataObject("FrameNote.SettingsRow",source),DragDropEffects.Move);}
        finally{
            panel.PreviewDragOver-=Over;panel.PreviewDrop-=Drop;panel.PreviewDragLeave-=Leave;
            panel.ClearValue(UIElement.AllowDropProperty);panel.ClearValue(Panel.BackgroundProperty);
            layer?.Remove(marker);row.Background=Brushes.Transparent;row.Opacity=1;Panel.SetZIndex(row,0);
            var positions=rows.ToDictionary(r=>r,r=>r.TranslatePoint(new Point(),panel).Y);
            if(dropped&&source!=dest){
                rows.RemoveAt(source);rows.Insert(dest,row);commit(dest);panel.UpdateLayout();
            }
            for(int i=0;i<rows.Count;i++){
                var current=(DockPanel)panel.Children[i];
                double top=LayoutInformation.GetLayoutSlot(current).Top+current.Margin.Top;
                Slide(current,positions[rows[i]]-top,0,true);
            }
        }
    }
    static ScrollViewer? FindScroll(DependencyObject? node){
        while(node!=null&&node is not ScrollViewer)node=VisualTreeHelper.GetParent(node);
        return node as ScrollViewer;
    }
}
