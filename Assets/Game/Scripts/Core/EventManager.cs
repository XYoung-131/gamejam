using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 事件管理器——全局事件广播系统
/// 用字符串作为事件名，解耦各系统之间的通信
/// 静态类，无需挂载，直接调用
/// 
/// 用法：
///   // 订阅事件
///   EventManager.AddListener("UnitDied", OnUnitDied);
///   
///   // 触发事件
///   EventManager.Trigger("UnitDied", unit);
///   
///   // 取消订阅
///   EventManager.RemoveListener("UnitDied", OnUnitDied);
/// </summary>
public static class EventManager
{
    // 所有事件的字典（用委托基类存储）
    private static Dictionary<string, Delegate> eventDict = new Dictionary<string, Delegate>();

    // ============================================================
    //                      无参数事件
    // ============================================================

    /// <summary>
    /// 订阅无参数事件
    /// </summary>
    public static void AddListener(string eventName, Action callback)
    {
        if (!eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = callback;
        }
        else
        {
            eventDict[eventName] = Delegate.Combine(eventDict[eventName], callback);
        }
    }

    /// <summary>
    /// 取消订阅无参数事件
    /// </summary>
    public static void RemoveListener(string eventName, Action callback)
    {
        if (eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = Delegate.Remove(eventDict[eventName], callback);
            if (eventDict[eventName] == null)
                eventDict.Remove(eventName);
        }
    }

    /// <summary>
    /// 触发无参数事件
    /// </summary>
    public static void Trigger(string eventName)
    {
        Delegate del;
        if (eventDict.TryGetValue(eventName, out del))
        {
            Action callback = del as Action;
            if (callback != null)
                callback.Invoke();
        }
    }

    // ============================================================
    //                      1 参数事件
    // ============================================================

    /// <summary>
    /// 订阅 1 参数事件
    /// </summary>
    public static void AddListener<T>(string eventName, Action<T> callback)
    {
        if (!eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = callback;
        }
        else
        {
            eventDict[eventName] = Delegate.Combine(eventDict[eventName], callback);
        }
    }

    /// <summary>
    /// 取消订阅 1 参数事件
    /// </summary>
    public static void RemoveListener<T>(string eventName, Action<T> callback)
    {
        if (eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = Delegate.Remove(eventDict[eventName], callback);
            if (eventDict[eventName] == null)
                eventDict.Remove(eventName);
        }
    }

    /// <summary>
    /// 触发 1 参数事件
    /// </summary>
    public static void Trigger<T>(string eventName, T arg)
    {
        Delegate del;
        if (eventDict.TryGetValue(eventName, out del))
        {
            Action<T> callback = del as Action<T>;
            if (callback != null)
                callback.Invoke(arg);
        }
    }

    // ============================================================
    //                      2 参数事件
    // ============================================================

    /// <summary>
    /// 订阅 2 参数事件
    /// </summary>
    public static void AddListener<T1, T2>(string eventName, Action<T1, T2> callback)
    {
        if (!eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = callback;
        }
        else
        {
            eventDict[eventName] = Delegate.Combine(eventDict[eventName], callback);
        }
    }

    /// <summary>
    /// 取消订阅 2 参数事件
    /// </summary>
    public static void RemoveListener<T1, T2>(string eventName, Action<T1, T2> callback)
    {
        if (eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = Delegate.Remove(eventDict[eventName], callback);
            if (eventDict[eventName] == null)
                eventDict.Remove(eventName);
        }
    }

    /// <summary>
    /// 触发 2 参数事件
    /// </summary>
    public static void Trigger<T1, T2>(string eventName, T1 arg1, T2 arg2)
    {
        Delegate del;
        if (eventDict.TryGetValue(eventName, out del))
        {
            Action<T1, T2> callback = del as Action<T1, T2>;
            if (callback != null)
                callback.Invoke(arg1, arg2);
        }
    }

    // ============================================================
    //                      3 参数事件
    // ============================================================

    /// <summary>
    /// 订阅 3 参数事件
    /// </summary>
    public static void AddListener<T1, T2, T3>(string eventName, Action<T1, T2, T3> callback)
    {
        if (!eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = callback;
        }
        else
        {
            eventDict[eventName] = Delegate.Combine(eventDict[eventName], callback);
        }
    }

    /// <summary>
    /// 取消订阅 3 参数事件
    /// </summary>
    public static void RemoveListener<T1, T2, T3>(string eventName, Action<T1, T2, T3> callback)
    {
        if (eventDict.ContainsKey(eventName))
        {
            eventDict[eventName] = Delegate.Remove(eventDict[eventName], callback);
            if (eventDict[eventName] == null)
                eventDict.Remove(eventName);
        }
    }

    /// <summary>
    /// 触发 3 参数事件
    /// </summary>
    public static void Trigger<T1, T2, T3>(string eventName, T1 arg1, T2 arg2, T3 arg3)
    {
        Delegate del;
        if (eventDict.TryGetValue(eventName, out del))
        {
            Action<T1, T2, T3> callback = del as Action<T1, T2, T3>;
            if (callback != null)
                callback.Invoke(arg1, arg2, arg3);
        }
    }

    // ============================================================
    //                       工具方法
    // ============================================================

    /// <summary>
    /// 清空所有事件（切场景、重开游戏时调用）
    /// </summary>
    public static void ClearAll()
    {
        eventDict.Clear();
    }

    /// <summary>
    /// 检查某个事件是否有订阅者
    /// </summary>
    public static bool HasListener(string eventName)
    {
        return eventDict.ContainsKey(eventName) && eventDict[eventName] != null;
    }

    /// <summary>
    /// 获取当前事件总数（调试用）
    /// </summary>
    public static int GetEventCount()
    {
        return eventDict.Count;
    }
}
