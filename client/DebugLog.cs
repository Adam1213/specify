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
    private static BlockingCollection<string> DebugLogQueue;

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
        OpenTask(region, taskName);
        try
        {
            await Task.Run(task);
        }
        finally
        {
            CloseTask(region, taskName);
        }
    }

    public static async Task DoTask(Region region, string taskName, Func<Task> task)
    {
        OpenTask(region, taskName);
        try
        {
            await task();
        }
        finally
        {
            CloseTask(region, taskName);
        }
    }

    public static void OpenTask(Region region, string taskName)
    {
        if (OpenedTasks.TryAdd((region, taskName), DateTime.Now)) //Will fail if already exists
        {
            LogEvent($"Task Started: {taskName}", region);
        }
        // Ensure OpenTask hasn't been called twice on the same task.
        else
        {
            LogEvent($"{taskName} has already been started. This is a specify-specific error.", region, EventType.ERROR);
        }
    }
    public static void CloseTask(Region region, string taskName)
    {
        if (OpenedTasks.TryRemove((region, taskName), out DateTime startedAt)) //Will fail if already removed
        {
            LogEvent($"Task Completed: {taskName} - Runtime: {(DateTime.Now - startedAt).TotalMilliseconds}", region);
        }
        // Ensure CloseTask hasn't been called on a task that was never opened, or called twice on the same task.
        else
        {
            LogEvent($"DebugLog Task could not be closed. Task was not in list. {taskName}", region, EventType.ERROR);
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

    public static void StartDebugLog()
    {
        LogText = "";
        LogStartTime = DateTime.Now;
        Started = true;

        //Initializing region start/end time and error count is unnecessary

        _ = Task.Run(DebugLogLoop);

        LogEvent($"--- DEBUG LOG STARTED {LogStartTime.ToString("HH:mm:ss")} ---");
        LogSettings();
    }

    private static void DebugLogLoop()
    {
        DebugLogQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        using var writer = new StreamWriter(LogFilePath);
        while (Started || DebugLogQueue.Any())
        {
            if (!DebugLogQueue.TryTake(out string debugString,200))
                continue;

            if (Settings.EnableDebug)
            {
                try
                {
                    writer.Write(debugString);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(LogFailureFilePath, ex.ToString());
                    Settings.EnableDebug = false;
                }
            }
            LogText += debugString;
        }
    }

    public static void StopDebugLog()
    {
        /*if(!Settings.EnableDebug)
        {
            return;
        }*/
        foreach (var region in StartedRegions.Where(x => !x.Value.EndTime.HasValue))
        {
            if (!region.Value.EndTime.HasValue)
            {
                LogEvent($"Logging completed with unfinished region: {region.Key}", region.Key, EventType.ERROR);
            }
            if (region.Value.errorCount > 0)
            {
                LogEvent($"{region.Key} Data Errors: {region.Value.errorCount}");
            }
        }

        LogEvent($"Total Elapsed Time: {(DateTime.Now - LogStartTime).TotalMilliseconds}");
        LogEvent($"--- DEBUG LOG FINISHED {DateTime.Now.ToString("HH:mm:ss")} ---");
        Started = false;
    }

    public static void StartRegion(Region region)
    {
        /*if(!Settings.EnableDebug)
        {
            return;
        }*/
        if (!StartedRegions.TryAdd(region, new RegionStats() { startTime = DateTime.Now }))
        {
            LogEvent($"{region} Region already started.", region, EventType.ERROR);
            return;
        }
        LogEvent($"{region} Region Start", region, EventType.REGION_START);
    }

    public static void EndRegion(Region region)
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
                LogEvent($"Region already completed.", region, EventType.ERROR);
                return;
            }

            LogEvent($"{region} Region End - Total Time: {(finishTime - regionStats.startTime).TotalMilliseconds}ms", region, EventType.REGION_END);
            regionStats.EndTime = finishTime;
            StartedRegions[region] = regionStats;
        }
        else
        {
            LogEvent($"Tried to end region that was not started.", region, EventType.ERROR);
        }
    }

    public static void LogEvent(string message, Region region = Region.Misc, EventType type = EventType.INFORMATION)
    {
        if (!Started)
        {
            return;
        }
        string debugString;
        if (region != Region.Misc && (!StartedRegions.TryGetValue(region, out RegionStats stats) || stats.EndTime.HasValue))
        {
            debugString = CreateDebugString($"Logging attempted on uninitialized region - {message}", region, EventType.ERROR);
        }
        else
            debugString = CreateDebugString(message, region, type);

        DebugLogQueue.Add(debugString);
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

    private static void LogSettings()
    {
        var properties = typeof(Settings).GetProperties();
        foreach (PropertyInfo property in properties)
        {
            LogEvent($"{property.Name}: {property.GetValue(null)}");
        }
    }

    public static void LogFatalError(string message, Region region)
    {
        Settings.EnableDebug = true;
        LogEvent("UNEXPECTED FATAL EXCEPTION", region, EventType.ERROR);
        LogEvent(message, region, EventType.ERROR);
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