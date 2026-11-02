using System.IO;
namespace NeonDesk;

public static class DriveCatalog
{
    public static List<DriveInfo> ReadyLocal()
    {
        var result=new List<DriveInfo>();
        try
        {
            foreach(var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if(drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)result.Add(drive);
                }
                catch(IOException){ }catch(UnauthorizedAccessException){ }
            }
        }
        catch(IOException){ }
        return result.OrderBy(d=>d.Name,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
