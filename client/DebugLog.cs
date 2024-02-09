using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace specify_client;

public class RegionStats
{
    public DateTime startTime;
    public DateTime? EndTime { get; set; }
    public int errorCount;
}

public static class DebugLog
{

    public static string LogText;
    private static bool Started = false;
    private static DateTime LogStartTime { get; set; }
    private const string LogFilePath = "specify_debug.log";
    private const string LogFailureFilePath = "specify_log_failure.log";
    private static readonly ConcurrentDictionary<Region, RegionStats> StartedRegions = new();
    private static readonly ConcurrentDictionary<(Region region, string taskName), DateTime> OpenedTasks = new();

    private static SemaphoreSlim logSemaphore = new(1, 1);

    public enum Region
    {
        Main = 0,
        System = 1,
        Security = 2,
        Networking = 3,
        Hardware = 4,
        Events = 5,
        Misc = 6

    }

    public enum EventType
    {
        REGION_START = 0,
        INFORMATION = 1,
        WARNING = 2,
        ERROR = 3,
        REGION_END = 4
    }

    public static int GetErrorCountForRegion(Region region) => StartedRegions.TryGetValue(region, out var stats) ? stats.errorCount : 0;

    public static async Task DoTask(Region region, string taskName, Action task)
    {
        await OpenTask(region, taskName);
        try
        {
            task.Invoke();
        }
        finally
        {
            await CloseTask(region, taskName);
        }
    }

    public static async Task DoTask(Region region, string taskName, Func<Task> task)
    {
        await OpenTask(region, taskName);
        try
        {
            await Task.Factory.StartNew(task, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
        finally
        {
            await CloseTask(region, taskName);
        }
    }

    public static async Task OpenTask(Region region, string taskName)
    {
        if (OpenedTasks.TryAdd((region, taskName), DateTime.Now)) //Will fail if already exists
        {
            await LogEventAsync($"Task Started: {taskName}", region);
        }
        // Ensure OpenTask hasn't been called twice on the same task.
        else
        {
            await LogEventAsync($"{taskName} has already been started. This is a specify-specific error.", region, EventType.ERROR);
        }
    }
    public static async Task CloseTask(Region region, string taskName)
    {
        if (OpenedTasks.TryRemove((region, taskName), out DateTime startedAt)) //Will fail if already removed
        {
            await LogEventAsync($"Task Completed: {taskName} - Runtime: {(DateTime.Now - startedAt).TotalMilliseconds}", region);
        }
        // Ensure CloseTask hasn't been called on a task that was never opened, or called twice on the same task.
        else
        {
            await LogEventAsync($"DebugLog Task could not be closed. Task was not in list. {taskName}", region, EventType.ERROR);
        }
    }
    /// <summary>
    /// Verifies that no tasks remain open and will log errors for each open task. This method should only be run one time: Immediately prior to serialization.
    /// </summary>
    /// <returns></returns>
    public static void CheckOpenTasks()
    {
        foreach (var taskGroup in OpenedTasks.GroupBy(x => x.Key.region)) //Group by region
        {
            LogEvent($"{taskGroup.Key} has outstanding tasks:", taskGroup.Key, EventType.ERROR);
            foreach (var task in taskGroup.Select(x => x.Key))
            {
                LogEvent($"OUTSTANDING: {task.taskName}", task.region);
            }
        }
    }

    public static async Task StartDebugLog()
    {
        LogText = "";
        LogStartTime = DateTime.Now;
        if (!File.Exists(LogFilePath) && Settings.EnableDebug)
        {
            File.Create(LogFilePath).Close();
        }
        else if (Settings.EnableDebug)
        {
            await Task.Run(() => File.WriteAllText(LogFilePath, ""));
        }

        //Initializing region start/end time and error count is unnecessary

        Started = true;
        await LogEventAsync($"--- DEBUG LOG STARTED {LogStartTime.ToString("HH:mm:ss")} ---");
        await LogSettings();
    }

    public static async Task StopDebugLog()
    {
        /*if(!Settings.EnableDebug)
        {
            return;
        }*/
        foreach (var region in StartedRegions.Where(x => !x.Value.EndTime.HasValue))
        {
            if (!region.Value.EndTime.HasValue)
            {
                await LogEventAsync($"Logging completed with unfinished region: {region.Key}", region.Key, EventType.ERROR);
            }
            if (region.Value.errorCount > 0)
        {
                await LogEventAsync($"{region.Key} Data Errors: {region.Value.errorCount}");
        }
        }

        await LogEventAsync($"Total Elapsed Time: {(DateTime.Now - LogStartTime).TotalMilliseconds}");
        await LogEventAsync($"--- DEBUG LOG FINISHED {DateTime.Now.ToString("HH:mm:ss")} ---");
        Started = false;
    }

    public static async Task StartRegion(Region region)
    {
        /*if(!Settings.EnableDebug)
        {
            return;
        }*/
        if (!StartedRegions.TryAdd(region, new RegionStats() { startTime = DateTime.Now }))
        {
            await LogEventAsync($"{region} Region already started.", region, EventType.ERROR);
            return;
        }
        await LogEventAsync($"{region} Region Start", region, EventType.REGION_START);
    }

    public static async Task EndRegion(Region region)
    {
        DateTime finishTime = DateTime.Now;
        /*if(!Settings.EnableDebug)
        {
            return;
        }*/
        if (StartedRegions.TryGetValue(region, out var regionStats))
        {
            if (regionStats.EndTime.HasValue)
        {
            await LogEventAsync($"Region already completed.", region, EventType.ERROR);
            return;
        }

            await LogEventAsync($"{region} Region End - Total Time: {(finishTime - regionStats.startTime).TotalMilliseconds}ms", region, EventType.REGION_END);
            regionStats.EndTime = finishTime;
            StartedRegions[region] = regionStats;
        }
        else
        {
            await LogEventAsync($"Tried to end region that was not started.", region, EventType.ERROR);
        }
    }

    public static async Task LogEventAsync(string message, Region region = Region.Misc, EventType type = EventType.INFORMATION)
    {
        if (!Started)
        {
            return;
        }
        string debugString = CreateDebugString(message, region, type);
        if (region != Region.Misc && (!StartedRegions.TryGetValue(region, out RegionStats stats) || stats.EndTime.HasValue))
        {
            debugString = CreateDebugString($"Logging attempted on uninitialized region - {message}", region, EventType.ERROR);
        }

        if (Settings.EnableDebug)
        {
            await logSemaphore.WaitAsync();
            int retryCount = 0;
            while (true)
            {
                try
                {
                    var writer = new StreamWriter(LogFilePath, true);
                    await writer.WriteAsync(debugString);
                    writer.Close();
                    break;
                }
                catch (Exception ex)
                {
                    if (retryCount > 10)
                    {
                        File.WriteAllText(LogFailureFilePath, ex.ToString());
                        Settings.EnableDebug = false;
                        break;
                    }
                    await Task.Delay(30);
                    retryCount++;
                    continue;
                }
            }
            logSemaphore.Release();
        }
        LogText += debugString;
    }

    public static void LogEvent(string message, Region region = Region.Misc, EventType type = EventType.INFORMATION)
    {
        if (!Started)
        {
            return;
        }
        string debugString = CreateDebugString(message, region, type);
        if (region != Region.Misc && (!StartedRegions.TryGetValue(region, out RegionStats stats ) || stats.EndTime.HasValue))
        {
            debugString = CreateDebugString($"Logging attempted on uninitialized region - {message}", region, EventType.ERROR);
        }
        if (Settings.EnableDebug)
        {
            logSemaphore.Wait();
            int retryCount = 0;
            while (true)
            {
                try
                {
                    var writer = new StreamWriter(LogFilePath, true);
                    writer.Write(debugString);
                    writer.Close();
                    break;
                }
                catch (Exception ex)
                {
                    if (retryCount > 10)
                    {
                        File.WriteAllText(LogFailureFilePath, ex.ToString());
                        Settings.EnableDebug = false;
                        break;
                    }
                    Thread.Sleep(30);
                    retryCount++;
                    continue;
                }
            }
            logSemaphore.Release();
        }
        LogText += debugString;
    }

    private static string CreateDebugString(string message, Region region, EventType type)
    {
        string debugString = $"[{(DateTime.Now - LogStartTime).TotalMilliseconds}]";
        while (debugString.Length < 12)
        {
            debugString += " ";
        }
        switch (type)
        {
            case EventType.INFORMATION:
                debugString += " [Information] ";
                break;

            case EventType.WARNING:
                debugString += "     [Warning] ";
                break;

            case EventType.ERROR:
                debugString += "       [ERROR] !!! ";
                break;

            case EventType.REGION_START:
                debugString += "[Region Start] --- ";
                break;

            case EventType.REGION_END:
                debugString += "  [Region End] --- ";
                break;
        }
        debugString += message;
        if (type == EventType.ERROR)
        {
            debugString += " !!! ";
            
            Interlocked.Increment(ref StartedRegions[region].errorCount);
        }
        if (type == EventType.REGION_START || type == EventType.REGION_END)
        {
            debugString += " --- ";
        }
        while (debugString.Length < 90)
        {
            debugString += " ";
        }
        debugString += " :";
        if (region != Region.Misc)
        {
            debugString += $" {region}";
        }
        debugString += "\r\n";
        return debugString;
    }

    private static async Task LogSettings()
    {
        var properties = typeof(Settings).GetProperties();
        foreach (PropertyInfo property in properties)
        {
            await LogEventAsync($"{property.Name}: {property.GetValue(null)}");
        }
    }

    public static async Task LogFatalError(string message, Region region)
    {
        Settings.EnableDebug = true;
        await LogEventAsync("UNEXPECTED FATAL EXCEPTION", region, EventType.ERROR);
        await LogEventAsync(message, region, EventType.ERROR);
        while (true)
        {
            try
            {
                File.WriteAllText(LogFilePath, LogText);
                break;
            }
            catch
            {
                Thread.Sleep(30);
                continue;
            }
        }

        Monolith.ProgramDone(ProgramDoneState.ProgramFailed);
    }
}