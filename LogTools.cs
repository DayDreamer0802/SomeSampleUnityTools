using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

[Flags]
public enum LogChannel
{
    None = 0,
    Core = 1,
    UI = 1 << 1,
    Net = 1 << 2,
    Battle = 1 << 3,
    Asset = 1 << 4,
    Audio = 1 << 5,
    AI = 1 << 6,
    Other = 1 << 7,
    All = ~0
}

public enum LogLevel
{
    Error,
    Warn,
    Info,
}

public static class LogTools
{
    #region Public Configuration
    
    // 将配置放在最上面，方便修改
    public static LogChannel EnabledChannels = LogChannel.All;

    public static readonly HashSet<string> IgnoreNamespaces = new()
    {
        "UnityEngine",
        "UnityEditor", 
        "System.Reflection", 
        "TMPro",   
    };
    
    public static readonly HashSet<string> WhiteList = new()
    {
        "UnityEngine.MonoBehaviour",
    };

    public static readonly HashSet<Type> IgnoreTypes = new()
    {
    };
   
    
    public static readonly HashSet<Type> IgnoreBaseTypes = new()
    {
        typeof(Delegate), 
        typeof(Type),
    };
    
    static bool ShouldIgnoreInternal(object obj, Type type)
    {
        // A. 基础检查
        if (obj == null) return false;
        
      
        // B. 精确匹配 (最快)
        if (IgnoreTypes.Contains(type)) return true;

        // C. 命名空间匹配
        if (type.Namespace != null)
        {
            foreach (var ns in IgnoreNamespaces)
            {

                if (type.Namespace.StartsWith(ns))
                {
                    foreach (var i in WhiteList)
                    {
                        if (type.Namespace.StartsWith(i))
                            return false;
                    }
                    return true;
                }
            }
        }
        
        // 遍历所有注册的基类，检查当前对象是否是它的子类
        foreach (var baseType in IgnoreBaseTypes)
        {
            if (baseType.IsAssignableFrom(type)) return true;
        }

        return false;
    }
    #endregion

    #region Private

    static readonly Color DeepSkyBlue = new Color(0 / 255f, 191 / 255f, 255 / 255f);
    static readonly Color PaleGreen = new Color(152 / 255f, 251 / 255f, 152 / 255f);
    static readonly Color Orange = new Color(255 / 255f, 165 / 255f, 0 / 255f);

    private static readonly Color DefaultSuffixColor = PaleGreen;
    private static readonly Color DefaultMonitorNoGCColor = DeepSkyBlue;
    private static readonly Color DefaultMonitorGCColor = Orange;

    // 辅助检查方法
    static bool IsChannelEnabled(LogChannel channel)
    {
        if (channel == LogChannel.None) return true;
        return (EnabledChannels & channel) != 0;
    }

    static string AddClassSuffix(this string msg, object obj, Color? color = null)
    {
        if (obj == null) return msg;
        var typeName = obj.GetType().Name;
        var prefix = color != null
            ? $"[{typeName}]"
            : $"<color=#{DefaultSuffixColor.ToHtmlStringRGB()}>[{typeName}]</color>";

        return $"{prefix} {msg}";
    }

    static string ToHtmlStringRGB(this Color color)
    {
        return ColorUtility.ToHtmlStringRGB(color);
    }

    
    [HideInCallstack]
    static void Log_Internal(string msg, Color? color = null, LogLevel logLevel = LogLevel.Info,
        UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        string prefix = channel != LogChannel.None ? $" [{channel}]  ".Colored(Color.white) : string.Empty;

        if (color != null)
            msg = msg.Colored(color.Value);

        string finalMsg = prefix + msg;

        switch (logLevel)
        {
            case LogLevel.Info:
                Debug.Log(finalMsg, context);
                break;
            case LogLevel.Warn:
                Debug.LogWarning(finalMsg, context);
                break;
            case LogLevel.Error:
                Debug.LogError(finalMsg, context);
                break;
        }
    }

  #region Serialize Object 优化：反射缓存层

    const int MaxDepth = 10;
    
    // 优化：加入反射缓存，极大降低高频打印复杂对象的 CPU 开销
    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();
    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();

    static string GetIndent(int depth) => new string(' ', depth * 2);

    static string Format(object obj)
    {
        StringBuilder sb = new StringBuilder();
        FormatInternal(sb, obj, new HashSet<object>(), 0);
        return sb.ToString();
    }

    static void FormatInternal(StringBuilder sb, object obj, HashSet<object> visited, int depth)
    {
        if (obj == null)
        {
            sb.Append("null");
            return;
        }
        
        if (depth > MaxDepth)
        {
            sb.Append("... (Max Depth)");
            return;
        }

        Type type = obj.GetType();

        if (ShouldIgnoreInternal(obj, type))
        {
            sb.Append($"[{type.Name}]{obj}"); 
            return;
        }
        
        // 2. 引用类型防死循环
        if (!type.IsValueType && obj is not string)
        {
            if (obj is UnityEngine.Object && obj is not MonoBehaviour)
            {
                sb.Append(obj);
                return;
            }

            if (!visited.Add(obj))
            {
                sb.Append($"[Circular Link: {type.Name}]");
                return;
            }
        }

        // 3. 基础类型
        if (obj is string || type.IsPrimitive || type.IsEnum)
        {
            sb.Append(obj);
            return;
        }

        // 4. 字典 [Dict]
        if (obj is IDictionary dict)
        {
            sb.Append($" [Dict] (Count: {dict.Count})"); // 加上类型和数量
            sb.AppendLine();
            sb.Append(GetIndent(depth));
            sb.AppendLine("{");
            
            foreach (DictionaryEntry entry in dict)
            {
                sb.Append(GetIndent(depth + 1));
                
                // 加上 [Key] 标识
                sb.Append("[Key] ");
                FormatInternal(sb, entry.Key, visited, depth + 1);
                
                sb.Append(" : [Value] "); // 分隔符
                
                FormatInternal(sb, entry.Value, visited, depth + 1);
                sb.AppendLine(",");
            }
            sb.Append(GetIndent(depth));
            sb.Append("}");
            return;
        }

        // 5. 集合/数组 [List]
        if (obj is IEnumerable enumerable)
        {
            // 尝试获取数量（Array 和 List 都有 Count/Length，但 IEnumerable 没有）
            int count = -1;
            if (obj is ICollection col) count = col.Count;
            
            sb.Append($" [List] {(count >= 0 ? $"(Count: {count})" : "")}"); 
            sb.AppendLine();
            sb.Append(GetIndent(depth));
            sb.AppendLine("[");
            
            int index = 0;
            foreach (var item in enumerable)
            {
                sb.Append(GetIndent(depth + 1));
                
                // 加上下标 [0]
                sb.Append($"[{index}] "); 
                
                FormatInternal(sb, item, visited, depth + 1);
                sb.AppendLine(",");
                index++;
            }
            sb.Append(GetIndent(depth));
            sb.Append("]");
            return;
        }

       if (type.IsClass || type.IsValueType)
        {
            if (depth > 0) sb.AppendLine();
            
            sb.Append(GetIndent(depth));
            sb.Append($"{type.Name} {{");
            sb.AppendLine();

            // 优化：从缓存读取 Fields，避免每次 GetFields 产生巨大的反射开销和 GC
            if (!FieldCache.TryGetValue(type, out var fields))
            {
                var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
                fields = type.GetFields(flags);
                FieldCache[type] = fields;
            }
            
            foreach (var field in fields)
            {
                if (ShouldIgnoreInternal(field, field.DeclaringType)) continue;
                if (Attribute.IsDefined(field, typeof(CompilerGeneratedAttribute))) continue;

                sb.Append(GetIndent(depth + 1));
                sb.Append($"{field.Name}: ");
                FormatInternal(sb, field.GetValue(obj), visited, depth + 1);
                sb.AppendLine(",");
            }

            // 优化：从缓存读取 Properties
            if (!PropertyCache.TryGetValue(type, out var props))
            {
                props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                PropertyCache[type] = props;
            }

            foreach (var prop in props)
            {
                if (ShouldIgnoreInternal(prop, prop.DeclaringType)) continue;

                if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                {
                    if (Attribute.IsDefined(prop, typeof(ObsoleteAttribute))) continue;

                    try
                    {
                        object val = prop.GetValue(obj);
                        sb.Append(GetIndent(depth + 1));
                        sb.Append($"{prop.Name}: ");
                        FormatInternal(sb, val, visited, depth + 1);
                        sb.AppendLine(",");
                    }
                    catch { }
                }
            }

            sb.Append(GetIndent(depth));
            sb.Append("}");
            return;
        }

        sb.Append(obj);
    }

 

    #endregion

    #endregion

    #region Public API

    public static string Colored(this string str, Color color)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{str}</color>";
    }

    #region Log (Editor & DevelopmentBuild)

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogException(string msg, Exception e, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        
        // 补上 Channel 前缀
        if (channel != LogChannel.None) msg = $"[{channel}] {msg}";
        
        Debug.LogError(msg, context);
        Debug.LogException(e, context);
    }

    // --- String Overloads ---

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string msg, Color? color = null, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        // 提前检查，虽然 Log_Internal 也会检查，但这里如果是复杂字符串插值调用，提前返回也好
        if (!IsChannelEnabled(channel)) return;
        Log_Internal(msg, color, LogLevel.Info, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(string msg, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        Log_Internal(msg, null, LogLevel.Warn, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogError(string msg, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        Log_Internal(msg, null, LogLevel.Error, context, channel);
    }

    // --- Object Overloads (Performance Critical: Check channel BEFORE Format) ---

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object msg, Color? color = null, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return; 
        Log_Internal(Format(msg), color, LogLevel.Info, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(object msg, Color? color = null, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        Log_Internal(Format(msg), null, LogLevel.Warn, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogError(object msg, Color? color = null, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        Log_Internal(Format(msg), null, LogLevel.Error, context, channel);
    }

    // --- Extension Methods ---

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(this object obj, string msg, Color? color = null, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        msg = msg.AddClassSuffix(obj, color);
        Log_Internal(msg, color, LogLevel.Info, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(this object obj, string msg, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        msg = msg.AddClassSuffix(obj);
        Log_Internal(msg, null, LogLevel.Warn, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogError(this object obj, string msg, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        msg = msg.AddClassSuffix(obj, null);
        Log_Internal(msg, null, LogLevel.Error, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(this object obj, object msg, Color? color = null, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        var msgStr = Format(msg);
        msgStr = msgStr.AddClassSuffix(obj, color);
        Log_Internal(msgStr, color, LogLevel.Info, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(this object obj, object msg, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        var msgStr = Format(msg);
        msgStr = msgStr.AddClassSuffix(obj);
        Log_Internal(msgStr, null, LogLevel.Warn, context, channel);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogError(this object obj, object msg, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
        if (!IsChannelEnabled(channel)) return;
        var msgStr = Format(msg);
        msgStr = msgStr.AddClassSuffix(obj);
        Log_Internal(msgStr, null, LogLevel.Error, context, channel);
    }

    #endregion

#region Code Timer / GC Monitor (支持聚合统计)

    /// <summary>
    /// 性能监控器。
    /// </summary>
    /// <param name="name">监控块名称</param>
    /// <param name="context">挂载对象</param>
    /// <param name="channel">频道</param>
    /// <param name="aggregateSecondsCallback">聚合打印时的回调函数</param>
    /// <param name="aggregateSeconds">聚合打印的时间间隔(秒)。如果为 0，则立即打印；如果为 1，则每秒汇总打印一次平均/最大值。</param>
    /// <param name="warnTimeThresholdMs">单次立即打印时的警告阈值</param>
    [HideInCallstack]
    public static PerfMonitor Monitor(string name,float aggregateSeconds = 0f, Action aggregateSecondsCallback=null, LogChannel channel = LogChannel.None,bool profile = true, float warnTimeThresholdMs = 0f, UnityEngine.Object context = null )
    {

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (aggregateSecondsCallback != null)
        {
            StatsCacheAction.TryAdd(name, aggregateSecondsCallback);
        }
#if  !UNITY_EDITOR
        profile = false; 
#endif
        return new PerfMonitor(name, context, channel, aggregateSeconds, warnTimeThresholdMs,profile);
#else
        return default; 
#endif
    }
    [HideInCallstack]
    public static PerfMonitor Monitor(this object mClass,string name,float aggregateSeconds = 0f, Action aggregateSecondsCallback=null,   LogChannel channel = LogChannel.None, bool profile = true,float warnTimeThresholdMs = 0f,UnityEngine.Object context = null )
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (aggregateSecondsCallback != null)
        {
            StatsCacheAction.TryAdd(name, aggregateSecondsCallback);
        }

        name += $" [T:{mClass.GetType()}] ";
        return new PerfMonitor(name, context, channel, aggregateSeconds, warnTimeThresholdMs,profile);
#else
        return default; 
#endif
    }
 

    // --- 聚合数据缓存类 ---
    private class MonitorStat
    {
        public int Count;
        public double TotalTimeMs;
        public double MaxTimeMs;
        public long TotalAlloc;
        public long MaxAlloc;
        public float NextLogTime;
        
        public void Reset(float nextTime)
        {
            Count = 0;
            TotalTimeMs = 0;
            MaxTimeMs = 0;
            TotalAlloc = 0;
            MaxAlloc = 0;
            NextLogTime = nextTime;
        }
    }

    // 使用字典缓存每个 Name 的统计数据
    private static readonly Dictionary<string, MonitorStat> StatsCache = new Dictionary<string, MonitorStat>();
    private static readonly Dictionary<string, Action> StatsCacheAction = new Dictionary<string, Action>();
    
public readonly struct PerfMonitor : IDisposable
    {
        private readonly string _name;
        private readonly UnityEngine.Object _context;
        private readonly long _startTime;
        private readonly long _startGC;
        private readonly LogChannel _logChannel;
        private readonly float _aggregateSeconds;
        private readonly float _warnTimeThresholdMs;
        private readonly bool _isActive;
        private readonly bool profile;

        public PerfMonitor(string name, UnityEngine.Object context, LogChannel logChannel, float aggregateSeconds, float warnTimeThresholdMs, bool profile)
        {
            _isActive = IsChannelEnabled(logChannel);
            if (!_isActive)
            {
                this = default;
                return;
            }
        
            _name = name;
            _context = context;
            _logChannel = logChannel;
            _aggregateSeconds = aggregateSeconds;
            _warnTimeThresholdMs = warnTimeThresholdMs;
            this.profile = profile;
            
    
            _startGC = GC.GetTotalMemory(false);
            if(profile)
                Profiler.BeginSample(name);
                
            _startTime = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (!_isActive) return;
            
            long endTime = Stopwatch.GetTimestamp();
            if(profile)
                Profiler.EndSample();
                
          
            long endGC =  GC.GetTotalMemory(false);
            
            double ms = (endTime - _startTime) * 1000.0 / Stopwatch.Frequency;
            long diff = endGC - _startGC;
          
            long alloc = diff > 0 ? diff : 0;
   
            if (_aggregateSeconds > 0.00001f)
            {
                // ========== 聚合统计模式 ==========
                ProcessAggregate(ms, alloc);
            }
            else
            {
                // ========== 立即打印模式 ==========
                ProcessImmediate(ms, diff);
            }
        }

        private void ProcessAggregate(double ms, long alloc)
        {
            if (!StatsCache.TryGetValue(_name, out var stat))
            {
             
                stat = new MonitorStat { NextLogTime = GetRealtimeSeconds() + _aggregateSeconds };
                StatsCache[_name] = stat;
            }

            stat.Count++;
            stat.TotalTimeMs += ms;
            if (ms > stat.MaxTimeMs) stat.MaxTimeMs = ms;

            stat.TotalAlloc += alloc;
            if (alloc > stat.MaxAlloc) stat.MaxAlloc = alloc;

            if (GetRealtimeSeconds() >= stat.NextLogTime)
            {
                double avgTime = stat.TotalTimeMs / stat.Count;
                long avgAlloc = stat.TotalAlloc / stat.Count;

                string timeStr = $"Time (Avg: {avgTime:F3}ms | Max: {stat.MaxTimeMs:F3}ms)";
                string gcStr = stat.MaxAlloc > 0 
                    ? $"Alloc (Avg: {avgAlloc}B | Max: {stat.MaxAlloc}B)" 
                    : "No Alloc";
                string callCountStr = $"Calls: {stat.Count}";

                string finalLog = $"[Monitor-Agg] {_name} -> {timeStr} | {gcStr} | {callCountStr}";
                Color logColor = stat.MaxAlloc > 0 ? DefaultMonitorGCColor : DefaultMonitorNoGCColor; 
                
                Log_Internal(finalLog, logColor, LogLevel.Info, _context, _logChannel);

                stat.Reset(GetRealtimeSeconds() + _aggregateSeconds);
                if (StatsCacheAction.TryGetValue(_name, out var action))
                {
                    action?.Invoke();
                }
            }
        }

        private void ProcessImmediate(double ms, long diff)
        {
            bool hasAlert = diff > 0 || diff < 0; 
            bool isOverTime = ms > _warnTimeThresholdMs;
            
            if (!hasAlert && !isOverTime) return; 
       
            string timeLog = ms < 1.0 ? $"{ms:F4} ms" : $"{ms:F2} ms";
            string gcLog;
            if (diff is >= 1024 and < 1024 * 1024)
            {
                gcLog = $"Alloc: +{diff/1204f:F2} KB";
            }
            else if(diff >= 1024 * 1024)
            {
                gcLog = $"Alloc: +{diff/(1024f*1024f):F2} MB";
            }
            else if (diff > 0)
            {
                gcLog = $"Alloc: +{diff} B";
            }
            else if (diff < 0) gcLog = "GC Triggered";
            else gcLog = "No Alloc";
        
            var finalLog = $"[Monitor] {_name} -> Time: {timeLog} | {gcLog}";
            Color logColor = hasAlert ? DefaultMonitorGCColor : DefaultMonitorNoGCColor;

            Log_Internal(finalLog, logColor,  LogLevel.Info, _context, _logChannel);
        }

        // 辅助方法：获取不受 Unity 主线程限制的时间
        private static float GetRealtimeSeconds()
        {
            return (float)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }
    }

    #endregion
    #endregion

}


public static class ReleaseLogTools
{
    private static readonly string LogDir = Path.Combine(Application.persistentDataPath, "ReleaseLogs");
    private static readonly string LogFile = Path.Combine(LogDir, $"release_{DateTime.Now:yyyy-MM-dd}.log");

    private static readonly Queue<string> _queue = new Queue<string>();
    private static readonly object Locker = new object();
    private static AutoResetEvent _signal = new AutoResetEvent(false);
    private static bool _running = true;

    private static readonly Stack<StringBuilder> _sbPool = new Stack<StringBuilder>();

    // 默认颜色
    private static readonly Color InfoColor = Color.white;
    private static readonly Color WarnColor = Color.yellow;
    private static readonly Color ErrorColor = Color.red;

    static ReleaseLogTools()
    {
        if (!Directory.Exists(LogDir))
            Directory.CreateDirectory(LogDir);

        Task.Run(() => BackgroundWriter());

        Application.quitting += () =>
        {
            _running = false;
            _signal.Set();
        };
    }

    #region Public API

    public static void LogException(string msg, Exception ex)
    {
        msg = $"{msg}\n{ex.Message}\n{ex.StackTrace}";
        EnqueueLine(msg, LogLevel.Error, null);
    }

    public static void Log(string msg)
    {
        EnqueueLine(msg, LogLevel.Info, null);
    }

    public static void LogWarning(string msg)
    {
        EnqueueLine(msg, LogLevel.Warn, null);
    }

    public static void LogError(string msg)
    {
        EnqueueLine(msg, LogLevel.Error, null);
    }

    public static void LogRelease(this object obj, string msg, LogLevel level = LogLevel.Info)
    {
        EnqueueLine($"[{obj.GetType().Name}] {msg}", level, null);
    }

    public static void LogRelease(this object obj, object msg, LogLevel level = LogLevel.Info)
    {
        string serialized = SerializeObject(msg);
        EnqueueLine($"[{obj.GetType().Name}] {serialized}", level, null);
    }

    #endregion

    #region 内部方法

    private static void EnqueueLine(string line, LogLevel level, Color? color)
    {
        if (level == LogLevel.Error)
        {
            string stack = Environment.StackTrace;
            line += "\n" + stack;
        }


        Color finalColor = color ?? GetDefaultColor(level);

        string finalLine = $"[{level}] [{DateTimeOffset.Now}] {line}";
      
        lock (Locker)
        {
            _queue.Enqueue(finalLine);
        }
        
#if UNITY_EDITOR
        string colorTag = $"<color=#{ColorUtility.ToHtmlStringRGB(finalColor)}>";
        finalLine = $"{colorTag}[{level}] [{DateTimeOffset.Now}] {line}</color>";
        switch (level)
        {
            case LogLevel.Info: Debug.Log(finalLine); break;
            case LogLevel.Warn: Debug.LogWarning(finalLine); break;
            case LogLevel.Error: Debug.LogError(finalLine); break;
        }
#endif

        _signal.Set();
    }

    private static Color GetDefaultColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Info => InfoColor,
            LogLevel.Warn => WarnColor,
            LogLevel.Error => ErrorColor,
            _ => InfoColor
        };
    }

    private static void BackgroundWriter()
    {
        while (_running)
        {
            _signal.WaitOne();
            List<string> batch = new List<string>();

            lock (Locker)
            {
                while (_queue.Count > 0)
                    batch.Add(_queue.Dequeue());
            }

            if (batch.Count > 0)
            {
                try
                {
                    File.AppendAllLines(LogFile, batch, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Debug.LogError($"ReleaseLogTools write error: {e}");
                }
            }
        }

        // 退出前 flush
        List<string> finalBatch = new List<string>();
        lock (Locker)
        {
            while (_queue.Count > 0)
                finalBatch.Add(_queue.Dequeue());
        }

        if (finalBatch.Count > 0)
        {
            try
            {
                File.AppendAllLines(LogFile, finalBatch, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    private static string SerializeObject(object obj)
    {
        if (obj == null) return "null";
        if (obj is string || obj.GetType().IsPrimitive) return obj.ToString();

        var sb = GetSB();
        try
        {
            SerializeInternal(sb, obj, 0);
            return sb.ToString();
        }
        finally
        {
            ReleaseSB(sb);
        }
    }

    private static void SerializeInternal(StringBuilder sb, object obj, int depth)
    {
        if (depth > 5)
        {
            sb.Append("...");
            return;
        }

        if (obj is IDictionary dict)
        {
            sb.Append("{");
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(", ");
                SerializeInternal(sb, entry.Key, depth + 1);
                sb.Append(": ");
                SerializeInternal(sb, entry.Value, depth + 1);
                first = false;
            }

            sb.Append("}");
        }
        else if (obj is IEnumerable enumerable && !(obj is string))
        {
            sb.Append("[");
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(", ");
                SerializeInternal(sb, item, depth + 1);
                first = false;
            }

            sb.Append("]");
        }
        else
        {
            sb.Append(obj.ToString());
        }
    }

    #endregion

    #region StringBuilder Pool

    private static StringBuilder GetSB()
    {
        lock (_sbPool)
        {
            if (_sbPool.Count > 0)
            {
                var sb = _sbPool.Pop();
                sb.Clear();
                return sb;
            }
        }

        return new StringBuilder(512);
    }

    private static void ReleaseSB(StringBuilder sb)
    {
        lock (_sbPool)
        {
            _sbPool.Push(sb);
        }
    }

    #endregion
}