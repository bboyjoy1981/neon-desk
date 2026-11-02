using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NeonDesk;
public sealed class HudPanel:Border
{
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;if(w<=1||h<=1)return;
        double cut=10;var path=new StreamGeometry();using(var g=path.Open()){g.BeginFigure(new(.5,.5),true,true);g.LineTo(new(w-cut,.5),true,false);g.LineTo(new(w-.5,cut),true,false);g.LineTo(new(w-.5,h-.5),true,false);g.LineTo(new(cut,h-.5),true,false);g.LineTo(new(.5,h-cut),true,false);}path.Freeze();
        dc.DrawGeometry(Background,new Pen(BorderBrush,1),path);
        var ink=new SolidColorBrush(Color.FromRgb(255,101,122));ink.Freeze();
        dc.DrawLine(new Pen(ink,2),new(14,.5),new(Math.Min(64,w-20),.5));
        dc.DrawLine(new Pen(ink,1),new(w-25,h-.5),new(w-8,h-.5));
    }
}
