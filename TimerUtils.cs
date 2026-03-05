using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct TimerHandle : IEquatable<TimerHandle>
{
    internal long id;

    public bool Equals(TimerHandle other)
    {
        return id == other.id;
    }

    public override bool Equals(object obj)
    {
        return obj is TimerHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
       return id.GetHashCode();
    }
}
public enum DelayActionType
{
    Once,
    Repeat,
    OnceUnScaled,
    RepeatUnScaled,
    OnceFrame,
    RepeatFrame,
    OnceFrameUnScaled,
    RepeatFrameUnScaled,
}
public class DelayedAction
{
    public TimerHandle handle;
    
    public float delayTime;
    
    public Action action;

    public DelayActionType delayActionType;

    public float delayTimer;

    public object owner;

    public bool pause;

    public int RepeatCount;

    public int MaxRepeatCount = -1;
}
public class TimerUtils : MonoBehaviour
{
    static TimerUtils instance;

    public static TimerUtils Instance
    {
        get
        {
            if (!instance)
            {
                new GameObject("TimerUtils").AddComponent<TimerUtils>();
            }
            return instance;
        }
    }
    private readonly List<TimerHandle> toRemoveList = new List<TimerHandle>();
    
    private readonly List<DelayedAction> toAddList = new List<DelayedAction>();
    
    void Update()
    {
        float delta = Time.deltaTime;
        float unscaled = Time.unscaledDeltaTime;

        foreach (var pair in delayedActionDic)
        {
            var action = pair.Value;
            if(action.pause)
                continue;
            switch (action.delayActionType)
            {
                case DelayActionType.Once:
                case DelayActionType.Repeat:
                    action.delayTimer += delta;
                    break;
                case DelayActionType.OnceUnScaled:
                    case DelayActionType.RepeatUnScaled:
                    action.delayTimer += unscaled;
                    break;
                case DelayActionType.OnceFrame:
                case DelayActionType.RepeatFrame:
                    action.delayTimer += delta == 0 ? 0 : 1;
                    break;
                case DelayActionType.OnceFrameUnScaled:
                case DelayActionType.RepeatFrameUnScaled:
                    action.delayTimer += 1;
                    break;
            }
    
            if (action.delayTimer >= action.delayTime)
            {
                if(!ReferenceEquals(action.owner, null) && action.owner == null)
                {
                    // Destroyed
                    toRemoveList.Add(pair.Key);
                    continue;
                }

                var toRemove = false;
                if (action.delayActionType is DelayActionType.Repeat or DelayActionType.RepeatFrame
                    or DelayActionType.RepeatUnScaled or DelayActionType.RepeatFrameUnScaled)
                {
                    action.delayTimer -= action.delayTime;
                    action.RepeatCount++;
                    if (action.MaxRepeatCount != -1 && action.RepeatCount >= action.MaxRepeatCount)
                        toRemove = true;
                }
                else
                    toRemove = true;
                action.action?.Invoke();
                if(toRemove)
                    toRemoveList.Add(pair.Key);
            }
        }

        foreach (var pair in toRemoveList)
        {
            if(IsValid(pair))
             delayedActionDic.Remove(pair);
        }
        if (toRemoveList.Count != 0)
            toRemoveList.Clear();
        
        foreach (var pair in toAddList)
        {
            delayedActionDic.Add(pair.handle, pair);
        }
        if (toAddList.Count != 0)
        {
            toAddList.Clear();
        }
    }
    public void Awake()
    {
        if (!instance)
        {
            instance = this;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            DontDestroyOnLoad(gameObject);
        }
        else if(instance != this)
        {
            Destroy(gameObject);
        }
     
    }

    
    void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        foreach (var action in delayedActionDic)
        {
            if(!ReferenceEquals(action.Value.owner, null) && action.Value.owner == null)
            {
                toRemoveList.Add(action.Key);
            }
        }
        foreach (var pair in toRemoveList)
        {
            delayedActionDic.Remove(pair);
        }
        toRemoveList.Clear();
    }

    private readonly Dictionary<TimerHandle,DelayedAction> delayedActionDic = new ();

    private long TimerCounter;

    public DelayedAction GetDelayAction(TimerHandle handle)
    {
        if (IsValid(handle))
        {
            return delayedActionDic[handle];
        }
        return null;
    }
    
    public TimerHandle AddDelayedAction(float delayTime, Action action, object owner = null, DelayActionType delayActionType = DelayActionType.Once,int maxRepeatCount = -1)
    {
        var handle = new TimerHandle() { id = TimerCounter++ };
        toAddList.Add(new DelayedAction()
        {
            owner = owner, handle = handle, delayTime = delayTime, action = action, delayActionType = delayActionType,
            MaxRepeatCount = maxRepeatCount
        });
        return handle;
    }
    public void RemoveDelayedAction(TimerHandle handle)
    {
        toRemoveList.Add(handle);
    }

    public void ResumeActionTimer(TimerHandle handle)
    {
        if (IsValid(handle))
        {
            delayedActionDic[handle].pause = false;
        }
    }

    public void PauseActionTimer(TimerHandle handle)
    {
        if (IsValid(handle))
        {
            delayedActionDic[handle].pause = true;
        }
    }
    private bool IsValid(TimerHandle handle)
    {
        return delayedActionDic.ContainsKey(handle);
    }
    
}
