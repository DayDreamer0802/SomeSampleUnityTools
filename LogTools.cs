using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
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
        string prefix = channel != LogChannel.None ? $"[{channel}] " : string.Empty;

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

    #region Serialize Object

    const int MaxDepth = 10;

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

        // 6. 类与结构体 [Obj]
        if (type.IsClass || type.IsValueType)
        {
            // 第一层不换行，嵌套层换行
            if (depth > 0) sb.AppendLine();
            
            sb.Append(GetIndent(depth));
            sb.Append($"{type.Name} {{"); // 这里就不加 [Obj] 了，类名就是最好的标识
            sb.AppendLine();

            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
            var fields = type.GetFields(flags);
            
            foreach (var field in fields)
            {
                if (ShouldIgnoreInternal(field,field.DeclaringType)) continue;
                if (Attribute.IsDefined(field, typeof(CompilerGeneratedAttribute))) continue;

                sb.Append(GetIndent(depth + 1));
                sb.Append($"{field.Name}: ");
                FormatInternal(sb, field.GetValue(obj), visited, depth + 1);
                sb.AppendLine(",");
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (ShouldIgnoreInternal(prop,prop.DeclaringType)) continue;

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

    #region Code Timer / GC Monitor

    [HideInCallstack]
    public static IDisposable Monitor(string name, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 优化：如果频道没开启，直接返回 dummy，不创建 Monitor 对象，不记录 GC/Time
        if (!IsChannelEnabled(channel)) return dummyScope;
        return new ScopeMonitor(name, context, channel);
#else
        return dummyScope;
#endif
    }

    [HideInCallstack]
    public static IDisposable Monitor(this object mClass, string name, UnityEngine.Object context = null, LogChannel channel = LogChannel.None)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!IsChannelEnabled(channel)) return dummyScope;
        name = $"[{mClass.GetType().Name}] {name}";
        // 修复：补上了 channel 参数，之前这里编译会报错
        return new ScopeMonitor(name, context, channel);
#else
        return dummyScope;
#endif
    }

    private sealed class DummyScope : IDisposable
    {
        public void Dispose() { }
    }
    
    private static readonly IDisposable dummyScope = new DummyScope();

    private sealed class ScopeMonitor : IDisposable
    {
        private readonly string _name;
        private readonly UnityEngine.Object _context;
        private readonly long _startTime;
        private readonly long _startGC;
        private readonly LogChannel _logChannel;

        public ScopeMonitor(string name, UnityEngine.Object context, LogChannel logChannel)
        {
            _name = name;
            _context = context;
            _logChannel = logChannel;
            _startGC = GC.GetTotalMemory(false);
            _startTime = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
       
            if (!IsChannelEnabled(_logChannel)) return;
            
            long endTime = Stopwatch.GetTimestamp();
            long endGC = GC.GetTotalMemory(false);

            double ms = (endTime - _startTime) * 1000.0 / Stopwatch.Frequency;
            string timeLog = ms < 1.0 ? $"{ms:F4} ms" : $"{ms:F2} ms";

            long diff = endGC - _startGC;
            string gcLog;
            bool hasAlert = false;

            if (diff > 0)
            {
                gcLog = $"Alloc: +{diff} B";
                hasAlert = true;
            }
            else if (diff < 0)
            {
                gcLog = "GC Triggered";
                hasAlert = true;
            }
            else
            {
                gcLog = "No Alloc";
            }

            var finalLog = $"[Monitor] {_name} -> Time: {timeLog} | {gcLog}";
            Color logColor = hasAlert ? DefaultMonitorGCColor : DefaultMonitorNoGCColor;

            Log_Internal(finalLog, logColor, LogLevel.Info, _context, _logChannel);
        }
    }

    #endregion

    #endregion
}