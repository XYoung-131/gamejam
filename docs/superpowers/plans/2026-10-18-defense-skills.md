# Defense Skills Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all 29 defense skills in the supplied workbook usable with their specified effects, self-first player interaction, and enemy execution.

**Architecture:** `CardData` stores stable rule IDs and asset references. A defense targeting helper finds legal cells; a resolver applies effects for players and enemies; `PlayerCardSystem` owns validation and payment; `BattleDebugDriver` owns clicks and highlights; `EnemyUnit` chooses AI targets through the same rules.

**Tech Stack:** Unity C#, ScriptableObject YAML assets, existing .NET 8 standalone checks, Unity editor checks when an editor is available.

**Spec:** `docs/superpowers/specs/2026-10-18-defense-skills-design.md`

## Global Constraints

- Cover all 29 skills: 16 player-usable and 13 enemy-only.
- Player infusion pays card Thought only; successful slot use pays Stamina only, once per cast.
- A selected defense card highlights only the caster first. Pure self effects cast on that click; targeted effects require a second click. Esc cancels target selection.
- Enemy-only cards remain unavailable from draw and steal; 《跳》《格挡》《远跳》 start slotted and are absent from ordinary offers.
- Do not modify example scene files or revert unrelated dirty workspace changes.
- Preserve existing asset GUIDs and serialized enum numbers; append new enum values explicitly.
- Invalid target, blocked landing, insufficient Stamina, or disabled action must not consume Stamina or uses.

## Review Focus

1. No legal landing: 《错乱传送》 and 《毒遁》 must not spend player resources or leave a stale selection.
2. Target dies after selection: final validation must reject a dead or removed unit before payment.
3. Multi-target duplicates: 《精神摧毁》 must apply once per distinct selected unit and charge once.
4. End-of-turn boundary: temporary debuffs, 《炎障》, 《坚守》, and 《自动屏障》 must expire or reset at the intended turn.
5. Limited free cells: summons place only units that fit, without overwriting occupants or changing unrelated terrain.

---

## File Structure

- `Assets/Game/Scripts/Cards/CardData.cs`: serialized defense rule IDs, parameters, and compatibility entry points.
- `Assets/Game/Scripts/Cards/DefenseSkillMath.cs`: pure hex-distance and distinct-target calculations usable in standalone checks.
- `Assets/Game/Scripts/Cards/DefenseSkillTargeting.cs`: legal target and landing cells, selection validation, AI target choice.
- `Assets/Game/Scripts/Cards/DefenseSkillResolver.cs`: shared effect execution, with helpers for shields, buffs, movement, terrain, and summons.
- `Assets/Game/Scripts/Units/DefenseCombatState.cs`: per-unit, per-turn flags and delayed/temporary defense effects.
- `Assets/Game/Scripts/Cards/PlayerCardSystem.cs`: slot and free-card use, cost, initial cards, offer filter.
- `Assets/Game/Scripts/Debug/BattleDebugDriver.cs`: self-first defense click state and highlights.
- `Assets/Game/Scripts/Units/EnemyUnit.cs`: choose and run enemy-only defense effects.
- `Assets/Game/ScriptableObjects/技能/防御技能/*.asset`: configure all 29 assets; keep `.meta` GUIDs.
- `Tests/DefenseRulesChecks/*` and `Assets/Game/Editor/DefenseSkillChecks.cs`: deterministic rules and Unity integration checks.

### Task 1: Serialized defense rules and deterministic targeting

**Files:** Create `DefenseSkillMath.cs`, `DefenseSkillTargeting.cs`, `Tests/DefenseRulesChecks/DefenseRulesChecks.csproj`, `Tests/DefenseRulesChecks/Program.cs`; modify `CardData.cs`; add targeting checks in `DefenseSkillChecks.cs`.

**Interfaces:** Produce `DefenseTargetRule` and `DefenseEffectRule` with explicit values; `DefenseSkillMath.IsDistanceAllowed(HexCoord from, HexCoord to, int min, int max)`; `DefenseSkillTargeting.GetSelectableCells(CardData card, Unit caster, IReadOnlyList<HexCoord> selected)` and `DefenseSkillTargeting.IsValidSelection(CardData card, Unit caster, IReadOnlyList<HexCoord> selected)`.

- [ ] Write failing standalone checks for distance 1 versus exactly 2 and distinct 1–3 target coordinates; add editor checks for occupied versus free landing, enemy versus ally, missing grid cell, and dead targets.
- [ ] Run `dotnet run --project Tests/DefenseRulesChecks/DefenseRulesChecks.csproj`; confirm the missing rule implementation is the failure.
- [ ] Add explicit serialized fields, pure distance helpers, and legal-cell logic. Link `HexCoord.cs`, `DefenseSkillMath.cs`, and existing `UnityStubs.cs` in the standalone project; keep attack enums untouched.
- [ ] Run the standalone checks; expect all Task 1 checks to pass.
- [ ] Commit only this task's files and new `.meta` files.

### Task 2: Initial cards and pool safety

**Files:** Modify `PlayerCardSystem.cs`, `LevelManager.cs`, `CardPoolFilteringChecks.cs`; add a focused editor check in `DefenseSkillChecks.cs`.

**Interfaces:** Use `CardData.initialOnly` to mark starting cards; generated defense pools may retain them, while `BuildOffer` excludes them.

- [ ] Write failing checks that 《跳》《格挡》《远跳》 occupy initial slots, no initial-only or enemy-only card enters offers, and no starting card is duplicated.
- [ ] Run the checks in Unity when available; otherwise compile and record the missing runtime test separately.
- [ ] Populate initial slots from initial-only assets in the configured defense pool and exclude those assets from ordinary offers. Preserve existing attack paths.
- [ ] Re-run pool checks and compile the generated C# project with the new sources included; expect zero errors.
- [ ] Commit only Task 2 files and assets needed to identify initial-only cards.

### Task 3: Movement and per-turn movement lock

**Files:** Create `DefenseCombatState.cs` and `DefenseSkillResolver.cs`; modify `Unit.cs` and the movement skill assets.

**Interfaces:** Produce `DefenseSkillResolver.CanResolve(CardData card, Unit caster, IReadOnlyList<HexCoord> selected)` and `DefenseSkillResolver.Resolve(CardData card, Unit caster, IReadOnlyList<HexCoord> selected)`; `DefenseCombatState` tracks successful self-movement skill use and 《坚守》 for the current turn.

- [ ] Write failing checks for 《跳》《小步走》《快走》《远跳》《换位》《错乱传送》《毒遁》 valid/invalid movement and 《坚守》 in both use orders.
- [ ] Run the checks and confirm failure in movement or turn-lock behavior.
- [ ] Implement movement, swap, teleport and turn lock; resolve landing before payment. Random teleport picks normal terrain first.
- [ ] Run checks and compile; verify no occupied cell is overwritten.
- [ ] Commit Task 3 files and configured movement assets only.

### Task 4: Self defense and reaction effects

**Files:** Modify `DefenseSkillResolver.cs`, `DefenseCombatState.cs`, `Unit.cs`, and relevant defense assets.

**Interfaces:** Reuse `CanResolve` and `Resolve`; expose `DefenseCombatState.GetStaminaCost(CardData card, Unit caster)` for 《自动屏障》 first-use cost.

- [ ] Write failing checks for 《格挡》《羽翼加护》《坚守》《伤痕填补》《自动屏障》《炎障》 including a shield-absorbed attack, first versus later use, and turn reset.
- [ ] Run the checks and confirm the specified shield/cost/reaction mismatch.
- [ ] Implement conditional shield values, missing-health shield, per-turn first-use discount, and attacker burn from damage context.
- [ ] Run checks and compile; confirm environment damage does not trigger 《炎障》.
- [ ] Commit Task 4 files and assets only.

### Task 5: Debuffs, delayed effects, and conditional blessing

**Files:** Modify `DefenseSkillResolver.cs`, `DefenseCombatState.cs`, `Unit.cs`, relevant buff and defense assets.

**Interfaces:** `DefenseCombatState` stores independent remaining duration for temporary stacks and scheduled 《抱抱》 application.

- [ ] Write failing checks for 2 stacks lasting 2/3/6 turns as specified, one-versus-three-target 《精神摧毁》, no duplicate 《抱抱》 scheduling, and 《神圣祝福》 self/angel branches.
- [ ] Run the checks and confirm current permanent-buff behavior fails the duration cases.
- [ ] Implement 《绊倒》《精神压制》《精神摧毁》《抱抱》《神圣祝福》《荧光汇聚》《肌肉突变》. Apply elite/BOSS stack rules through existing Buff APIs.
- [ ] Run checks and compile; verify target death before cast prevents payment.
- [ ] Commit Task 5 files and assets only.

### Task 6: Summons, terrain, and allied effects

**Files:** Modify `DefenseSkillResolver.cs`, `LevelManager.cs` or an existing spawn helper, `TerrainSystem.cs`, and the remaining enemy-only defense assets.

**Interfaces:** Spawn through existing `UnitData.prefab` and `EnemyUnit.Initialize`; use the existing `HexCell.terrain` and terrain-refresh path.

- [ ] Write failing checks for 《寻觅》《缝补》《抱抱喷射》《投掷织毯》《群体疗愈》《克勒妮的呼唤》《风飑》《岚匿》《飔垣》, including empty-cell shortages and missing named allies.
- [ ] Run the checks and confirm effects missing without changing board state on invalid candidates.
- [ ] Implement ally selection, summon placement, terrain conversion, group healing/movement and next-turn target protection.
- [ ] Run checks and compile; confirm summons occupy distinct valid cells and terrain change refreshes effects.
- [ ] Commit Task 6 files and assets only.

### Task 7: Self-first player input and payment boundary

**Files:** Modify `BattleDebugDriver.cs`, `PlayerCardSystem.cs`; extend `DefenseSkillChecks.cs`.

**Interfaces:** Produce `PlayerCardSystem.TryUseDefenseCardOnCells(int slotIndex, IReadOnlyList<HexCoord> selected)`; consume `GetSelectableCells`, `CanResolve`, and `Resolve`; hold target selection in the debug driver only.

- [ ] Write failing checks for self highlight on card selection, immediate self cast, second-step target/landing click, Esc cancel, no movement while a defense card is selected, 《精神摧毁》 confirmation, and invalid selection leaving Stamina and uses intact.
- [ ] Run the editor checks where available and confirm the expected input-state failure.
- [ ] Implement self-first click/highlight state and defensive slot/free-card routing. Validate before one Stamina payment and one use decrement. Keep 《轰击》 and attack selection behavior intact.
- [ ] Compile and run available checks; manually exercise the input path in Unity when an editor is available.
- [ ] Commit Task 7 files only.

### Task 8: Enemy execution and complete asset audit

**Files:** Modify `EnemyUnit.cs`, all remaining defense `.asset` files, `DefenseSkillChecks.cs`, and `docs/defense-skill-rules.md`.

**Interfaces:** Enemy AI consumes `DefenseSkillTargeting.GetSelectableCells`, calls `CanResolve` before `Resolve`, and preserves existing attack handling.

- [ ] Write failing checks that every one of 29 assets has the expected rule, cost, use count and rarity, that 13 enemy-only skills stay out of player offers, and enemy AI chooses valid units/landings.
- [ ] Run the checks and confirm any unconfigured asset or missing AI path fails.
- [ ] Finish 29 asset configurations and enemy auto-choice. Add a concise player-facing operation/reference document.
- [ ] Run standalone checks, editor checks if available, generated C# compilation and `git diff --check`; confirm no example scene diff.
- [ ] Request final code review, fix actionable findings, then commit only Task 8 files.
