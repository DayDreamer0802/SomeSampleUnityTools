using UnityEngine;
using UnityEngine.Profiling;
using System.Text;
using System;
using Unity.Profiling;


public class RuntimeProfileUtils : MonoBehaviour
{
    public static RuntimeProfileUtils Instance { get; private set; }


    const int GraphWidth = 300;
    const int GraphHeight = 100;
    const int MaxSamples = GraphWidth;
    const float UpdateInterval = 0.2f;

    // --- 数据存储 ---
    readonly float[] _frameDeltaBuffer = new float[MaxSamples];
    readonly float[] _sortedDeltas = new float[MaxSamples];
    int _bufferIndex;

    // --- 绘图相关 ---
    Texture2D _graphTexture;
    Color32[] _pixels;
    readonly Color32 _bgColor = new Color32(0, 0, 0, 150);
    readonly Color32 _curveColor = new Color32(0, 255, 255, 255);
    readonly Color32 _lineGoodColor = new Color32(50, 255, 50, 128);
    readonly Color32 _lineWarnColor = new Color32(255, 80, 80, 128);

    // --- 状态与计时 ---
    bool _show = false;
    float _dtTimer = 0;
    float _avgFPS = 0;
    float _onePercentLow = 0;
    float _maxGraphFPS = 60f;
    [SerializeField]
    bool IsInitialized = false;

    Rect _windowRect;
    Rect _btnRect;
    GUIStyle _textStyle;
    float _uiScale = 1f;
    readonly StringBuilder _sb = new StringBuilder(1024);
    string _cachedDisplayText = "Initializing...";

    // --- Profiler Recorders ---
    ProfilerRecorder _setPassCallsRecorder;
    ProfilerRecorder _drawCallsRecorder;
    ProfilerRecorder _verticesRecorder;
    ProfilerRecorder _trianglesRecorder;
    ProfilerRecorder _shadowCastersRecorder;
    ProfilerRecorder _batchesRecorder;
    ProfilerRecorder _waitForPresentRecorder;
    private bool IsActive;

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        IsActive = IsInitialized;
        Instance = this;
    }
    // 【新增】自动初始化，打包或编辑器下自动生效
    /*[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInitialize()
    {
        if (Application.isEditor)
        {

            var go = new GameObject("[RuntimeProfileUtils]");
            go.AddComponent<RuntimeProfileUtils>();
            DontDestroyOnLoad(go);
        }
    }*/

    private bool isInit = false;

    void Init()
    {
        isInit = true;
        _graphTexture = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };
        _pixels = new Color32[GraphWidth * GraphHeight];

        for (int i = 0; i < MaxSamples; i++) _frameDeltaBuffer[i] = 1f / 60f;
        _cpuFrameRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Internal,
            "Main Thread");

        _gpuFrameRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Render,
            "GPU Frame Time");
        _shadowCastersRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Render,
            "Shadow Casters");
        _batchesRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Render,
            "Batches Count");
        _waitForPresentRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Render,
            "Gfx.WaitForPresentOnGfxThread");
        _setPassCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _verticesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        _trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
    }

    void Start()
    {
        if (!IsActive)
            return;
        Init();
    }

    void OnDestroy()
    {
        if (!isInit)
            return;
        _shadowCastersRecorder.Dispose();
        _waitForPresentRecorder.Dispose();
        _batchesRecorder.Dispose();
        _setPassCallsRecorder.Dispose();
        _cpuFrameRecorder.Dispose();
        _gpuFrameRecorder.Dispose();
        _drawCallsRecorder.Dispose();
        _verticesRecorder.Dispose();
        _trianglesRecorder.Dispose();
        if (_graphTexture) Destroy(_graphTexture);
    }

    private float TextureTimer = 0;

    void Update()
    {
        // 1. 采集数据：每帧必做，用于保证 1% Low 的准确性
        _frameDeltaBuffer[_bufferIndex] = Time.unscaledDeltaTime;
        _bufferIndex = (_bufferIndex + 1) % MaxSamples;
        if (Input.GetKeyDown(KeyCode.F12))
        {
            IsActive = !IsActive;
            if (IsActive && !isInit)
            {
                Init();
            }

            if (IsActive)
            {
                _uiScale = Screen.height / 1080f;
                float logicalWidth = Screen.width / _uiScale;
                float logicalHeight = Screen.height / _uiScale;
                if (_windowRect.width == 0 ||
                    _windowRect.x > logicalWidth ||
                    _windowRect.y > logicalHeight ||
                    _windowRect.x < 0 ||
                    _windowRect.y < 0)
                {
                    _windowRect = new Rect(logicalWidth - 330, 60, 320, 430);
                }
            }
        }

        if (!IsActive||!isInit)
            return;
        
        // 2. 降频更新 UI 和 图表
        _dtTimer += Time.unscaledDeltaTime;
        if (_dtTimer >= UpdateInterval)
        {
            _dtTimer = 0; // 重置计时器

            CalculateMetrics();

            if (_show)
            {
                RebuildString();
            }
        }

        TextureTimer += Time.unscaledDeltaTime;
        if (TextureTimer >= 0.02f)
        {
            TextureTimer -= 0.02f;
            UpdateGraphTexture();
        }
    }

    void CalculateMetrics()
    {
        Array.Copy(_frameDeltaBuffer, _sortedDeltas, MaxSamples);
        Array.Sort(_sortedDeltas);

        int sampleCount1Percent = Mathf.Max(1, (int)(MaxSamples * 0.01f));
        int startIndex = MaxSamples - sampleCount1Percent;
        double totalTimeLow = 0;

        for (int i = startIndex; i < MaxSamples; i++)
            totalTimeLow += _sortedDeltas[i];

        float avgLowFrameTime = (float)(totalTimeLow / sampleCount1Percent);
        _onePercentLow = avgLowFrameTime > 0 ? 1f / avgLowFrameTime : 0;
        _avgFPS = 1f / Time.smoothDeltaTime;

        // 动态调整图表的最大帧率
        float target = Application.targetFrameRate > 0
            ? Application.targetFrameRate
            : Screen.currentResolution.refreshRate;
        _maxGraphFPS = Mathf.Max(60f, target * 1.2f); // 预留 20% 的顶部空间
    }

    ProfilerRecorder _cpuFrameRecorder;
    ProfilerRecorder _gpuFrameRecorder;

    void RebuildString()
    {
        _sb.Length = 0;

        _sb.Append("FPS: ").AppendFloat(_avgFPS, 1).Append("    (").AppendFloat(Time.unscaledDeltaTime * 1000f, 2)
            .Append(" ms)\n")
            .Append("1% Low: ").AppendFloat(_onePercentLow, 1).Append("\n");
        float cpuMs = _cpuFrameRecorder.Valid ? _cpuFrameRecorder.LastValue / 1000000f : 0;
        float gpuMs = _gpuFrameRecorder.Valid ? _gpuFrameRecorder.LastValue / 1000000f : 0;
        float waitMs = _waitForPresentRecorder.Valid
            ? _waitForPresentRecorder.LastValue / 1000000f
            : 0f;

        _sb.Append("Cpu Frame: ").AppendFloat(cpuMs, 2).Append(" ms\n");
        _sb.Append("Gpu Frame: ").AppendFloat(gpuMs, 2).Append(" ms\n");
        _sb.Append("GPU Wait: ")
            .AppendFloat(waitMs, 2)
            .Append(" ms\n");
        _sb.Append("--------------------------------\n");

        long alloc = Profiler.GetTotalAllocatedMemoryLong();
        long reserved = Profiler.GetTotalReservedMemoryLong();
        long monoHeap = Profiler.GetMonoHeapSizeLong();
        long monoUsed = Profiler.GetMonoUsedSizeLong();

        _sb.Append("Used Mem: ").AppendMem(reserved - Profiler.GetTotalUnusedReservedMemoryLong())
            .Append("\nAlloc: ").AppendMem(alloc)
            .Append("\nReserved: ").AppendMem(reserved)
            .Append("\nMono Used: ").AppendMem(monoUsed)
            .Append("\nMono Heap: ").AppendMem(monoHeap)
            .Append("\n--------------------------------\n");

        _sb.Append("DrawCalls: ").AppendLong(ValidRecorder(_drawCallsRecorder))
            .Append("\nBatches:   ").AppendLong(ValidRecorder(_batchesRecorder))
            .Append("       SetPass:   ").AppendLong(ValidRecorder(_setPassCallsRecorder))
            .Append("\nShadow:    ").AppendLong(ValidRecorder(_shadowCastersRecorder))
            .Append("\nTriangles: ").AppendCount(ValidRecorder(_trianglesRecorder))
            .Append("       Vertices:  ").AppendCount(ValidRecorder(_verticesRecorder));

        _cachedDisplayText = _sb.ToString();
    }

    void UpdateGraphTexture()
    {
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = _bgColor;

        void DrawHLine(float fps, Color32 col)
        {
            int y = GetY(fps);
            if (y >= 0 && y < GraphHeight)
            {
                for (int x = 0; x < GraphWidth; x += 4) _pixels[y * GraphWidth + x] = col;
            }
        }

        float target = Application.targetFrameRate > 0
            ? Application.targetFrameRate
            : Screen.currentResolution.refreshRate;
        DrawHLine(target, _lineGoodColor);
        DrawHLine(target / 2f, _lineWarnColor);

        int prevX = 0;
        int prevY = GetY(1f / _frameDeltaBuffer[_bufferIndex]);

        for (int x = 1; x < GraphWidth; x++)
        {
            int idx = (_bufferIndex + x) % MaxSamples;
            float dt = _frameDeltaBuffer[idx];
            int y = GetY(dt > 0 ? 1f / dt : 0);

            DrawLine(prevX, prevY, x, y, _curveColor);
            prevX = x;
            prevY = y;
        }

        _graphTexture.SetPixels32(_pixels);
        _graphTexture.Apply(false);
    }

    void OnGUI()
    {
        if (!IsActive)
            return;

        _uiScale = Screen.height / 1080f;

        GUI.matrix = Matrix4x4.identity;
        GUI.matrix = Matrix4x4.Scale(new Vector3(_uiScale, _uiScale, 1f));

     

        _textStyle ??= new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = Color.white },
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        // F12开启时如果UI在屏幕外，重置位置
     

        // button永远贴在window上方
        _btnRect = new Rect(_windowRect.x + 200, _windowRect.y - 45, 120, 40);

        if (GUI.Button(_btnRect, "FPS: " + (int)_avgFPS))
            _show = !_show;

        if (!_show) return;

        _windowRect = GUI.Window(999, _windowRect, WindowFunction, "Performance Profiler");
    }

    void WindowFunction(int windowID)
    {
        // 绘制文本
        GUI.Label(new Rect(10, 25, 300, 300), _cachedDisplayText, _textStyle);
        // 绘制图表
        GUI.DrawTexture(new Rect(10, 320, GraphWidth, GraphHeight), _graphTexture);
        // 使窗口可以拖拽
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    // --- 辅助方法 ---
    int GetY(float fps) => (int)(Mathf.Clamp01(fps / _maxGraphFPS) * (GraphHeight - 1));

    void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 >= 0 && x0 < GraphWidth && y0 >= 0 && y0 < GraphHeight)
                _pixels[y0 * GraphWidth + x0] = color;

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    long ValidRecorder(ProfilerRecorder r) => r.Valid ? r.LastValue : 0;
}

// --- StringBuilder 扩展保持原样并优化链式调用 ---
public static class StringBuilderExtensions
{
    public static StringBuilder AppendInt(this StringBuilder sb, int value) => sb.Append(value);
    public static StringBuilder AppendLong(this StringBuilder sb, long value) => sb.Append(value);

    public static StringBuilder AppendFloat(this StringBuilder sb, float value, int precision)
    {
        if (value < 0)
        {
            sb.Append('-');
            value = -value;
        }

        int intPart = (int)value;
        sb.Append(intPart);

        if (precision > 0)
        {
            sb.Append('.');
            float remainder = value - intPart;
            for (int i = 0; i < precision; i++)
            {
                remainder *= 10;
                int digit = (int)remainder;
                sb.Append(digit);
                remainder -= digit;
            }
        }

        return sb;
    }

    public static StringBuilder AppendMem(this StringBuilder sb, long bytes)
    {
        return sb.AppendFloat(bytes / 1048576f, 2).Append(" MB"); // 1024*1024 = 1048576
    }

    public static StringBuilder AppendCount(this StringBuilder sb, long count)
    {
        if (count > 1000000) return sb.AppendFloat(count / 1000000f, 1).Append('M');
        if (count > 1000) return sb.AppendFloat(count / 1000f, 1).Append('k');
        return sb.AppendLong(count);
    }
}