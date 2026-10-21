/// <summary>
/// 单个单位在一场战斗中的被动能力实例。
/// 计数、冷却、是否触发过等可变状态应放在派生 Runtime 中，不能写回 PassiveAbilityData。
/// </summary>
public class PassiveRuntime
{
    public PassiveAbilityData Data { get; }
    public Unit Owner { get; }
    public bool IsActive { get; private set; }

    public PassiveRuntime(PassiveAbilityData data, Unit owner)
    {
        Data = data;
        Owner = owner;
    }

    internal void Activate()
    {
        if (IsActive || Owner == null) return;
        IsActive = true;
        Owner.OnBuffApplied += HandleBuffApplied;
        Owner.OnBuffRemoved += HandleBuffRemoved;
        EventManager.AddListener<int>("PlayerTurnStarted", HandleRoundStarted);
        EventManager.AddListener("PlayerTurnEnded", HandlePlayerTurnEnded);
        EventManager.AddListener("EnemyTurnStarted", HandleEnemyTurnStarted);
        EventManager.AddListener<Unit, DamageContext>("UnitAboutToTakeDamage", HandleAboutToTakeDamage);
        EventManager.AddListener<Unit, Unit, int>("UnitDamageResolved", HandleUnitDamaged);
        EventManager.AddListener<Unit, Unit>("UnitDiedBy", HandleUnitDied);
        OnActivated();
    }

    internal void Deactivate()
    {
        if (!IsActive) return;
        Owner.OnBuffApplied -= HandleBuffApplied;
        Owner.OnBuffRemoved -= HandleBuffRemoved;
        EventManager.RemoveListener<int>("PlayerTurnStarted", HandleRoundStarted);
        EventManager.RemoveListener("PlayerTurnEnded", HandlePlayerTurnEnded);
        EventManager.RemoveListener("EnemyTurnStarted", HandleEnemyTurnStarted);
        EventManager.RemoveListener<Unit, DamageContext>("UnitAboutToTakeDamage", HandleAboutToTakeDamage);
        EventManager.RemoveListener<Unit, Unit, int>("UnitDamageResolved", HandleUnitDamaged);
        EventManager.RemoveListener<Unit, Unit>("UnitDiedBy", HandleUnitDied);
        OnDeactivated();
        IsActive = false;
    }

    public virtual void OnActivated() { }
    public virtual void OnDeactivated() { }
    public virtual void OnTurnStart() { }
    public virtual void OnTurnEnd() { }
    public virtual void OnRoundStarted(int round) { }
    public virtual void OnPlayerTurnEnded() { }
    public virtual void OnEnemyTurnStarted() { }
    public virtual void BeforeDecision() { }
    public virtual void AfterDecision(EnemyDecision decision) { }
    public virtual void OnOwnerAboutToTakeDamage(DamageContext context) { }
    public virtual bool CanBeDisplaced(DisplacementType type) { return true; }
    public virtual bool CanBeAffectedByTerrain(TerrainData terrain) { return true; }

    /// <summary>返回 false 可以阻止本次技能释放；不会启动该技能的冷却。</summary>
    public virtual bool CanUseSkill(CardData skill) { return true; }

    /// <summary>仅供明确拥有友军攻击机制的敌人被动覆写。</summary>
    public virtual bool CanTargetFriendlyWithAttack(Unit target) { return false; }

    /// <summary>只在技能完成结算后调用。</summary>
    public virtual void OnSkillUsed(CardData skill) { }
    public virtual void OnBuffApplied(BuffData buff) { }
    public virtual void OnBuffRemoved(BuffData buff) { }
    public virtual void OnUnitDamaged(Unit damaged, Unit source, int healthDamage) { }
    public virtual void OnOwnerDamaged(Unit source, int healthDamage) { }
    public virtual void OnDamageDealt(Unit target, int healthDamage) { }
    public virtual void OnUnitDied(Unit dead, Unit killer) { }
    public virtual void OnUnitKilled(Unit target) { }
    public virtual void OnOwnerDied() { }
    public virtual void OnOwnerDied(Unit killer) { OnOwnerDied(); }

    private void HandleBuffApplied(Unit unit, BuffData buff)
    {
        if (IsActive && unit == Owner) OnBuffApplied(buff);
    }

    private void HandleBuffRemoved(Unit unit, BuffData buff)
    {
        if (IsActive && unit == Owner) OnBuffRemoved(buff);
    }

    private void HandleRoundStarted(int round)
    {
        if (IsActive) OnRoundStarted(round);
    }

    private void HandlePlayerTurnEnded()
    {
        if (IsActive) OnPlayerTurnEnded();
    }

    private void HandleEnemyTurnStarted()
    {
        if (IsActive) OnEnemyTurnStarted();
    }

    private void HandleUnitDamaged(Unit damaged, Unit source, int healthDamage)
    {
        if (!IsActive || damaged == null || healthDamage <= 0) return;
        OnUnitDamaged(damaged, source, healthDamage);
        if (damaged == Owner) OnOwnerDamaged(source, healthDamage);
        if (source == Owner && damaged != Owner) OnDamageDealt(damaged, healthDamage);
    }

    private void HandleAboutToTakeDamage(Unit damaged, DamageContext context)
    {
        if (IsActive && damaged == Owner && context != null)
            OnOwnerAboutToTakeDamage(context);
    }

    private void HandleUnitDied(Unit dead, Unit killer)
    {
        if (!IsActive || dead == null) return;
        OnUnitDied(dead, killer);
        if (killer == Owner && dead != Owner) OnUnitKilled(dead);
        if (dead != Owner) return;
        OnOwnerDied(killer);
        Deactivate();
    }
}
