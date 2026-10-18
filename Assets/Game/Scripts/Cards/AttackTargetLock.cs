using System.Collections.Generic;
using UnityEngine;

/// <summary>《光辉打击》施放后的追踪状态，附着于施法单位，随关卡或单位销毁而清理。</summary>
public sealed class AttackTargetLock : MonoBehaviour
{
    private sealed class Entry
    {
        public Unit target;
        public CardData card;
        public int remainingMoves;
        public HexCoord lastCoord;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Unit caster;

    public static void Register(Unit caster, Unit target, CardData card, int followupCount)
    {
        if (caster == null || target == null || card == null || followupCount <= 0) return;
        AttackTargetLock tracker = caster.GetComponent<AttackTargetLock>();
        if (tracker == null) tracker = caster.gameObject.AddComponent<AttackTargetLock>();
        tracker.caster = caster;
        tracker.entries.Add(new Entry
        {
            target = target,
            card = card,
            remainingMoves = followupCount,
            lastCoord = target.GetCoord()
        });
    }

    private void Awake() => caster = GetComponent<Unit>();

    private void OnEnable()
    {
        EventManager.AddListener<Unit>("UnitMoved", HandleMoved);
        EventManager.AddListener<Unit>("UnitDisplaced", HandleMoved);
        EventManager.AddListener<Unit>("UnitTeleported", HandleMoved);
        EventManager.AddListener<Unit>("UnitDied", HandleDied);
        EventManager.AddListener<int>("LevelStarted", HandleLevelStarted);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<Unit>("UnitMoved", HandleMoved);
        EventManager.RemoveListener<Unit>("UnitDisplaced", HandleMoved);
        EventManager.RemoveListener<Unit>("UnitTeleported", HandleMoved);
        EventManager.RemoveListener<Unit>("UnitDied", HandleDied);
        EventManager.RemoveListener<int>("LevelStarted", HandleLevelStarted);
    }

    private void HandleLevelStarted(int level) => entries.Clear();

    private void HandleDied(Unit dead)
    {
        entries.RemoveAll(entry => entry.target == dead);
        if (dead == caster) entries.Clear();
    }

    private void HandleMoved(Unit moved)
    {
        if (caster == null || caster.GetCurrentHealth() <= 0 || moved == null ||
            moved.IsDisplacing) return;
        foreach (Entry entry in new List<Entry>(entries))
        {
            if (caster == null || caster.GetCurrentHealth() <= 0) break;
            if (!entries.Contains(entry) || entry.target != moved ||
                entry.lastCoord == moved.GetCoord()) continue;
            if (moved.GetCurrentHealth() <= 0 || entry.remainingMoves <= 0)
            {
                entries.Remove(entry);
                continue;
            }
            entry.lastCoord = moved.GetCoord();
            entry.remainingMoves--;
            if (entry.remainingMoves <= 0) entries.Remove(entry);
            entry.card.ResolveLockedFollowup(caster, moved);
        }
    }
}
