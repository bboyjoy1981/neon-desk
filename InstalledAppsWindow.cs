using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace NeonDesk;
internal sealed class InstalledAppsWindow:Window
{
    private readonly UsageObserver _usage;private readonly CancellationTokenSource _cancel=new();private readonly DataGrid _grid=new(){IsReadOnly=true,AutoGenerateColumns=false};private readonly TextBox _search=new(){MinWidth=260,Padding=new Thickness(10),Margin=new Thickness(0,10,0,10)};private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};private List<AppEntry> _rows=[];
    internal InstalledAppsWindow(UsageObserver usage)
    {
        _usage=usage;Title="软件管理 · 登记大小与本机观察记录";Width=1160;Height=720;MinWidth=850;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowTheme.Apply(this);
        var root=new Grid{Margin=new Thickness(20)};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel();header.Children.Add(new TextBlock{Text="已安装软件",FontSize=24,FontWeight=FontWeights.Bold});header.Children.Add(new TextBlock{Text="登记大小由安装程序提供，可能缺失或不准确。最近观察时间只覆盖清理台运行期间，不代表完整的历史使用记录。",TextWrapping=TextWrapping.Wrap});header.Children.Add(_search);header.Children.Add(_status);root.Children.Add(header);
        foreach(var (title,path,width) in new[]{("软件名称","Name",270),("登记大小","Size",110),("最近观察到运行","Usage",180),("版本","Version",140),("发布者","Publisher",180),("类型","Kind",130)})_grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new System.Windows.Data.Binding(path),Width=width});Grid.SetRow(_grid,1);root.Children.Add(_grid);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};var refresh=new Button{Content="刷新软件列表"};var uninstall=new Button{Content="前往 Windows 卸载",IsEnabled=false};var location=new Button{Content="打开安装位置",IsEnabled=false};actions.Children.Add(refresh);actions.Children.Add(uninstall);actions.Children.Add(location);Grid.SetRow(actions,2);root.Children.Add(actions);Content=root;
        _search.TextChanged+=(_,_)=>Filter();_grid.SelectionChanged+=(_,_)=>{uninstall.IsEnabled=_grid.SelectedItem is AppEntry;location.IsEnabled=_grid.SelectedItem is AppEntry a&&Directory.Exists(a.Location);};
        uninstall.Click+=(_,_)=>{try{if(_grid.SelectedItem is AppEntry a){Clipboard.SetText(a.Name);Process.Start(new ProcessStartInfo("ms-settings:appsfeatures"){UseShellExecute=true});_status.Text="已复制软件名称。可在 Windows 已安装应用中搜索并卸载；请按系统提示确认。";}}catch(Exception ex){_status.Text="无法打开系统卸载："+ex.Message;}};
        location.Click+=(_,_)=>{try{if(_grid.SelectedItem is AppEntry a&&Directory.Exists(a.Location))Process.Start(new ProcessStartInfo("explorer.exe","\""+a.Location+"\""){UseShellExecute=true});}catch(Exception ex){_status.Text=ex.Message;}};
        async Task Load(){refresh.IsEnabled=false;_status.Text="正在读取本机软件登记信息…";try{var result=await Task.Run(()=>Read(_cancel.Token));if(_cancel.IsCancellationRequested)return;_rows=result.Rows;Filter();_status.Text=$"共 {result.Rows.Count} 项。{result.Note} 在搜索框输入名称或发布者筛选。";}catch(OperationCanceledException){}catch(Exception ex){_status.Text="读取失败："+ex.Message;}finally{refresh.IsEnabled=true;}}
        Loaded+=async(_,_)=>await Load();refresh.Click+=async(_,_)=>await Load();Closed+=(_,_)=>_cancel.Cancel();
    }
    private void Filter(){var q=_search.Text.Trim();_grid.ItemsSource=_rows.Where(x=>q.Length==0||x.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Publisher.Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();}
    private (List<AppEntry> Rows,string Note) Read(CancellationToken token)
    {
        var rows=new List<AppEntry>();int skipped=0;
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try{using var basis=RegistryKey.OpenBaseKey(hive,view);using var uninstall=basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");if(uninstall==null)continue;foreach(var id in uninstall.GetSubKeyNames()){token.ThrowIfCancellationRequested();try{using var key=uninstall.OpenSubKey(id);string Get(string n)=>key?.GetValue(n)?.ToString()??"";var name=Get("DisplayName");if(name.Length==0||Get("SystemComponent")=="1")continue;long.TryParse(Get("EstimatedSize"),out var kb);var root=Get("InstallLocation");rows.Add(new(name,Get("DisplayVersion"),Get("Publisher"),root,kb>0?kb*1024:0,"桌面软件",_usage.LastSeen(root,Get("DisplayIcon"))));}catch(UnauthorizedAccessException){skipped++;}catch(IOException){skipped++;}}}catch(UnauthorizedAccessException){skipped++;}catch(IOException){skipped++;}
        }
        string note="";
        try
        {
            var info=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8};foreach(var a in new[]{"-NoProfile","-NonInteractive","-Command","[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage } | Select-Object Name,Version,Publisher,InstallLocation | ConvertTo-Json -Compress"})info.ArgumentList.Add(a);
            using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync(token);var errors=p.StandardError.ReadToEndAsync(token);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));try{p.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();}catch{if(!p.HasExited)p.Kill(true);throw;}
            var json=output.GetAwaiter().GetResult();if(p.ExitCode!=0)throw new IOException();if(!string.IsNullOrWhiteSpace(json)){using var doc=JsonDocument.Parse(json);var packages=doc.RootElement.ValueKind==JsonValueKind.Array?doc.RootElement.EnumerateArray().ToArray():new[]{doc.RootElement};foreach(var a in packages){string Get(string n)=>a.TryGetProperty(n,out var v)?v.GetString()??"":"";var root=Get("InstallLocation");rows.Add(new(Get("Name"),Get("Version"),Get("Publisher"),root,0,"打包应用",_usage.LastSeen(root,"")));}}
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){note="打包应用读取超时，仅展示已读取项目。";}catch(OperationCanceledException){throw;}catch{note="打包应用读取不可用，仅展示已读取项目。";}
        if(skipped>0)note+=$" {skipped} 项登记信息不可访问。";
        return(rows.DistinctBy(x=>(x.Name,x.Version,x.Location)).OrderByDescending(x=>x.Bytes).ThenBy(x=>x.Name).ToList(),note);
    }
    private sealed record AppEntry(string Name,string Version,string Publisher,string Location,long Bytes,string Kind,DateTimeOffset? Seen)
    {
        public string Size=>Bytes>0?Format.Bytes(Bytes):"未提供";
        public string Usage=>Seen is {} t?$"{t.LocalDateTime:yyyy-MM-dd HH:mm}（观察）":"未知 / 尚无观察记录";
    }
}
