using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ellipse=System.Windows.Shapes.Ellipse;
namespace NeonDesk;

public partial class MainWindow : Window
{
    private void LargeFiles_Click(object sender,RoutedEventArgs e) => new LargeFilesWindow { Owner=this }.Show();
    private void Duplicates_Click(object sender,RoutedEventArgs e) => new DuplicatesWindow { Owner=this }.Show();
    private void InstalledApps_Click(object sender,RoutedEventArgs e) => new InstalledAppsWindow(_usage){Owner=this}.Show();
    private void StartupApps_Click(object sender,RoutedEventArgs e) => OpenSettings("ms-settings:startupapps");
    private void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri){UseShellExecute=true}); }
        catch(Exception ex) { MessageBox.Show(this,"无法打开 Windows 设置："+ex.Message,"系统管理入口"); }
    }
    private void CategoryChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_scan is null)return;
        string category=(CacheCategories.SelectedItem as CacheCategory)?.Name??"全部缓存";
        CacheGrid.ItemsSource=_scan.Caches.Where(c=>category=="全部缓存"||c.App==category).OrderByDescending(c=>c.Bytes).ToList();
        UpdateSelectionSummary();
    }
    private void UpdateSelectionSummary()
    {
        if(SelectionSummary is null)return;
        var rows=_scan?.Caches??[];
        SelectionSummary.Text=$"已勾选 {rows.Count(r=>r.Selected)} 项 · 预计最多释放 {Format.Bytes(rows.Where(r=>r.Selected).Sum(r=>r.Bytes))} · 全部分类合计";
    }
    private readonly Metrics _metrics=new();
    private readonly History _history=new();
    private readonly UsageObserver _usage;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly Queue<(DateTime Time,double Value)> _cpu=[];
    private readonly Queue<string> _log=[];
    private CancellationTokenSource? _operation;
    private Task? _metricsTask;
    private ScanReport? _scan;
    private List<RootUsage> _volumeRows=[];
    private readonly List<Ellipse> _particles=[];
    private int _job;
    private bool _busy,_metricsBusy,_closed;
    public MainWindow()
    {
        InitializeComponent();
        _usage=new(_history.Folder);
        _timer.Tick+=RefreshMetrics;
        Loaded+=(_,_)=>{CreateParticles();ApplyMotion();_timer.Start();RefreshMetrics(this,EventArgs.Empty);};
        StateChanged+=(_,_)=>ApplyMotion();
        HistoryGrid.ItemsSource=_history.Cleanups().OrderByDescending(r=>r.Time).ToList();
        Footer.Text="LOCAL DATA  /  "+_history.Folder;
        Log("清理台已就绪。扫描只读取文件元数据，不读取文档或聊天内容。");
    }
    private async void RefreshMetrics(object? sender,EventArgs e)
    {
        if(_metricsBusy||_closed)return;_metricsBusy=true;_usage.Observe();
        try
        {
            Clock.Text=DateTime.Now.ToString("yyyy.MM.dd  /  HH:mm:ss");
            var task=Task.Run(_metrics.Read);_metricsTask=task;
            var sample=await task;if(_closed)return;
            CpuValue.Text=sample.Cpu is double cpu?$"{cpu:0}%":"采样中";CpuBar.Value=sample.Cpu??0;
            CoreValue.Text=CpuValue.Text;
            MemoryValue.Text=double.IsFinite(sample.Memory)?$"{sample.Memory:0}%":"不可用";MemoryBar.Value=double.IsFinite(sample.Memory)?sample.Memory:0;
            GpuValue.Text=sample.Gpu is double gpu?$"{gpu:0}%":"不可用";GpuBar.Value=sample.Gpu??0;
            GpuDetail.Text=sample.Gpu is null?"驱动计数器未提供数据":"最忙引擎 · 不是温度";
            DownloadValue.Text="↓ "+Format.Bytes(sample.Download)+"/s";UploadValue.Text="↑ "+Format.Bytes(sample.Upload)+"/s";
            DiskIoValue.Text=sample.DiskRead is double read&&sample.DiskWrite is double write?$"读 {Format.Bytes(read)}/s · 写 {Format.Bytes(write)}/s":"磁盘读写计数器不可用";
            DriveTiles.ItemsSource=sample.Drives;
            DriveCount.Text=$"{sample.Drives.Count:00} / 已就绪本地磁盘";
            VolumeSummary.Text="已连接 "+string.Join("  ·  ",sample.Drives.Select(d=>d.Name));
            if(sample.Cpu is double point)_cpu.Enqueue((DateTime.UtcNow,point));
            while(_cpu.Count>0&&_cpu.Peek().Time<DateTime.UtcNow.AddSeconds(-60))_cpu.Dequeue();
            DrawChart();
        }
        catch(Exception exception){if(!_closed)Footer.Text="监测暂不可用 / "+exception.Message;}
        finally{_metricsBusy=false;}
    }
    private void DrawChart()
    {
        double width=CpuChart.ActualWidth,height=CpuChart.ActualHeight;if(width<=0||height<=0)return;
        var now=DateTime.UtcNow;
        CpuLine.Points=new PointCollection(_cpu.Select(p=>new Point(Math.Clamp(1-(now-p.Time).TotalSeconds/60,0,1)*width,height-Math.Clamp(p.Value,0,100)*height/100)));
    }
    private void Chart_SizeChanged(object sender,SizeChangedEventArgs e){if(CpuLine is not null)DrawChart();}
    private void Page(FrameworkElement page,string title)
    {
        foreach(var item in new FrameworkElement[]{OverviewPage,DiskPage,CleanupPage,GrowthPage,LogsPage})item.Visibility=item==page?Visibility.Visible:Visibility.Collapsed;
        PageTitle.Text=title;
        NavOverview.IsChecked=page==OverviewPage;NavDisk.IsChecked=page==DiskPage;
        NavCleanup.IsChecked=page==CleanupPage;NavGrowth.IsChecked=page==GrowthPage;NavLogs.IsChecked=page==LogsPage;
        ApplyMotion();
    }
    private void Overview_Click(object sender,RoutedEventArgs e)=>Page(OverviewPage,"实时概览");
    private void DiskPage_Click(object sender,RoutedEventArgs e)=>Page(DiskPage,"全盘雷达");
    private void CleanupPage_Click(object sender,RoutedEventArgs e)=>Page(CleanupPage,"缓存清理");
    private void GrowthPage_Click(object sender,RoutedEventArgs e)=>Page(GrowthPage,"空间与增长");
    private void LogsPage_Click(object sender,RoutedEventArgs e)=>Page(LogsPage,"执行记录");
    private void Busy(bool busy)
    {
        _busy=busy;ScanButton.IsEnabled=!busy;CleanButton.IsEnabled=!busy;CancelButton.IsEnabled=busy;CacheGrid.IsEnabled=!busy;
        ScanProgress.Visibility=busy&&MotionToggle.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
    }
    private void Log(string text)
    {
        _log.Enqueue($"[{DateTime.Now:HH:mm:ss}] {text}");while(_log.Count>200)_log.Dequeue();
        EventLog.Text=string.Join(Environment.NewLine,_log);EventLog.ScrollToEnd();
    }
    private async void Scan_Click(object sender,RoutedEventArgs e)
    {
        if(_busy)return;Busy(true);_scan=null;CacheGrid.ItemsSource=null;
        _operation=new();int job=++_job;Log("开始全盘扫描：所有已就绪本地盘、应用缓存和过期临时文件。可随时取消。");
        try
        {
            var progress=new Progress<string>(message=>{if(_busy&&job==_job)Activity.Text=message;});
            var report=await Task.Run(()=>Scanner.Scan(progress,_operation.Token));
            _scan=report;AppGrid.ItemsSource=report.Apps.OrderByDescending(a=>a.Bytes).ToList();
            foreach(var cache in report.Caches)cache.PropertyChanged+=(_,_)=>UpdateSelectionSummary();
            CacheCategories.ItemsSource=new[]{new CacheCategory("全部缓存",report.Caches)}.Concat(report.Caches.GroupBy(c=>c.App).OrderByDescending(g=>g.Sum(c=>c.Bytes)).Select(g=>new CacheCategory(g.Key,g.ToList()))).ToArray();
            CacheCategories.SelectedIndex=0;
            CacheGrid.ItemsSource=report.Caches.OrderByDescending(c=>c.Bytes).ToList();RootGrid.ItemsSource=report.Roots.OrderByDescending(r=>r.Bytes).ToList();
            int errors=report.Apps.Sum(a=>a.Errors)+report.Caches.Sum(c=>c.Errors)+report.VolumeRoots.Sum(r=>r.Errors);
            Activity.Text=$"扫描完成 · 应用逻辑体积 {Format.Bytes(report.Apps.Sum(a=>a.Bytes))} · 白名单缓存 {Format.Bytes(report.Caches.Sum(c=>c.Bytes))}";
            CacheTotal.Text=Format.Bytes(report.Caches.Sum(c=>c.Bytes));
            ScanDetail.Text=$"{report.Drives.Length} 个本地盘 · {report.Caches.Count} 个缓存目录 · {errors} 项访问或扫描限制 · 软件占用不等于可删除量";
            _volumeRows=report.VolumeRoots;DiskFilter.ItemsSource=new[]{"全部磁盘"}.Concat(report.Drives.Select(d=>d.TrimEnd('\\'))).ToArray();DiskFilter.SelectedIndex=0;
            try
            {
                var growth=_history.AddScan(report);GrowthGrid.ItemsSource=growth.Rows;
                RootGrid.ItemsSource=growth.Roots.Where(r=>!r.App.StartsWith("磁盘 ",StringComparison.Ordinal)).OrderByDescending(r=>r.Bytes).ToList();
                _volumeRows=growth.Roots.Where(r=>r.App.StartsWith("磁盘 ",StringComparison.Ordinal)).ToList();ApplyDiskFilter();
                BaselineText.Text=growth.Previous is null?"已建立首次基线。下次扫描后显示变化。":$"对比 {growth.Previous:yyyy-MM-dd HH:mm} 的扫描；逻辑体积变化不等于释放空间。";
            }
            catch(Exception exception){Log("无法保存扫描快照："+exception.Message);}
            ApplyDiskFilter();
            Log($"扫描完成，可选择的白名单缓存 {Format.Bytes(report.Caches.Where(c=>c.Selectable).Sum(c=>c.Bytes))}。没有执行删除。");
        }
        catch(OperationCanceledException){Activity.Text="扫描已取消。未执行清理。";Log("扫描取消，未完成结果不会保存为基线。");}
        catch(Exception exception){Activity.Text="扫描失败："+exception.Message;Log(Activity.Text);}
        finally{_operation.Dispose();_operation=null;Busy(false);}
    }
    private void Cancel_Click(object sender,RoutedEventArgs e){_operation?.Cancel();CancelButton.IsEnabled=false;Activity.Text="正在停止当前任务…";}
    private void SelectAll_Click(object sender,RoutedEventArgs e){if(_busy||_scan is null)return;foreach(var row in CacheGrid.Items.OfType<CacheRow>())row.Selected=row.Selectable;}
    private void SelectNone_Click(object sender,RoutedEventArgs e){if(_busy||_scan is null)return;foreach(var row in _scan.Caches)row.Selected=false;}
    private void Cache_SelectionChanged(object sender,SelectionChangedEventArgs e){if(CachePath is not null&&CacheGrid.SelectedItem is CacheRow row)CachePath.Text=row.Path;}
    private static void Explore(string path)
    {
        if(Directory.Exists(path))Process.Start(new ProcessStartInfo("explorer.exe",'"'+path+'"'){UseShellExecute=true});
    }
    private void OpenCache_Click(object sender,RoutedEventArgs e){if(CacheGrid.SelectedItem is CacheRow row){try{Explore(row.Path);}catch(Exception ex){Log("打开目录失败："+ex.Message);}}}
    private void OpenRoot_Click(object sender,RoutedEventArgs e){if(RootGrid.SelectedItem is RootUsage row){try{Explore(row.Path);}catch(Exception ex){Log("打开目录失败："+ex.Message);}}}
    private void OpenDisk_Click(object sender,RoutedEventArgs e){if(DiskGrid.SelectedItem is RootUsage row){try{Explore(row.Path);}catch(Exception ex){Log("打开目录失败："+ex.Message);}}}
    private void DiskFilter_Changed(object sender,SelectionChangedEventArgs e){if(DiskGrid is not null)ApplyDiskFilter();}
    private void ApplyDiskFilter()
    {
        var selected=DiskFilter.SelectedItem as string;
        var rows=_volumeRows.Where(r=>selected is null||selected=="全部磁盘"||string.Equals(Path.GetPathRoot(r.Path)?.TrimEnd('\\'),selected,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Bytes).ToList();
        DiskGrid.ItemsSource=rows;
        double max=rows.Count>0?Math.Max(1,rows[0].Bytes):1;
        DiskMap.ItemsSource=rows.Take(18).Select(r=>new {Label=r.App+"  "+(Path.GetFileName(r.Path.TrimEnd('\\')) is {Length:>0} leaf?leaf:"根目录文件"),r.Path,r.Size,Width=Math.Max(1,300*r.Bytes/max)}).ToList();
    }
    private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void Motion_Changed(object sender,RoutedEventArgs e){if(IsLoaded)ApplyMotion();}
    private void CreateParticles()
    {
        var random=new Random(42);
        for(int i=0;i<16;i++)
        {
            double size=random.Next(2,5);var point=new Ellipse {Width=size,Height=size,Fill=new SolidColorBrush(i%2==0?Color.FromRgb(255,101,122):Color.FromRgb(255,161,141)),Opacity=.28,RenderTransform=new TranslateTransform()};
            Canvas.SetLeft(point,random.NextDouble()*Math.Max(1,ActualWidth));Canvas.SetTop(point,random.NextDouble()*Math.Max(1,ActualHeight));
            Particles.Children.Add(point);_particles.Add(point);
        }
    }
    private void ApplyMotion()
    {
        if(OrbitSpin is null||MotionToggle is null)return;
        bool motion=MotionToggle.IsChecked==true&&WindowState!=WindowState.Minimized&&!_closed;
        bool hero=motion&&OverviewPage.Visibility==Visibility.Visible;
        OrbitSpin.BeginAnimation(RotateTransform.AngleProperty,hero?new DoubleAnimation(0,360,TimeSpan.FromSeconds(15)){RepeatBehavior=RepeatBehavior.Forever}:null);
        InnerSpin.BeginAnimation(RotateTransform.AngleProperty,hero?new DoubleAnimation(360,0,TimeSpan.FromSeconds(22)){RepeatBehavior=RepeatBehavior.Forever}:null);
        CoreHalo.BeginAnimation(OpacityProperty,hero?new DoubleAnimation(.2,.75,TimeSpan.FromSeconds(2.5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever}:null);
        HeroSweep.BeginAnimation(TranslateTransform.XProperty,hero?new DoubleAnimation(-220,1300,TimeSpan.FromSeconds(6)){RepeatBehavior=RepeatBehavior.Forever}:null);
        int index=0;foreach(var point in _particles)
        {
            ((TranslateTransform)point.RenderTransform).BeginAnimation(TranslateTransform.YProperty,motion?new DoubleAnimation(-18,36,TimeSpan.FromSeconds(5+index%5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever}:null);
            point.BeginAnimation(OpacityProperty,motion?new DoubleAnimation(.1,.65,TimeSpan.FromSeconds(2+index%3)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever}:null);index++;
        }
        ScanProgress.Visibility=_busy&&motion?Visibility.Visible:Visibility.Collapsed;
    }
    private async void Clean_Click(object sender,RoutedEventArgs e)
    {
        if(_busy||_scan is null)return;
        CacheGrid.CommitEdit(DataGridEditingUnit.Cell,true);CacheGrid.CommitEdit(DataGridEditingUnit.Row,true);
        var selected=_scan.Caches.Where(r=>r.Selected&&r.Selectable).ToArray();
        if(selected.Length==0){MessageBox.Show(this,"请先扫描并勾选要清理的缓存目录。正在运行的软件需要先退出，然后重新扫描。","尚未选择缓存");return;}
        string detail=string.Join("\n",selected.Take(8).Select(r=>$"• {r.App} / {r.Name} / {r.Size}"));
        if(selected.Length>8)detail+=$"\n以及另外 {selected.Length-8} 个目录。";
        if(MessageBox.Show(this,$"将永久删除选中的白名单缓存文件，无法撤销。\n\n{detail}\n\n预计最多 {Format.Bytes(selected.Sum(r=>r.Bytes))}。变化或占用中的文件会跳过。\n确认清理？","确认清理选中缓存",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        Busy(true);_operation=new();
        try
        {
            var progress=new Progress<string>(message=>{Activity.Text=message;Log(message);});
            var summary=await Task.Run(()=>Cleaner.Clean(selected,progress,_operation.Token));
            Activity.Text=(summary.Cancelled?"已取消 · ":"")+$"已删除 {summary.Deleted:N0} 个缓存文件 · 文件体积 {Format.Bytes(summary.Bytes)} · 跳过 {summary.Skipped:N0} 项";
            ScanDetail.Text="清理已结束。请重新扫描以更新占用；磁盘可用空间由实时监测刷新。";Log(Activity.Text);
            try{_history.AddCleanup(summary);HistoryGrid.ItemsSource=_history.Cleanups().OrderByDescending(r=>r.Time).ToList();}catch(Exception ex){Log("清理完成，但保存记录失败："+ex.Message);}
        }
        catch(OperationCanceledException){Activity.Text="清理已停止；已删除的文件无法撤销。";Log(Activity.Text);}
        catch(Exception exception){Activity.Text="清理中断："+exception.Message;Log(Activity.Text);}
        finally
        {
            _scan=null;CacheGrid.ItemsSource=null;CacheCategories.ItemsSource=null;UpdateSelectionSummary();_operation.Dispose();_operation=null;Busy(false);
        }
    }
    private void Processes_Click(object sender,RoutedEventArgs e)
    {
        var rows=new List<ProcessRow>();foreach(var process in Process.GetProcesses()){using(process){try{rows.Add(new(process.Id,process.ProcessName,process.WorkingSet64));}catch{}}}
        var grid=new DataGrid{IsReadOnly=true,ItemsSource=rows.OrderByDescending(p=>p.Memory).Take(60).ToList(),Margin=new Thickness(16)};
        grid.Columns.Add(new DataGridTextColumn{Header="PID",Binding=new System.Windows.Data.Binding("Id"),Width=100});
        grid.Columns.Add(new DataGridTextColumn{Header="进程",Binding=new System.Windows.Data.Binding("Name"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="工作集内存",Binding=new System.Windows.Data.Binding("Size"),Width=160});
        new Window{Title="进程内存排行 · 工作集可能包含共享页",Owner=this,Width=730,Height=580,Background=new SolidColorBrush(Color.FromRgb(8,15,28)),Content=grid,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(_busy){e.Cancel=true;_operation?.Cancel();Activity.Text="正在停止任务，完成后可关闭窗口。";}
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        _closed=true;_timer.Stop();ApplyMotion();
        if(_metricsTask is {IsCompleted:false} task)_=task.ContinueWith(_=>_metrics.Dispose(),TaskScheduler.Default);else _metrics.Dispose();
        base.OnClosed(e);
    }
}
