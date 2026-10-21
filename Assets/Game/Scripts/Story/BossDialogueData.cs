using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 首领战斗前后的剧情对话配置（一个角色一份）。
///
/// 存放位置：Assets/Game/Resources/BossDialogues/ 下，
/// 运行时由 BossDialoguePanel 自动扫描加载——新增角色只要复制一份资产改内容，
/// 不需要改代码。
///
/// 三段对话分别对应：
///   firstEncounter  —— 第一次遇见（开战前播放，播完记入 PlayerPrefs）
///   repeatEncounter —— 第二次及以后遇见（开战前播放）
///   victory         —— 该角色被击败时播放（每次胜利都播）
/// </summary>
[CreateAssetMenu(menuName = "Game/剧情/首领对话", fileName = "BossDialogue")]
public class BossDialogueData : ScriptableObject
{
    [Header("匹配条件")]
    [Tooltip("关卡里出现这个名字的单位时，认为“遇见”了该角色（与 UnitData.unitName 完全一致）")]
    public string unitName = "林德沃姆";

    [Tooltip("记录“已经见过第一次”用的存档键；留空则用 unitName。改这里可以让旧记录失效，重新播第一次遇见")]
    public string seenKey = "";

    [Header("表现")]
    [Tooltip("打字机速度：每秒显示多少个字（改成很大的值等于瞬间显示）")]
    public float charactersPerSecond = 45f;

    [Header("立绘")]
    [Tooltip("站在左边的角色（主角），名字要和台词里的 speaker 完全一致才会高亮")]
    public string leftName = "勒忒";
    public Sprite leftPortrait;
    [Tooltip("左侧立绘显示高度（像素，1920×1080 参考分辨率）。两侧填同一个值即为「立绘高度对齐」")]
    public float leftPortraitHeight = 960f;

    [Tooltip("站在右边的角色（对手），名字要和台词里的 speaker 完全一致才会高亮")]
    public string rightName = "林德沃姆";
    public Sprite rightPortrait;
    [Tooltip("右侧立绘显示高度（像素），与左侧保持同一个值")]
    public float rightPortraitHeight = 960f;

    [Header("第一次遇见（开战前）")]
    public List<DialogueLine> firstEncounter = new List<DialogueLine>();

    [Header("第二次及以后遇见（开战前）")]
    public List<DialogueLine> repeatEncounter = new List<DialogueLine>();

    [Header("胜利（该角色被击败后）")]
    public List<DialogueLine> victory = new List<DialogueLine>();
}

/// <summary>一句台词：说话人 + 内容</summary>
[System.Serializable]
public class DialogueLine
{
    [Tooltip("显示在左上角名牌上的名字，例如“林德沃姆”")]
    public string speaker = "";

    [TextArea(2, 6)]
    public string text = "";
}
