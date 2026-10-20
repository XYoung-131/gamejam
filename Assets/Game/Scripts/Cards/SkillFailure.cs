using UnityEngine;

/// <summary>
/// 「技能释放失败的原因」跨模块传递用的小信箱。
///
/// 为什么要有这个东西：
/// 出牌的校验散在 PlayerCardSystem（阶段/槽位/次数/射程/视线/亲密）、
/// PlayerUnit（耐力/行动次数）、DefenseSkillResolver（防御选格）三处，
/// 以前每一处只写 Debug.Log，UI 拿不到"为什么放不出来"，只能弹一句
/// "释放失败（射程/视线/耐力）"这种含糊提示。
///
/// 现在所有校验点统一调 Set(原因)，HUD 出牌失败时 Consume() 一次取走并弹窗，
/// 取走即清空，避免下一次成功出牌后还弹出上一次的旧原因。
/// </summary>
public static class SkillFailure
{
    private static string reason;

    /// <summary>最近一次失败原因（没有失败时可能残留上一条，UI 请用 Consume）</summary>
    public static string Reason => reason;

    /// <summary>记录失败原因（同时打 Console，方便排查）</summary>
    public static void Set(string value)
    {
        reason = value;
        if (!string.IsNullOrEmpty(value)) Debug.Log("[技能失败] " + value);
    }

    /// <summary>清空原因（每次尝试释放技能前调用）</summary>
    public static void Clear()
    {
        reason = null;
    }

    /// <summary>取走原因并清空；没有原因返回 null</summary>
    public static string Consume()
    {
        string value = reason;
        reason = null;
        return value;
    }
}
