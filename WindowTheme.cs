using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace NeonDesk;
internal static class WindowTheme
{
    internal static void Apply(Window window)
    {
        window.Background=new SolidColorBrush(Color.FromRgb(16,11,16));
        window.Foreground=new SolidColorBrush(Color.FromRgb(245,231,235));
        window.FontFamily=new FontFamily("Microsoft YaHei UI");
        window.SourceInitialized+=(_,_)=>
        {
            try{int dark=1;DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,20,ref dark,sizeof(int));}
            catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
        };
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}


