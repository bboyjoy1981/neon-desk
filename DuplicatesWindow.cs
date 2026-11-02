using System.IO;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace NeonDesk;

public sealed class DuplicatesWindow:Window
{
    private CancellationTokenSource _cancel=new();
    public DuplicatesWindow()
    {
        Title="重复文件分析 · 内容校验 · 不执行删除";Width=1000;Height=650;
        Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(8,15,28));
        WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowTheme.Apply(this);
        var root=new Grid{Margin=new Thickness(20)};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var bar=new StackPanel();var choose=new Button{Content="选择文件夹并分析"};
        var cancel=new Button{Content="取消分析",IsEnabled=false,Margin=new Thickness(0,8,0,0)};
        var status=new TextBlock{Text="对所选目录中 ≥1 MB 的同体积文件计算 SHA-256。相同内容不代表可以删除。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};
        bar.Children.Add(choose);bar.Children.Add(cancel);bar.Children.Add(status);root.Children.Add(bar);
        cancel.Click+=(_,_)=>{_cancel.Cancel();cancel.IsEnabled=false;status.Text="正在取消…";};
        var grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,EnableRowVirtualization=true,EnableColumnVirtualization=true};Grid.SetRow(grid,1);
        foreach(var (label,path) in new[]{("重复组","Group"),("大小","Size"),("路径","Path")})grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new System.Windows.Data.Binding(path){Mode=System.Windows.Data.BindingMode.OneWay},Width=path=="Path"?new DataGridLength(1,DataGridLengthUnitType.Star):120});
        var open=new Button{Content="打开选中文件位置",Height=44,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0),IsEnabled=false};Grid.SetRow(open,2);root.Children.Add(open);
        grid.SelectionChanged+=(_,_)=>open.IsEnabled=grid.SelectedItem is Row;
        open.Click+=(_,_)=>{if(grid.SelectedItem is Row row&&File.Exists(row.Path))try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+row.Path+"\""){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}};
        root.Children.Add(grid);Content=root;Closed+=(_,_)=>_cancel.Cancel();
        choose.Click+=async(_,_)=>
        {
            var dialog=new OpenFolderDialog{Title="选择要分析的文件夹"};if(dialog.ShowDialog(this)!=true)return;
            _cancel.Dispose();_cancel=new();choose.IsEnabled=false;cancel.IsEnabled=true;grid.ItemsSource=null;
            try
            {
                var progress=new Progress<string>(message=>status.Text=message);
                var rows=await Task.Run(()=>Analyze(dialog.FolderName,progress,_cancel.Token));
                if(!_cancel.IsCancellationRequested){grid.ItemsSource=rows;status.Text=rows.Count==0?"分析完成：没有发现 ≥1 MB 的相同内容文件。链接、占用或变化中的文件会跳过。":$"分析完成：{rows.Select(x=>x.Group).Distinct().Count()} 个重复组，{rows.Count} 个文件。相同组号代表内容相同；选中一行可打开位置。不会自动删除。";}
            }
            catch(OperationCanceledException){status.Text="分析已取消，未删除任何文件。可重新选择文件夹。";}
            catch(Exception ex){status.Text="分析未完成："+ex.Message;}
            finally{choose.IsEnabled=true;cancel.IsEnabled=false;}
        };
    }
    private static List<Row> Analyze(string root,IProgress<string> progress,CancellationToken token)
    {
        var sizes=new Dictionary<long,List<string>>();var pending=new Stack<string>();pending.Push(root);int count=0;
        while(pending.TryPop(out var dir))
        {
            token.ThrowIfCancellationRequested();if(!Safety.SafePath(dir,root))continue;
            try
            {
                foreach(var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if((entry.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                        if((entry.Attributes&FileAttributes.Directory)!=0){pending.Push(entry.FullName);continue;}
                        if(++count>300000)throw new InvalidOperationException("文件超过30万项，请选择更小的目录。");
                        if(count%500==0)progress.Report($"阶段 1/2 · 正在整理文件大小 · 已检查 {count:N0} 个文件");
                        var file=(FileInfo)entry;if(file.Length<1048576)continue;
                        if(!sizes.TryGetValue(file.Length,out var paths))sizes[file.Length]=paths=[];
                        paths.Add(file.FullName);
                    }
                    catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        var result=new List<Row>();int group=0,hashed=0;
        int candidates=sizes.Where(x=>x.Value.Count>1).Sum(x=>x.Value.Count);
        foreach(var size in sizes.Where(x=>x.Value.Count>1))
        {
            var hashes=new Dictionary<string,List<string>>();
            foreach(var path in size.Value)
            {
                token.ThrowIfCancellationRequested();progress.Report($"阶段 2/2 · 内容校验 {++hashed:N0}/{candidates:N0} · {System.IO.Path.GetFileName(path)}");
                try
                {
                    if(!Safety.SafePath(path,root))continue;
                    using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                    if(stream.Length!=size.Key)continue;
                    using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[1024*1024];int read;
                    while((read=stream.Read(buffer))>0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);}
                    string key=Convert.ToHexString(hash.GetHashAndReset());if(!hashes.TryGetValue(key,out var matches))hashes[key]=matches=[];matches.Add(path);
                }
                catch(IOException){}catch(UnauthorizedAccessException){}
            }
            foreach(var matches in hashes.Values.Where(x=>x.Count>1)){group++;result.AddRange(matches.Select(path=>new Row(group,Format.Bytes(size.Key),path)));}
        }
        return result;
    }
    private sealed record Row(int Group,string Size,string Path);
}

