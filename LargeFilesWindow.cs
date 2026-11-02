using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
namespace NeonDesk;

public sealed class LargeFilesWindow:Window
{
    private readonly CancellationTokenSource _cancel=new();
    private readonly DataGrid _grid=new(){IsReadOnly=true,AutoGenerateColumns=false};
    public LargeFilesWindow()
    {
        Title="大文件雷达 · 只分析，不自动删除";Width=1000;Height=650;
        Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(8,15,28));
        WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowTheme.Apply(this);
        var root=new DockPanel{Margin=new Thickness(20)};
        var cancel=new Button{Content="取消扫描",HorizontalAlignment=HorizontalAlignment.Right};
        DockPanel.SetDock(cancel,Dock.Top);root.Children.Add(cancel);
        var status=new TextBlock{Text="扫描全部本地盘中 ≥100 MB 的文件，最多展示最大的1000项。",Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(status,Dock.Top);root.Children.Add(status);
        cancel.Click+=(_,_)=>{_cancel.Cancel();cancel.IsEnabled=false;status.Text="正在取消…";};
        var open=new Button{Content="打开选中文件所在位置",Height=44,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0)};
        open.Click+=(_,_)=>{if(_grid.SelectedItem is LargeFile row&&File.Exists(row.Path))try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+row.Path+"\""){UseShellExecute=true});}catch(Exception ex){status.Text="无法打开位置："+ex.Message;}};
        DockPanel.SetDock(open,Dock.Bottom);root.Children.Add(open);
        foreach(var (header,path,width) in new[]{("大小","Size",120),("类型","Extension",100),("修改时间","Modified",170),("文件路径","Path",540)})
            _grid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new System.Windows.Data.Binding(path){Mode=System.Windows.Data.BindingMode.OneWay},Width=width});
        root.Children.Add(_grid);Content=root;
        Closed+=(_,_)=>_cancel.Cancel();
        Loaded+=async(_,_)=>
        {
            try
            {
                var progress=new Progress<string>(message=>status.Text=message);
                var result=await Task.Run(()=>Scan(progress,_cancel.Token));
                if(!_cancel.IsCancellationRequested){_grid.ItemsSource=result.Files;status.Text=$"已检查 {result.Count:N0} 个文件 · 显示 {result.Files.Length} 项 · 跳过不可访问/链接 {result.Errors:N0} 项。大文件不等于垃圾。";}
            }
            catch(OperationCanceledException){status.Text="扫描已取消，未删除任何文件。";}
            catch(Exception error){status.Text="扫描失败："+error.Message;}
            finally{cancel.IsEnabled=false;}
        };
    }
    private static (LargeFile[] Files,int Count,int Errors) Scan(IProgress<string> progress,CancellationToken token)
    {
        var largest=new PriorityQueue<LargeFile,long>();int count=0,errors=0;
        foreach(var drive in DriveCatalog.ReadyLocal())
        {
            var pending=new Stack<string>();string root=drive.RootDirectory.FullName;pending.Push(root);
            while(pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                if(!Safety.SafePath(directory,root)){errors++;continue;}
                try
                {
                    foreach(var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if((entry.Attributes&FileAttributes.ReparsePoint)!=0){errors++;continue;}
                            if((entry.Attributes&FileAttributes.Directory)!=0){pending.Push(entry.FullName);continue;}
                            var file=(FileInfo)entry;count++;
                            if(count%2000==0)progress.Report($"扫描 {drive.Name} · 已检查 {count:N0} 文件；关闭窗口可取消。");
                            if(file.Length<100L*1024*1024)continue;
                            largest.Enqueue(new(file.FullName,file.Length,file.Extension,file.LastWriteTime),file.Length);
                            if(largest.Count>1000)largest.Dequeue();
                        }
                        catch(IOException){errors++;}catch(UnauthorizedAccessException){errors++;}
                    }
                }
                catch(IOException){errors++;}catch(UnauthorizedAccessException){errors++;}
            }
        }
        return(largest.UnorderedItems.Select(x=>x.Element).OrderByDescending(x=>x.Bytes).ToArray(),count,errors);
    }
    private sealed record LargeFile(string Path,long Bytes,string Extension,DateTime Modified){public string Size=>Format.Bytes(Bytes);}
}

