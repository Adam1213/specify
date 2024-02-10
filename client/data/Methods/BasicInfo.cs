using System;
using System.Linq;
using System.Threading.Tasks;

namespace specify_client.data;

public static partial class Cache
{
    public static async Task MakeMainData()
    {
        try
        {
            DebugLog.Region region = DebugLog.Region.Main;
            DebugLog.StartRegion(region);
            Os = Utils.GetWmi("Win32_OperatingSystem").First();
            Cs = Utils.GetWmi("Win32_ComputerSystem").First();
            DebugLog.LogEvent("Main WMI Data retrieved.", region);
            DebugLog.EndRegion(region);
        }
        catch (Exception ex)
        {
            DebugLog.LogFatalError($"{ex}", DebugLog.Region.Main);
        }
        MainDataWriteSuccess = true;

        await Task.CompletedTask;
    }
}