using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public struct TimerHandle : IEquatable<TimerHandle>
{
    internal int index;
    internal int version;

    public bool IsValid => index >= 0;

    public bool Equals(TimerHandle other) => index == other.index && version == other.version;
    public override bool Equals(object obj) => obj is TimerHandle other && Equals(other);
    public override int GetHashCode() => (index.GetHashCode() * 397) ^ version.GetHashCode();
}

public enum TimeMode { Scaled, Unscaled, Frames }

internal struct TimerData
{
    public float targetTime;
    public float interval;
    public int repeatCount;
    public int maxRepeatCount;
    public object owner;
    public Action action;
    public TimeMode mode;
    public bool isPaused;
    public float pauseTimeLeft;

    public int listIndex; // O(1) remove
}

public class TimerUtils : MonoBehaviour
{
    private static TimerUtils instance;

    public static TimerUtils Instance
    {
        get
        {
            if (!instance)
            {
                var go = new GameObject("TimerUtils");
                instance = go.AddComponent<TimerUtils>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    private TimerData[] dataArray = new TimerData[256];
    private int[] versions = new int[256];
    private readonly Stack<int> freeIndices = new Stack<int>(256);

    private readonly List<int> activeScaled = new List<int>(256);
    private readonly List<int> activeUnscaled = new List<int>(256);
    private readonly List<int> activeFrames = new List<int>(256);

    private readonly Queue<Action> structuralChanges = new Queue<Action>();

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        for (int i = 255; i >= 0; i--)
            freeIndices.Push(i);
        if(test)
            for (int i = 0; i < 10000; i++)
            {
                AddAction(0.3f + i/3000f, EmptyTestFunc, null, TimeMode.Scaled, 30+i/3000);
            }
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private readonly bool test = false;
    void EmptyTestFunc()
    {
        
    }

    private void EnsureCapacity()
    {
        if (freeIndices.Count == 0)
        {
            int old = dataArray.Length;
            int newCap = old * 2;

            Array.Resize(ref dataArray, newCap);
            Array.Resize(ref versions, newCap);

            for (int i = newCap - 1; i >= old; i--)
                freeIndices.Push(i);
        }
    }

    public TimerHandle AddAction(float delay, Action action, object owner = null, TimeMode mode = TimeMode.Scaled, int maxRepeat = 1)
    {
        EnsureCapacity();

        int index = freeIndices.Pop();
        versions[index]++;

        float currentTime = GetCurrentTime(mode);

        dataArray[index] = new TimerData
        {
            targetTime = currentTime + delay,
            interval = delay,
            repeatCount = 0,
            maxRepeatCount = maxRepeat,
            owner = owner,
            action = action,
            mode = mode,
            isPaused = false
        };

        structuralChanges.Enqueue(() =>
        {
            var list = GetActiveList(mode);

            dataArray[index].listIndex = list.Count;
            list.Add(index);
        });

        return new TimerHandle { index = index, version = versions[index] };
    }

    public void RemoveAction(TimerHandle handle)
    {
        if (!IsValidHandle(handle))
            return;

        structuralChanges.Enqueue(() =>
        {
            if (!IsValidHandle(handle))
                return;

            ReleaseIndex(handle.index, dataArray[handle.index].mode);
        });
    }

    private bool IsValidHandle(TimerHandle handle)
    {
        if (!handle.IsValid || handle.index >= versions.Length)
            return false;

        return versions[handle.index] == handle.version;
    }

    private void ReleaseIndex(int index, TimeMode mode)
    {
        ref TimerData data = ref dataArray[index];

        var list = GetActiveList(mode);

        int listIndex = data.listIndex;
        int lastSlot = list.Count - 1;

        int lastIndex = list[lastSlot];

        list[listIndex] = lastIndex;
        dataArray[lastIndex].listIndex = listIndex;

        list.RemoveAt(lastSlot);

        versions[index]++;

        data.action = null;
        data.owner = null;

        freeIndices.Push(index);
    }

    void Update()
    {
        /*using(LogTools.Monitor("TimerUtils Update",1f,()=>LogTools.Log(activeScaled.Count)))*/
        {
            while (structuralChanges.Count > 0)
            {
                try
                {
                    structuralChanges.Dequeue().Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            float scaledTime = Time.time;
            float unscaledTime = Time.unscaledTime;
            int frameCount = Time.frameCount;

            ProcessList(activeScaled, scaledTime, TimeMode.Scaled);
            ProcessList(activeUnscaled, unscaledTime, TimeMode.Unscaled);
            ProcessList(activeFrames, frameCount, TimeMode.Frames);
            
        }

    }

 
        private void ProcessList(List<int> activeList, float currentTime, TimeMode mode)
        {
            var list = activeList;
            var dataArr = dataArray;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                int dataIndex = list[i];
                ref TimerData data = ref dataArr[dataIndex];
                if (data.isPaused)
                    continue;
                
                if (currentTime < data.targetTime)
                    continue;
                
                if (data.owner is UnityEngine.Object uObj && !uObj)
                {
                    ReleaseIndex(dataIndex, mode);
                    continue;
                }
                try
                {
                    data.action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FastTimer] {e}");
                }

                data.repeatCount++;

                if (data.maxRepeatCount > 0 && data.repeatCount >= data.maxRepeatCount)
                {
                    ReleaseIndex(dataIndex, mode);
                }
                else
                {
                    data.targetTime += data.interval;
                }
            }
        }
    

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        CleanList(activeScaled, TimeMode.Scaled);
        CleanList(activeUnscaled, TimeMode.Unscaled);
        CleanList(activeFrames, TimeMode.Frames);
    }

    private void CleanList(List<int> list, TimeMode mode)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            int dataIndex = list[i];
            ref TimerData data = ref dataArray[dataIndex];

            if (data.owner is UnityEngine.Object uObj && !uObj)
            {
                ReleaseIndex(dataIndex, mode);
            }
        }
    }

    private float GetCurrentTime(TimeMode mode) => mode switch
    {
        TimeMode.Scaled => Time.time,
        TimeMode.Unscaled => Time.unscaledTime,
        _ => Time.frameCount
    };

    private List<int> GetActiveList(TimeMode mode) => mode switch
    {
        TimeMode.Scaled => activeScaled,
        TimeMode.Unscaled => activeUnscaled,
        _ => activeFrames
    };
}