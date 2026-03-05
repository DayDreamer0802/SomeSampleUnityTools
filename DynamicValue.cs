using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public static class DynamicValueUtils
{
    public static bool IsSingleFlag(int v)
    {
        return v != 0 && (v & (v - 1)) == 0;
    }

    public static DynamicValue AsDyn(this IList iList) => DynamicValue.ConvertToDynamicValue(iList);
    public static DynamicValue AsDyn(this IDictionary iDic) => DynamicValue.ConvertToDynamicValue(iDic);
    public static DynamicValue AsDyn(this object obj) => DynamicValue.ConvertToDynamicValue(obj);
    public static DynamicValue AsDyn(this Delegate de) => DynamicValue.ConvertToDynamicValue(de);
    public static DynamicValue AsDyn(this Enum e) => DynamicValue.ConvertToDynamicValue(e);

    public static bool IsIntValueType(this DynamicValue dynValue)
    {
        return dynValue.type is (
            DynamicValue.DynamicValueType.Int or
            DynamicValue.DynamicValueType.Bool or
            DynamicValue.DynamicValueType.Short or
            DynamicValue.DynamicValueType.UInt or
            DynamicValue.DynamicValueType.LayerMask or
            DynamicValue.DynamicValueType.Enum
            );
    }


    public static int ParseOptimized(ReadOnlySpan<char> str, int defaultValue = int.MaxValue)
    {
        if (str.IsEmpty) return defaultValue;

        uint result = 0;
        int start = 0;
        bool negative = false;

        char first = str[0];
        if (first == '-')
        {
            negative = true;
            start = 1;
        }
        else if (first == '+')
        {
            start = 1;
        }

        if (start >= str.Length) return defaultValue;

        uint maxDiv10 = uint.MaxValue / 10;

        for (int i = start; i < str.Length; i++)
        {
            char c = str[i];
            uint digit = (uint)(c - '0');
            if (digit > 9) return defaultValue;
            if (result > maxDiv10 || (result == maxDiv10 && digit > uint.MaxValue % 10)) return defaultValue;
            result = result * 10 + digit;
        }

        if (negative)
        {
            if (result > (uint)int.MaxValue + 1) return defaultValue;
            return -(int)result;
        }
        else
        {
            if (result > int.MaxValue) return defaultValue;
            return (int)result;
        }
    }
}

/// <summary>
/// 参考C++的Union联合体的IL2CPP下 安全的动态值变体结构 (Variant Struct)。
/// 用于解决 AOT 环境下 dynamic （JIT）关键字失效的问题，并优化基础类型的装箱 GC 开销。
/// 警告：结构体体积较大 (16 Bytes)，避免在 Update 高频循环中值传递。
/// 建议场景：配置表解析、UI 数据透传、序列化中间层。
/// A type-safe dynamic variant structure (Variant Struct) designed for IL2CPP, 
/// mimicking the behavior of a C++ Union.
/// 
/// Purpose: 
/// 1. Addresses the failure of the 'dynamic' keyword (JIT-dependent) in AOT environments.
/// 2. Optimizes GC overhead by avoiding boxing of primitive types.
/// 
/// WARNING:
/// This struct has a relatively large memory footprint (16 Bytes). 
/// Avoid pass-by-value in high-frequency loops (e.g., Update).
/// 
/// Recommended Use Cases: 
/// Configuration parsing, UI data pass-through, and serialization middleware.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct DynamicValue : IEquatable<DynamicValue>
{
    #region 字段/属性 Fields/Properties

    public enum DynamicValueType
    {
        Null,
        Int,
        Float,
        Double, // Cold Data (Boxed)
        Bool,
        Long, // Cold Data (Boxed)
        String,
        IDictionary,
        IList,
        Enum,
        Delegate,
        Obj,
        Short, // Mapped to Int
        UInt, // Mapped to Int
        LayerMask // Mapped to Int
    }

    [FieldOffset(0)] public readonly object Value;
    [FieldOffset(8)] private readonly int _i;
    [FieldOffset(8)] private readonly float _f;
    [FieldOffset(12)] public readonly DynamicValueType type;
    private int i_raw => _i;
    private float f_raw => _f;
    private bool b_raw => _i != 0;

    private long l_safe
    {
        get
        {
            if (type == DynamicValueType.Long) return (long)Value;
            if (type == DynamicValueType.UInt) return (uint)_i;
            return _i;
        }
    }

    private double d_safe => type == DynamicValueType.Double ? (double)Value : _f;

    #endregion

    #region 构造函数/Constructors

    private DynamicValue(int _val)
    {
        Value = null;
        _f = 0;
        _i = _val;
        type = DynamicValueType.Int;
    }

    private DynamicValue(float _val)
    {
        Value = null;
        _i = 0;
        _f = _val;
        type = DynamicValueType.Float;
    }

    private DynamicValue(bool _val)
    {
        Value = null;
        _f = 0;
        _i = _val ? 1 : 0;
        type = DynamicValueType.Bool;
    }

    private DynamicValue(short _val)
    {
        Value = null;
        _f = 0;
        _i = _val;
        type = DynamicValueType.Short;
    }

    private DynamicValue(uint _val)
    {
        Value = null;
        _f = 0;
        _i = (int)_val;
        type = DynamicValueType.UInt;
    }

    private DynamicValue(LayerMask _val)
    {
        Value = null;
        _f = 0;
        _i = _val.value;
        type = DynamicValueType.LayerMask;
    }

    // === 冷路径 (Cold Path) - Boxed ===
    private DynamicValue(long _val)
    {
        Value = _val;
        _i = _val < int.MaxValue ? (int)_val : 0;
        _f = 0;
        type = DynamicValueType.Long;
    }

    private DynamicValue(double _val)
    {
        Value = _val;
        _i = 0;
        _f = 0;
        type = DynamicValueType.Double;
    }

    // === 引用类型 ===
    private DynamicValue(string str)
    {
        Value = str;
        _i = 0;
        _f = 0;
        type = DynamicValueType.String;
    }

    private DynamicValue(IList val)
    {
        Value = val;
        _i = 0;
        _f = 0;
        type = DynamicValueType.IList;
    }

    private DynamicValue(IDictionary val)
    {
        Value = val;
        _i = 0;
        _f = 0;
        type = DynamicValueType.IDictionary;
    }

    private DynamicValue(Enum val)
    {
        Value = val;
        _f = 0;
        _i = Convert.ToInt32(val);
        type = DynamicValueType.Enum;
    }

    private DynamicValue(Delegate val)
    {
        Value = val;
        _i = 0;
        _f = 0;
        type = DynamicValueType.Delegate;
    }

    private DynamicValue(object val)
    {
        Value = val;
        _i = 0;
        _f = 0;
        type = DynamicValueType.Obj;
    }

    private DynamicValue(DBNull val)
    {
        Value = null;
        _i = 0;
        _f = 0;
        type = DynamicValueType.Null;
    }

    #endregion

    #region 隐式转换/Implicit Operators
    
    public static implicit operator DynamicValue(int v) => new(v);
    public static implicit operator DynamicValue(float v) => new(v);
    public static implicit operator DynamicValue(bool v) => new(v);
    public static implicit operator DynamicValue(short v) => new(v);
    public static implicit operator DynamicValue(uint v) => new(v);
    public static implicit operator DynamicValue(LayerMask v) => new(v);
    public static implicit operator DynamicValue(long v) => new(v); // Boxed
    public static implicit operator DynamicValue(double v) => new(v); // Boxed
    public static implicit operator DynamicValue(string v) => new(v);
    public static implicit operator DynamicValue(DBNull v) => new(v);

    public static implicit operator Vector2(DynamicValue v)
    {
        if (v.Value is Vector2 vec) return vec;


        if (v.Value is IList list)
        {
            if (list.Count >= 2)
            {
                float x = ParseFloat(list[0]);
                float y = ParseFloat(list[1]);
                return new Vector2(x, y);
            }
#if UNITY_EDITOR
            Debug.LogError(list.Count + "the count of IList is less than 2");

#endif
            return Vector2.zero;
        }
#if UNITY_EDITOR
        if (v.type != DynamicValueType.Null || v.Value != null)
            Debug.LogError($"[DynamicValue] Value {v.Value?.GetType()} cannot cast to Vector2.");
#endif
        // 默认值
        return Vector2.zero;
    }

    public static implicit operator Vector3(DynamicValue v)
    {
        if (v.Value is Vector3 vec) return vec;

        if (v.Value is IList list)
        {
            if (list.Count >= 3)
            {
                return new Vector3(
                    ParseFloat(list[0]),
                    ParseFloat(list[1]),
                    ParseFloat(list[2]));
            }
#if UNITY_EDITOR
            Debug.LogError($"[DynamicValue] To Vector3 failed. List count {list.Count} < 3.");
#endif
            return Vector3.zero;
        }
#if UNITY_EDITOR
        if (v.type != DynamicValueType.Null || v.Value != null)
            Debug.LogError($"[DynamicValue] Value {v.Value?.GetType()} cannot cast to Vector3.");
#endif
        return Vector3.zero;
    }

    public static implicit operator Quaternion(DynamicValue v)
    {
        if (v.Value is Quaternion quat) return quat;

        if (v.Value is IList list)
        {
            if (list.Count >= 4)
            {
                return new Quaternion(
                    ParseFloat(list[0]),
                    ParseFloat(list[1]),
                    ParseFloat(list[2]),
                    ParseFloat(list[3]));
            }


            if (list.Count == 3)
            {
#if UNITY_EDITOR
                Debug.Log($"Trying to Euler the list");
#endif

                return Quaternion.Euler(ParseFloat(list[0]),
                    ParseFloat(list[1]),
                    ParseFloat(list[2]));
            }


#if UNITY_EDITOR
            Debug.LogError($"[DynamicValue] To Quaternion failed. List count {list.Count} < 3.");
#endif
            return Quaternion.identity; // 旋转通常返回 identity 而不是 zero
        }
#if UNITY_EDITOR
        if (v.type != DynamicValueType.Null || v.Value != null)
            Debug.LogError($"[DynamicValue] Value {v.Value?.GetType()} cannot cast to  Quaternion.");
#endif
        return Quaternion.identity;
    }

    public static implicit operator Vector2Int(DynamicValue v)
    {
        if (v.Value is Vector2Int v2i) return v2i;

        if (v.Value is IList list)
        {
            if (list.Count >= 2)
            {
                return new Vector2Int(
                    ParseInt(list[0]),
                    ParseInt(list[1]));
            }
#if UNITY_EDITOR
            Debug.LogError($"[DynamicValue] To Vector2Int failed. List count {list.Count} < 2.");
#endif
            return Vector2Int.zero;
        }
#if UNITY_EDITOR
        if (v.type != DynamicValueType.Null || v.Value != null)
            Debug.LogError($"[DynamicValue] Value {v.Value?.GetType()} cannot cast to Vector2Int.");
#endif
        return Vector2Int.zero;
    }

    public static implicit operator Vector3Int(DynamicValue v)
    {
        if (v.Value is Vector3Int v3i) return v3i;

        if (v.Value is IList list)
        {
            if (list.Count >= 3)
            {
                return new Vector3Int(
                    ParseInt(list[0]),
                    ParseInt(list[1]),
                    ParseInt(list[2]));
            }
#if UNITY_EDITOR
            Debug.LogError($"[DynamicValue] To Vector3Int failed. List count {list.Count} < 3.");
#endif
            return Vector3Int.zero;
        }
#if UNITY_EDITOR
        if (v.type != DynamicValueType.Null || v.Value != null)
            Debug.LogError($"[DynamicValue] Value {v.Value?.GetType()} cannot cast to Vector3Int.");
#endif
        return Vector3Int.zero;
    }

    public static implicit operator Rect(DynamicValue v)
    {
        if (v.Value is Rect r) return r;

        if (v.Value is IList list)
        {
            if (list.Count >= 4)
            {
                // Rect(x, y, width, height)
                return new Rect(
                    ParseFloat(list[0]),
                    ParseFloat(list[1]),
                    ParseFloat(list[2]),
                    ParseFloat(list[3]));
            }
#if UNITY_EDITOR
            Debug.LogError($"[DynamicValue] To Rect failed. List count {list.Count} < 4.");
#endif
            return Rect.zero;
        }
#if UNITY_EDITOR
        if (v.type != DynamicValueType.Null || v.Value != null)
            Debug.LogError($"[DynamicValue] Value {v.Value?.GetType()} cannot cast to Rect.");
#endif
        return Rect.zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ParseFloat(object o)
    {
        if (o is DynamicValue dv) return dv;
        if (o is float f) return f;
        if (o is int i) return i;
        if (o is double d) return (float)d;
        if (o is string s && float.TryParse(s, out float res)) return res;
        return 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseInt(object o)
    {
        if (o is DynamicValue dv) return (int)dv;
        if (o is int i) return i;
        if (o is long l) return (int)l; // 兼容 long
        if (o is float f) return (int)f; // 兼容 float (截断)
        if (o is double d) return (int)d;
        if (o is string s && int.TryParse(s, out int res)) return res;
        return 0;
    }

    public static implicit operator int(DynamicValue v)
    {
        if (v.type == DynamicValueType.Int) return v._i;
        if (v.type == DynamicValueType.Short) return v._i;
        if (v.type == DynamicValueType.LayerMask) return v._i;
        if (v.type == DynamicValueType.UInt) return v._i;
        if (v.type == DynamicValueType.Long) return (int)(long)v.Value;
        if (v is { type: DynamicValueType.String, Value: string str }) return DynamicValueUtils.ParseOptimized(str);
        return 0;
    }

    public static implicit operator float(DynamicValue v) =>
        v.type == DynamicValueType.Float ? v._f :
        v.type == DynamicValueType.Int ? v._i :
        v.type == DynamicValueType.Double ? (float)(double)v.Value : v.ParseFloatOrZero();

    private float ParseFloatOrZero() => type == DynamicValueType.String &&
                                        float.TryParse((string)Value, NumberStyles.Any, CultureInfo.InvariantCulture,
                                            out var f)
        ? f
        : 0f;

    public static implicit operator bool(DynamicValue v) => v.b_raw;
    public static implicit operator short(DynamicValue v) => (short)v._i;
    public static implicit operator uint(DynamicValue v) => (uint)v._i;
    public static implicit operator LayerMask(DynamicValue v) => v._i;
    public static implicit operator long(DynamicValue v) => v.l_safe;
    public static implicit operator double(DynamicValue v) => v.d_safe;

    public static implicit operator string(DynamicValue v)
    {
        if (v.type == DynamicValueType.String) return v.Value as string;
        if (v.type == DynamicValueType.Int) return v._i.ToString();
        if (v.type == DynamicValueType.Float) return v._f.ToString(CultureInfo.InvariantCulture);
        if (v.type == DynamicValueType.Long || v.type == DynamicValueType.Double) return v.Value.ToString();
        return v.Value?.ToString();
    }

    #endregion

    #region 类型转换/Type Conversions

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T As<T>()
    {
        if (typeof(T) == typeof(int)) return (T)(object)_i;
        if (typeof(T) == typeof(float)) return (T)(object)_f;
        if (typeof(T) == typeof(bool)) return (T)(object)b_raw;
        if (typeof(T) == typeof(short)) return (T)(object)(short)_i;
        if (typeof(T) == typeof(uint)) return (T)(object)(uint)_i;
        if (typeof(T) == typeof(LayerMask)) return (T)(object)(LayerMask)_i;

        if (typeof(T) == typeof(long)) return (T)(object)l_safe;
        if (typeof(T) == typeof(double)) return (T)(object)d_safe;
        if (typeof(T) == typeof(string)) return (T)Value;

        if (typeof(T).IsEnum)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Use AsEnum<T> instead.");
#endif
            return AsEnum<T>();
        }

        if (type == DynamicValueType.IList && Value is IList)
        {
            throw new ArgumentException($"Use AsList<T> instead.");
        }

        if (type == DynamicValueType.IDictionary && Value is IDictionary)
            throw new ArgumentException($"Use AsDictionary<T> instead.");

        return (T)Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T AsEnum<T>()
    {
        if (type == DynamicValueType.Enum || type == DynamicValueType.Int)
            return (T)Enum.ToObject(typeof(T), _i);
        throw new InvalidCastException($"Cannot cast {Value?.GetType()} to Enum {typeof(T)}");
    }

    public override string ToString()
    {
        return type switch
        {
            DynamicValueType.Int => _i.ToString(),
            DynamicValueType.Float => _f.ToString(CultureInfo.InvariantCulture),
            DynamicValueType.Bool => b_raw.ToString(),
            DynamicValueType.Short => ((short)_i).ToString(),
            DynamicValueType.UInt => ((uint)_i).ToString(),
            DynamicValueType.LayerMask => ((LayerMask)_i).ToString(),
            DynamicValueType.Long => Value.ToString(), // Boxed Value
            DynamicValueType.Double => ((double)Value).ToString(CultureInfo.InvariantCulture), // Boxed Value
            _ => Value?.ToString() ?? "null"
        };
    }

    public object ToObject()
    {
        return type switch
        {
            DynamicValueType.Int => _i,
            DynamicValueType.Float => _f,
            DynamicValueType.Bool => b_raw,
            DynamicValueType.Short => (short)_i,
            DynamicValueType.UInt => (uint)_i,
            DynamicValueType.LayerMask => (LayerMask)_i,
            DynamicValueType.Long => Value, // Already Boxed
            DynamicValueType.Double => Value, // Already Boxed
            DynamicValueType.Enum => _i,
            _ => Value
        };
    }

    #endregion

    #region 转换/ConvertToDynamic

    public static DynamicValue ConvertToDynamicValue(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int parsedInt = DynamicValueUtils.ParseOptimized(s);
        if (parsedInt != int.MaxValue) return parsedInt; // 字符串转数字存储
        if (s.Length <= 5)
        {
            if (s == "true" || s == "True") return true;
            if (s == "false" || s == "False") return false;
        }

        return s;
    }

    public static DynamicValue ConvertToDynamicValue(float v) => new(v);
    public static DynamicValue ConvertToDynamicValue(bool v) => new(v);
    public static DynamicValue ConvertToDynamicValue(short v) => new(v);
    public static DynamicValue ConvertToDynamicValue(uint v) => new(v);
    public static DynamicValue ConvertToDynamicValue(LayerMask v) => new(v);

    public static DynamicValue ConvertToDynamicValue(object obj)
    {
        if (obj == null) return new DynamicValue((DBNull)null);
        if (obj is int i) return new(i);
        if (obj is float f) return new(f);
        if (obj is bool b) return new(b);
        if (obj is string s) return new(s);
        if (obj is short sh) return new(sh);
        if (obj is uint u) return new(u);
        if (obj is LayerMask lm) return new(lm);
        if (obj is IList ilist) return new DynamicValue(ilist);
        if (obj is IDictionary dict) return new DynamicValue(dict);
        if (obj is long l)
        {
            if (l is <= int.MaxValue and >= int.MinValue) return (int)l;
            return new(l);
        }

        if (obj is double d)
        {
            if (d is <= float.MaxValue and >= float.MinValue) return (float)d;
            return new(d);
        }

        if (obj is Enum e) return new DynamicValue(e);
        if (obj is Delegate del) return new DynamicValue(del);
        return new DynamicValue(obj);
    }

    #endregion

    #region 比较方法/Comparison Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CompareStr(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2)
    {
        if (s1.Length != s2.Length) return false;
        for (int i = 0; i < s1.Length; i++)
        {
            if (s1[i] != s2[i]) return false;
        }

        return true;
    }

    public static bool operator ==(DynamicValue a, DynamicValue b)
    {
        if (a.IsIntValueType())
        {
            if (b.IsIntValueType())
                return a._i == b._i;
            return false;
        }

        switch (a.type)
        {
            case DynamicValueType.String:
                //不做字符串的int和int比较 
                if (b.type is DynamicValueType.String)
                {
                    if (ReferenceEquals(a.Value, b.Value)) return true;
                    if (a.Value is string s && b.Value is string s2)
                    {
                        return CompareStr(s, s2);
                    }
                }

                return false;
            case DynamicValueType.Float:
                if (b.type is DynamicValueType.Float)
                {
                    return Mathf.Approximately(a._f, b._f);
                }

                return false;
            case DynamicValueType.Double:
                if (b.type is DynamicValueType.Double)
                    return Math.Abs(a.d_safe - b.d_safe) < Double.Epsilon;
                return false;
            case DynamicValueType.Long:
                if (b.IsIntValueType())
                    return a.l_safe == b._i;
                if (b.type is DynamicValueType.Long)
                    return (long)a.Value == (long)b.Value;
                return false;
            case DynamicValueType.IList:
                if (a.Value is IList list1 && b.Value is IList list2)
                {
                    return CompareLists(list1, list2);
                }

                return false;
            case DynamicValueType.IDictionary:
                if (a.Value is IDictionary dict && b.Value is IDictionary dict2)
                {
                    return CompareDictionaries(dict, dict2);
                }

                return false;
            default:
                return a.Value == b.Value;
        }
    }

    public static bool operator !=(DynamicValue a, DynamicValue b)
    {
        return !(a == b);
    }

    public static bool operator ==(object a, DynamicValue b)
    {
        return b == a;
    }

    public static bool operator !=(object a, DynamicValue b)
    {
        return !(b == a);
    }

    public static bool operator ==(DynamicValue a, DBNull b)
    {
        return a.type == DynamicValueType.Null ||
               ((a.type is DynamicValueType.Obj or DynamicValueType.Enum or DynamicValueType.String
                    or DynamicValueType.IDictionary
                    or DynamicValueType.IList) &&
                a.Value == b);
    }

    public static bool operator !=(DynamicValue a, DBNull b)
    {
        return !(a == b);
    }

    public static bool operator ==(DynamicValue a, object b)
    {
        if (b is DynamicValue d)
        {
            return a == d;
        }

        switch (a.type)
        {
            case DynamicValueType.Null:
                return b == null || b is DBNull;

            case DynamicValueType.Int:
                return CompareInt(a, b);

            case DynamicValueType.Long:
                return CompareLong(a, b);

            case DynamicValueType.Float:
                return CompareFloat(a, b);

            case DynamicValueType.Double:
                return CompareDouble(a, b);

            case DynamicValueType.Bool:
                return CompareBool(a, b);

            case DynamicValueType.String:
                return CompareString(a, b);

            case DynamicValueType.Enum:
                return CompareEnum(a, b);

            case DynamicValueType.IList:
                return CompareIList(a, b);

            case DynamicValueType.IDictionary:
                return CompareIDictionary(a, b);
            case DynamicValueType.Obj:
                return CompareObject(a, b);

            default:
                return Equals(a.Value, b);
        }
    }

    private static bool CompareString(DynamicValue a, object b)
    {
        if (ReferenceEquals(a.Value, b))
            return true;
        if (a.Value is string aStr && b is string bStr)
        {
            return CompareStr(aStr, bStr);
        }

        return false;
    }

    private static bool CompareEnum(DynamicValue a, object b)
    {
        if (b is Enum e && a.type == DynamicValueType.Enum)
        {
            return a._i == Convert.ToInt32(e);
        }

        if (b is int _i)
            return a._i == _i;
        return false;
    }

    public static bool operator !=(DynamicValue a, object b)
    {
        return !(a == b);
    }

    private static bool CompareIList(DynamicValue a, object b)
    {
        var list = (IList)a.Value;
        if (list is not null)
        {
            return b switch
            {
                IList bList => CompareLists(list, bList),
                IEnumerable enumerable => CompareListWithEnumerable(list, enumerable),
                _ => false
            };
        }

        return b is null;
    }

    private static bool CompareIDictionary(DynamicValue a, object b)
    {
        var dict = (IDictionary)a.Value;
        if (dict is not null)
        {
            return b switch
            {
                IDictionary bDict => CompareDictionaries(dict, bDict),
                _ => false
            };
        }

        return b is null;
    }

    private static bool CompareObject(DynamicValue a, object b)
    {
        return Equals(a.Value, b);
    }

    private static bool CompareLists(IList listA, IList listB)
    {
        if (listA.Count != listB.Count)
            return false;
        if (ReferenceEquals(listA, listB))
            return true;

        for (int i = 0; i < listA.Count; i++)
        {
            var itemA = listA[i];
            var itemB = listB[i];
            if (ReferenceEquals(itemA, itemB))
                continue;
            if (itemA is DynamicValue a && itemB is DynamicValue b)
            {
                if (!(a == b))
                    return false;
            }

            if (itemA is DynamicValue dynA)
            {
                if (!(dynA == itemB))
                    return false;
            }
            else if (itemB is DynamicValue dynB)
            {
                if (!(dynB == itemA))
                    return false;
            }
            else if (!Equals(itemA, itemB))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompareListWithEnumerable(IList list, IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        int index = 0;
        while (enumerator.MoveNext())
        {
            if (index >= list.Count)
                return false;

            var itemA = list[index];
            var itemB = enumerator.Current;

            if (itemA is DynamicValue dynA)
            {
                if (!(dynA == itemB))
                    return false;
            }
            else if (!Equals(itemA, itemB))
            {
                return false;
            }

            index++;
        }

        using var enumerator1 = enumerator as IDisposable;
        return index == list.Count;
    }

    private static bool CompareDictionaries(IDictionary dictA, IDictionary dictB)
    {
        if (ReferenceEquals(dictA, dictB))
            return true;
        if (dictA.Count != dictB.Count)
            return false;


        foreach (var key in dictA.Keys)
        {
            if (!dictB.Contains(key))
                return false;
            var valueA = dictA[key];
            var valueB = dictB[key];
            if (ReferenceEquals(valueA, valueB))
                continue;
            if (valueA is DynamicValue a && valueB is DynamicValue b)
            {
                if (!(a == b))
                    return false;
            }

            if (valueA is DynamicValue dynA)
            {
                if (!(dynA == valueB))
                    return false;
            }
            else if (valueB is DynamicValue dynB)
            {
                if (!(dynB == valueA))
                    return false;
            }
            else if (!Equals(valueA, valueB))
            {
                return false;
            }
        }

        return true;
    }


    public override bool Equals(object obj)
    {
        return base.Equals(obj);
    }

    public bool Equals(DynamicValue other)
    {
        if ((this.IsIntValueType() || type is DynamicValueType.Long) &&
            (other.IsIntValueType() || type is DynamicValueType.Long))
            return this.l_safe == other.l_safe;
        if (type == DynamicValueType.String && other.type == DynamicValueType.String)
            return string.Equals((string)Value, (string)other.Value, StringComparison.Ordinal);

        return Value.Equals(other.Value);
    }


    public override int GetHashCode()
    {
        if (this.IsIntValueType() || this.type is DynamicValueType.Long)
            return l_safe.GetHashCode();
        return Value?.GetHashCode() ?? 0;
    }


    // 私有比较辅助
    private static bool CompareInt(int a, object b) => b switch
    {
        int i => a == i, long l => a == l, float f => Math.Abs(a - f) < float.Epsilon, short s => a == s, _ => false
    };

    private static bool CompareUInt(uint a, object b) =>
        b switch { uint u => a == u, int i => i >= 0 && a == (uint)i, _ => false };

    private static bool CompareLong(long a, object b) => b switch { long l => a == l, int i => a == i, _ => false };

    private static bool CompareFloat(float a, object b) => b switch
    {
        float f => Math.Abs(a - f) < float.Epsilon, int i => Math.Abs(a - i) < float.Epsilon,
        double d => Math.Abs(a - d) < float.Epsilon, _ => false
    };

    private static bool CompareDouble(double a, object b) => b switch
    {
        double d => Math.Abs(a - d) < double.Epsilon, float f => Math.Abs(a - f) < double.Epsilon, _ => false
    };

    private static bool CompareBool(bool a, object b) => b is bool v && a == v;

    #endregion

    #region 集合方法/Collection

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public List<T> AsList<T>(bool nullable = true)
    {
        if (type == DynamicValueType.IList && Value is IList)
        {
            if (Value is List<T> listT) return listT;
            if (Value is IList listVal) return listVal.Cast<T>().ToList();
        }

        if (nullable) return null;
        throw new InvalidCastException($"Cannot cast {type} to List<{typeof(T).Name}>");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Dictionary<string, DynamicValue> AsDic(bool nullable = true)
    {
        return Value is Dictionary<string, DynamicValue> dic
            ? dic
            : (nullable ? null : new Dictionary<string, DynamicValue>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Dictionary<K, V> AsDictionary<K, V>(bool nullable = true)
    {
        if (Value is Dictionary<K, V> targetDict)
        {
            return targetDict;
        }

        if (type == DynamicValueType.Null)
            return nullable ? null : new Dictionary<K, V>();

        // 2. 类型检查
        if (type != DynamicValueType.IDictionary)
        {
            if (nullable) return null;
            throw new InvalidCastException(
                $"Cannot cast DynamicValueType.{type} to Dictionary<{typeof(K).Name}, {typeof(V).Name}>. Value: {FormatValueForError(Value)}");
        }


        if (Value is IDictionary dict)
        {
            var result = new Dictionary<K, V>(dict.Count);
            int index = 0;

            foreach (DictionaryEntry entry in dict)
            {
                try
                {
                    K key;
                    try
                    {
                        key = ConvertToDynamicValue(entry.Key).As<K>();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidCastException(
                            $"[AsDictionary] Failed to convert KEY at index {index}. " +
                            $"Source Type: {entry.Key?.GetType().Name ?? "null"}, " +
                            $"Target Type: {typeof(K).Name}. " +
                            $"Raw Value: {FormatValueForError(entry.Key)}. " +
                            $"Error: {ex.Message}");
                    }


                    V value;
                    try
                    {
                        value = ConvertToDynamicValue(entry.Value).As<V>();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidCastException(
                            $"[AsDictionary] Failed to convert VALUE for key '{FormatValueForError(entry.Key)}'. " +
                            $"Source Type: {entry.Value?.GetType()?.Name ?? "null"}, " +
                            $"Target Type: {typeof(V).Name}. " +
                            $"Raw Value: {FormatValueForError(entry.Value)}. " +
                            $"Error: {ex.Message}");
                    }


                    if (result.ContainsKey(key))
                    {
                        Debug.LogError($"[AsDictionary] Duplicate key ignored: {FormatValueForError(key)}");
                    }
                    else
                    {
                        result.Add(key, value);
                    }

                    index++;
                }
                catch (InvalidCastException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidCastException(
                        $"[AsDictionary] Unexpected error at index {index}. " +
                        $"Key: {FormatValueForError(entry.Key)}, " +
                        $"Value: {FormatValueForError(entry.Value)}. " +
                        $"Error: {ex.Message}");
                }
            }

            return result;
        }

        // 5. 无法转换
        throw new InvalidCastException(
            $"Cannot cast underlying type {Value?.GetType().Name} to Dictionary<{typeof(K).Name}, {typeof(V).Name}>");
    }


    private string FormatValueForError(object value)
    {
        if (value == null) return "null";
        try
        {
            string str = value.ToString();
            return str.Length > 50 ? str.Substring(0, 50) + "..." : str;
        }
        catch
        {
            return $"[{value.GetType().Name}]";
        }
    }

    public int Count
    {
        get
        {
            if (type == DynamicValueType.IDictionary && Value is IDictionary dict) return dict.Count;
            if (type == DynamicValueType.IList && Value is IList list) return list.Count;
            return Value is string str ? str.Length : 0;
        }
    }

    #endregion

    #region Collection Operations

    // Note: Iterators (IEnumerable) are intentionally not implemented.
    // DynamicValue should be cast to a concrete collection type before processing
    // to avoid the performance overhead and complexity of generic comparisons (e.g., Contains).
    //不实现迭代器的根本原因是不应该把DynamicValue作为集合进行操作 而是转为确定的类型
    //否则Contains比较非常麻烦且性能并不好
    public DynamicValue this[int key]
    {
        get
        {
            if (type == DynamicValueType.IList)
            {
                if (Value is List<DynamicValue> list)
                    return list[key];
                if (Value is List<int> list0)
                    return list0[key];
                if (Value is List<float> list1)
                    return list1[key];
                if (Value is List<bool> list2)
                    return list2[key];
                if (Value is List<string> list3)
                    return list3[key];
                if (Value is IList list4)
                    return ConvertToDynamicValue(list4[key]);
            }

            throw new InvalidOperationException($"Cannot index {type}");
        }
    }

    public DynamicValue this[DynamicValue key]
    {
        get
        {
            if (type == DynamicValueType.IList)
            {
                if (key.type is DynamicValueType.Int)
                    return this[(int)key];
                if (key.type is DynamicValueType.String && int.TryParse(key, out _))
                    return this[(int)key];
            }

            if (type == DynamicValueType.IDictionary)
            {
                if (Value is Dictionary<string, DynamicValue> dict && key.Value is string str)
                    return dict[str];
                if (Value is Dictionary<string, object> dictobj && key.Value is string strobj)
                    return new(dictobj[strobj]);
                if (Value is Dictionary<DynamicValue, Dictionary<string, DynamicValue>> dic)
                    return ConvertToDynamicValue(dic[key]);
                if (Value is Dictionary<DynamicValue, DynamicValue> dict1)
                    return dict1[key];
            }

            throw new InvalidOperationException($"Can not use this[] ,Value is {Value?.GetType()}");
        }
    }

    public DynamicValue ElementAt(int index)
    {
        if (type == DynamicValueType.IList && Value is IList list)
        {
            return ConvertToDynamicValue(list[index]);
        }

        if (type == DynamicValueType.IDictionary)
        {
            if (Value is Dictionary<string, DynamicValue> dict)
                return dict.ElementAt(index).Value;
            
            if (Value is IDictionary dic)
            {
                var enumerator = dic.GetEnumerator();
                for (int i = 0; i <= index; i++)
                {
                    if (!enumerator.MoveNext()) throw new ArgumentOutOfRangeException();
                }

                using var enumerator1 = enumerator as IDisposable;
                return ConvertToDynamicValue(enumerator.Value);
            }
        }

        throw new InvalidOperationException($"Cannot use ElementAt on {type}");
    }

    #endregion

    #region 位运算与 Flag 支持 (Bitwise Operations)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DynamicValue AddFlag(DynamicValue flag)
    {
        if (this.type == DynamicValueType.LayerMask || flag.type == DynamicValueType.LayerMask)
            return new DynamicValue((LayerMask)(this._i | flag._i)); // 保持 LayerMask 类型

        return (int)(this._i | flag._i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DynamicValue RemoveFlag(DynamicValue flag)
    {
        int res = this._i & ~flag._i;

        if (this.type == DynamicValueType.LayerMask)
            return new DynamicValue((LayerMask)res);

        return res;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasAllFlag(DynamicValue flag)
    {
        if (this.type == DynamicValueType.Long || flag.type == DynamicValueType.Long)
        {
            long selfL = this;
            long flagL = flag;
            return (selfL & flagL) == flagL;
        }

        if (flag._i == 0)
            return true;
        return (this._i & flag._i) == flag._i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasAnyFlag(DynamicValue flag)
    {
        if (this.type == DynamicValueType.Long || flag.type == DynamicValueType.Long)
        {
            long selfL = (long)this;
            long flagL = (long)flag;
            return (selfL & flagL) != 0;
        }

        if (flag._i == 0)
            return false;
        return (this._i & flag._i) != 0;
    }


    public static DynamicValue operator |(DynamicValue a, DynamicValue b)
    {
        if (a.type == DynamicValueType.Long || b.type == DynamicValueType.Long)
            return new DynamicValue((long)a | (long)b);

        // 两个都是 LayerMask 时，结果保持 LayerMask
        if (a.type == DynamicValueType.LayerMask && b.type == DynamicValueType.LayerMask)
            return new DynamicValue((LayerMask)(a._i | b._i));

        return new DynamicValue(a._i | b._i);
    }

    public static DynamicValue operator &(DynamicValue a, DynamicValue b)
    {
        if (a.type == DynamicValueType.Long || b.type == DynamicValueType.Long)
            return new DynamicValue((long)a & (long)b);

        if (a.type == DynamicValueType.LayerMask && b.type == DynamicValueType.LayerMask)
            return new DynamicValue((LayerMask)(a._i & b._i));

        return new DynamicValue(a._i & b._i);
    }

    public static DynamicValue operator ^(DynamicValue a, DynamicValue b)
    {
        if (a.type == DynamicValueType.Long || b.type == DynamicValueType.Long)
            return new DynamicValue((long)a ^ (long)b);

        return new DynamicValue(a._i ^ b._i);
    }

    public static DynamicValue operator ~(DynamicValue a)
    {
        if (a.type == DynamicValueType.Long)
            return new DynamicValue(~(long)a.Value);

        return new DynamicValue(~a._i);
    }

    #endregion

    #region Log

    public void Log()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[DynamicValue Type: {type}]");
        BuildLogString(sb, 0);
        Debug.Log(sb.ToString());
    }

    private void BuildLogString(StringBuilder sb, int indentLevel)
    {
        switch (type)
        {
            case DynamicValueType.IList:
                BuildIListString(Value as IList, sb, indentLevel);
                break;
            case DynamicValueType.IDictionary:
                BuildIDictionaryString(Value as IDictionary, sb, indentLevel);
                break;
            default:
                sb.Append(ToString());
                break;
        }
    }

    private void BuildIListString(IList list, StringBuilder sb, int indentLevel)
    {
        if (list == null)
        {
            sb.Append("null");
            return;
        }

        sb.AppendLine($"List[{list.Count}]: [");

        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            sb.Append(GetIndent(indentLevel + 1));
            sb.Append($"[{i}] = ");

            if (item is DynamicValue dynValue)
            {
                if (dynValue.type == DynamicValueType.IList || dynValue.type == DynamicValueType.IDictionary)
                {
                    sb.AppendLine();
                    dynValue.BuildLogString(sb, indentLevel + 1);
                }
                else
                {
                    sb.AppendLine(dynValue.ToString());
                }
            }
            else if (item is IList innerList)
            {
                sb.AppendLine();
                BuildIListString(innerList, sb, indentLevel + 1);
            }
            else if (item is IDictionary innerDict)
            {
                sb.AppendLine();
                BuildIDictionaryString(innerDict, sb, indentLevel + 1);
            }
            else
            {
                sb.AppendLine(item?.ToString() ?? "null");
            }
        }

        sb.Append(GetIndent(indentLevel));
        sb.Append("]");
    }

    private void BuildIDictionaryString(IDictionary dict, StringBuilder sb, int indentLevel)
    {
        if (dict == null)
        {
            sb.Append("null");
            return;
        }

        sb.AppendLine($"Dictionary[{dict.Count}]: {{");

        foreach (DictionaryEntry entry in dict)
        {
            var key = entry.Key?.ToString() ?? "null";
            var value = entry.Value;

            sb.Append(GetIndent(indentLevel + 1));
            sb.Append($"\"{key}\": ");

            if (value is DynamicValue dynValue)
            {
                if (dynValue.type == DynamicValueType.IList || dynValue.type == DynamicValueType.IDictionary)
                {
                    sb.AppendLine();
                    dynValue.BuildLogString(sb, indentLevel + 1);
                }
                else
                {
                    sb.AppendLine(dynValue.ToString());
                }
            }
            else if (value is IDictionary innerDict)
            {
                sb.AppendLine();
                BuildIDictionaryString(innerDict, sb, indentLevel + 1);
            }
            else if (value is IList innerList)
            {
                sb.AppendLine();
                BuildIListString(innerList, sb, indentLevel + 1);
            }
            else
            {
                sb.AppendLine(value?.ToString() ?? "null");
            }
        }

        sb.Append(GetIndent(indentLevel));
        sb.Append("}");
    }

    private string GetIndent(int level) => new string(' ', level * 2);

    #endregion
}