using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace specify_client;

public class Program
{
    public const string SpecifyVersion = "v1.2.1";
    public static Stopwatch Time;

    public static async Task Main()
    {
        try
        {
            
            // Set Specify to run in en-US so system messages are printed in English.
            var culture = CultureInfo.CreateSpecificCulture("en-US");

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            Time = new Stopwatch();
            Time.Start();
            DebugLog.StartDebugLog();
            var pList = new ProgressList();

            await Task.WhenAll(data.Cache.MakeMainData()
                , data.Cache.MakeSystemData()
                , data.Cache.MakeSecurityData()
                , data.Cache.MakeNetworkData()
                , data.Cache.MakeHardwareData()
                , data.Cache.MakeEventData());

            pList.RunItem(ProgressList.Specificializing);
        }
        catch (Exception ex)
        {
            DebugLog.LogFatalError(ex.ToString(), DebugLog.Region.Misc);
        }

        await Task.CompletedTask;
    }
}
