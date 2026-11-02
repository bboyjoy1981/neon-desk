using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace NeonDesk;

// Circular volume gauges use live disk usage. Sweep/grid are decorative, not measured positions.
public sealed class RadarView : FrameworkElement
{
    public static readonly DependencyProperty DrivesProperty=DependencyProperty.Register(nameof(Drives),typeof(IEnumerable),typeof(RadarView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MotionProperty=DependencyProperty.Register(nameof(Motion),typeof(bool),typeof(RadarView),new PropertyMetadata(true,MotionChanged));
    private static readonly DependencyProperty PhaseProperty=DependencyProperty.Register("Phase",typeof(double),typeof(RadarView),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable? Drives {get=>(IEnumerable?)GetValue(DrivesProperty);set=>SetValue(DrivesProperty,value);}
    public bool Motion {get=>(bool)GetValue(MotionProperty);set=>SetValue(MotionProperty,value);}
    private Window? _owner;
    public RadarView()
    {
        IsHitTestVisible=false;
        Loaded+=(_,_)=>{_owner=Window.GetWindow(this);if(_owner!=null)_owner.StateChanged+=OwnerChanged;Animate();};
        Unloaded+=(_,_)=>{if(_owner!=null)_owner.StateChanged-=OwnerChanged;_owner=null;BeginAnimation(PhaseProperty,null);};
        IsVisibleChanged+=(_,_)=>Animate();
    }
    private void OwnerChanged(object? sender,EventArgs e)=>Animate();
    private static void MotionChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)=>((RadarView)d).Animate();
    private void Animate()=>BeginAnimation(PhaseProperty,IsLoaded&&IsVisible&&Motion&&SystemParameters.ClientAreaAnimation&&_owner?.WindowState!=WindowState.Minimized?new DoubleAnimation(0,360,TimeSpan.FromSeconds(10)){RepeatBehavior=RepeatBehavior.Forever}:null);
    private static SolidColorBrush Brush(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
    private static readonly Brush Red=Brush("#FF657A"),Warm=Brush("#FFAC99"),Rose=Brush("#D485AF"),GridInk=Brush("#703044"),Dim=Brush("#391D2C"),TextInk=Brush("#F3CAD7");
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(ActualWidth<40||ActualHeight<40)return;
        double w=ActualWidth,h=ActualHeight,r=Math.Max(15,Math.Min(w*.43,(h-37)*.48));var c=new Point(w/2,(h-26)/2);
        dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
        var thin=new Pen(GridInk,.6);
        for(int i=-4;i<=4;i++){double o=i*r/4;dc.DrawLine(thin,new(c.X-r,c.Y+o),new(c.X+r,c.Y+o));dc.DrawLine(thin,new(c.X+o,c.Y-r),new(c.X+o,c.Y+r));}
        for(int i=1;i<=4;i++)dc.DrawEllipse(null,thin,c,r*i/4,r*i/4);
        for(int i=0;i<60;i++){double a=i*Math.PI/30;double inner=r*(i%5==0?1.02:1.07);dc.DrawLine(new Pen(i%5==0?Red:GridInk,i%5==0?1.3:.7),new(c.X+Math.Cos(a)*inner,c.Y+Math.Sin(a)*inner),new(c.X+Math.Cos(a)*r*1.12,c.Y+Math.Sin(a)*r*1.12));}
        double phase=(double)GetValue(PhaseProperty)*Math.PI/180;
        if(Motion){dc.DrawLine(new Pen(Brush("#A8435B"),1),c,new(c.X+Math.Cos(phase)*r,c.Y+Math.Sin(phase)*r));}
        var drives=Drives?.OfType<DriveMetric>().Take(3).ToArray()??[];Brush[] colors=[Red,Warm,Rose];
        for(int i=0;i<drives.Length;i++)
        {
            double radius=r*(.82-i*.18),fraction=Math.Clamp(drives[i].UsedPercent/100,0,.99999);
            dc.DrawEllipse(null,new Pen(Dim,5),c,radius,radius);
            if(fraction>0){var path=new StreamGeometry();using(var g=path.Open()){g.BeginFigure(new(c.X,c.Y-radius),false,false);double a=fraction*Math.PI*2-Math.PI/2;g.ArcTo(new(c.X+Math.Cos(a)*radius,c.Y+Math.Sin(a)*radius),new(radius,radius),0,fraction>.5,SweepDirection.Clockwise,true,false);}path.Freeze();dc.DrawGeometry(null,new Pen(colors[i],5),path);}
            Label(dc,$"{drives[i].Name} {drives[i].UsedPercent:0}%",w*(i+.5)/Math.Max(1,drives.Length),h-13,colors[i],11);
        }
        dc.DrawEllipse(Red,null,c,3,3);
        if(drives.Length==0)Label(dc,"等待磁盘数据",w/2,h-13,TextInk,11);
        dc.Pop();
    }
    private void Label(DrawingContext dc,string value,double x,double y,Brush ink,double size)
    {
        var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new(x-text.Width/2,y-text.Height/2));
    }
}
