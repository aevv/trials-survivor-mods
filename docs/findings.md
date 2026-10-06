# Trials Survivors — reverse engineering notes

Game: **Trials Survivors** (Angry Wisp)
Build: Unity **2022.3.62f2** (Unity 6 internal versioning), **IL2CPP**, x64
`build-guid`: `6beb7c847e1241038d7d80f750458767`
IL2CPP metadata version: **31.1**

## Shape of the game

- No `Managed/` folder — IL2CPP. Game code is in `GameAssembly.dll`, names in
  `Trials Survivors_Data/il2cpp_data/Metadata/global-metadata.dat`.
- Gameplay assembly: `Assembly-CSharp` (2076 types).
- Built on **Unity DOTS/Entities + Burst** (`lib_burst_generated.dll`) with
  `ProjectDawn.Navigation` for agents. Enemies are entities with a bespoke
  spatial index, not physics colliders.
- Naming conventions worth knowing when reading dumps:
  - `SO_*` — ScriptableObjects (config/data assets)
  - `SS_*` — "skill system" runtime behaviours and data structs
  - `SSV2_*` — skill system v2 effect instances
  - `ARPG*` — core entity/combat layer
- Debug symbols survived: metadata still holds original source paths, e.g.
  `Assets\_Project\Sys_Entities\Entity\SpatialIndex\ARPGEntitySpatialIndex.cs`.

## The AoE target cap

This is the thing that makes a big explosion only hit a handful of monsters when
the screen is full. There are **three** separate limits in the chain.

### 1. Per-effect authored limit — the real one

**Three unrelated types declare their own `_limitDetectionCount`.** They are siblings,
not a hierarchy — `SS_Effect_AOE_Line` derives straight from `SS_Behaviour`, *not*
from `SS_Effect_AOE` — so a mod must patch each one. Patching only the sphere AoE
silently leaves every line/beam and aimed-projectile effect capped:

| type                                | base                             | field offset |
| ----------------------------------- | -------------------------------- | ------------ |
| `SS_Effect_AOE`                     | `SS_Behaviour`                    | `0x38`       |
| `SS_Effect_AOE_Line`                | `SS_Behaviour`                    | `0x38`       |
| `SS_Behaviour_LaunchAimedProjectile` | `SS_Behaviour_LaunchProjectileCore` | `0x50`     |

All three carry the same `-2 formula / -1 no limit` convention and all three
override `OnPlayBehaviour`, which is the natural patch point.

`SS_Effect_AOE : SS_Behaviour` (`[Serializable]`, embedded in skill card assets):

```csharp
[Tooltip("-2 to use formula, -1 for no limit")]
[SerializeField] protected int _limitDetectionCount;
[SerializeField] protected FormulaData _limitDetectionFormula;
[SerializeField] protected SS_TargetSorting.SelectionMode _selectMode;
```

Semantics, straight from the devs' own tooltip:

| value | meaning                                     |
| ----- | ------------------------------------------- |
| `-1`  | no limit                                    |
| `-2`  | evaluate `_limitDetectionFormula` at runtime |
| `>=0` | hard cap on targets hit                     |

`_selectMode` (`SS_TargetSorting.SelectionMode`) decides *which* targets survive
the cap — nearest, random, etc. Relevant flow:

```
SS_Effect_AOE.OnPlayBehaviour
  -> DetectTargets(detectionData, owner, target, position, radius, layerMask,
                   direction, cosHalfAngle, maxResults)
  -> SortTargets(SS_AOEDetectionData data, int limit)
```

**This is the field a mod should change.** Setting it to `-1` uses a code path
the developers already support, rather than forcing a value through a cap that
other code assumes is bounded.

### 2. Deferred batch slab — a hard 256 ceiling

`SS_AOEDeferredQueries` (static, batches AoE queries into Burst jobs):

```csharp
public static bool Enabled;
private const int SLAB_SIZE = 256;
private const int INITIAL_QUERY_CAPACITY = 512;
```

Each batched query writes hits into a 256-wide slab of a shared
`NativeArray<int> _hitIndices`, so **when `Enabled` is true, no single AoE can
report more than 256 hits** regardless of `_limitDetectionCount`. In practice 256
is far above anything a screen holds, so this is not a blocker — but it is a
ceiling, and `SS_AOEDeferredQueries.Enabled` is a public static we can flip to
take the non-deferred path if we ever need past it.

Its per-play record carries the limit through the batch:

```csharp
private sealed class DeferredPlay { ... internal int RawLimit; ... }
```

### 3. Spatial query hit cap

`ARPGEntitySpatialIndex` (`Assets\_Project\Sys_Entities\Entity\SpatialIndex\`):

```csharp
private const int INITIAL_CAPACITY = 4096;
private const float CELL_SIZE = 4f;
private const int MAX_LOS_CANDIDATES = 8;   // line-of-sight only, not AoE
public static bool UseBurstQuery;

public int QuerySphereNonAlloc(Vector3 center, float radius, int layerMask,
        SS_AOEDetectionData data, ARPGEntity owner, ARPGEntity target,
        in SS_TriggerDetectionDATA triggerDetection,
        ARPGEntityTargetFilter targetFilter = ARPGEntityTargetFilter.AliveOnly,
        Vector3 coneDirection = default, float coneCosHalfAngle = -1f,
        int maxResults = 0)
```

`maxResults` is passed down from the effect's limit. The Burst side receives it
on a plain struct:

```csharp
internal struct ARPGSpatialQueryArgs
{
    NativeArray<ARPGSpatialNativeEntry> Entries;   // 0x00
    NativeParallelMultiHashMap<long,int> Cells;    // 0x10
    NativeArray<int> Stamps;                       // 0x20
    NativeArray<int> HitIndices;                   // 0x30
    NativeArray<float> HitSqrDistances;            // 0x40
    NativeArray<float3> HitPositions;              // 0x50
    float3 Center; float Radius;                   // 0x60 / 0x6C
    int LayerMask; int Stamp;                      // 0x70 / 0x74
    int2 MinCell; int2 MaxCell;                    // 0x78 / 0x80
    float3 ConeDirection; float ConeCosHalfAngle;  // 0x88 / 0x94
    byte UseCone;                                  // 0x98
    int MaxHits;                                   // 0x9C  <-- cap
    byte SaturationCap;                            // 0xA0  <-- flag, not a count
    int OwnerIndex; int TargetIndex; ...
}
```

Note `SaturationCap` is a **`byte`, i.e. a flag**, not a numeric cap — it asks the
query to stop once the detection buffer saturates. Pairs with
`ARPGSpatialQueryBurst.ExecuteQuery` writing back `HitCount` / `Saturated`.

Because `MaxHits` is a field on a managed struct filled in *before* the Burst job
runs, **we never have to patch Burst-compiled native code.**

### The hit buffer is not a constraint

`SS_AOEDetectionData` is a pooled, self-resizing buffer:

```csharp
private const int InitialSize = 1024;
public Collider[] colliders; public ARPGEntity[] detectedTargets;
public float[] distances; public Vector3[] positions; public int[] indices;
public int count;
public bool IsSaturated { get; private set; }
public void Resize(int newSize);
public bool TryAddTarget(...);
public void MarkSaturated();
```

It starts at 1024 and grows, so raising the cap does **not** risk a buffer
overrun on this path — which is the usual way a mod like this crashes a Burst
game.

## In-run XP

The run's level and XP bar live on `ARPGEntity_Module_Level` (the player's module, which `UI_Module_XPBar` reads).
Class and global level progression is separate (`ClassManager.AddXpToGlobalLevel`, and the run-end
`LevelState_Playing.AddXp` / `AddPartialXpOnDeath`).

From the ISIL dump (`task dump FORMAT=isil`, then `dumps/isil/IsilDump/Assembly-CSharp/<Type>.txt`), only two
methods call `ARPGEntity_Module_Level.AddXp(float quantity)`:

- `Collectible_XpOrbInstance.EndGrab()` calls `AddXp(_xpData.XpMultiplierFormula.GetFloat(...) * _xpValue)`.
  The formula is where the player's "XP multiplier %" stat applies, so it's already included in `quantity`.
  `_xpValue` starts at `XpOrbDataSO._baseXp` and grows when orbs merge (`ReceiveMerge`). An orb upgrades to
  `_upgradeData` once it passes `_upgradeThreshold`.
- `CollectibleEffect_XpModifier.OnCollect()`, a collectible effect with its own `_quantity` formula.

Both calls are synchronous inside those methods. A prefix/finalizer pair on each caller can therefore mark the
source, and a prefix on `AddXp` can scale `quantity` for just that source. TrialsSurvivors.XpRates does this.

`SO_XpMultiplierManager` (`FinalMultiplier`, bonuses keyed by `SO_XpBonusSource`) also exists. It isn't on
the orb path above.

## The weapon slot cap

`ARPGEntity_Module_Skills.MAX_SKILL_COUNT = 5` is a `const`, so IL2CPP copied the literal into every use. There's
nothing to set at runtime. The ISIL dump shows where it ended up:

| method | native form | meaning |
|---|---|---|
| `Skills.get_SkillBarIsFull` | `cmp eax,5` | `Skills.Count >= 5` |
| `Skills.TryGetSkillAtSlot` | `cmp ebx,4` / `ja` | slot index bound |
| `Skills.TryFindFirstAvailableSlot` | `cmp eax,5` | slot loop |
| `Skills.AddSkill` | `cmp ecx,5`, `cmp ebx,5` | free-slot loops |
| `Skills.RegisterSkill` | `cmp ebx,5` | free-slot loop |
| `Skills.ReplaceSkill` | `cmp edi,4` / `jg` | slot index bound |
| `Cards.AddSkillInternal` | `cmp ebx,5` | `RegisterSkill` inlined here. The method also calls `SkillBarIsFull` |
| `Cards.BuildNormalDrawCandidates` | `cmp esi,5` | `Skills.Count < 5` gates new weapon cards in the draw |
| `Cards.TryDrawConstellation` | `cmp eax,5` / `setge` | inlined `SkillBarIsFull`, passed to `GenerateCardDeckPool` |
| `Skills.OnInitializeModule` | `new int[5]`, unrolled fill with -1 | `_skillSlots` |
| `Skills.FindNext/PreviousSelectableSlot`, `GetNextSelectableSkillExcluding` | `imul 66666667h` + `lea [rdx+rdx*4]` | `% 5`, compiled to a multiply. Can't be byte-patched |

"Cards" is `ARPGEntity_Module_CardUpgradeWrapper`. Every other `5` near skill code (string.Format arg arrays,
dictionary capacities, `UI_Module_Kikimeter.Awake` list capacities, `PlayerClassWrapper`) is unrelated. Searching
for inlined `Skills.Count` (`Dictionary<int, SSV2_SkillInstance>.get_Count` followed by a compare) only finds the
sites above.

Other relevant pieces:

- `SSV2_SkillInstance._isSelectable` (`+0x108`) is what the cycle methods check.
- `UI_Module_SpellBar.OnInitialize` fills `_spellSlots` from what looks like `GetComponentsInChildren<UI_Module_SpellSlot>()`
  (an unnamed generic call on `this`), then calls `TryGetSkillAtSlot` per slot. So an extra child slot gets picked up.
- `FSM_ARPG_Player_Alive.OnSelectSkillSlot0..4` are five separate input actions. There's no sixth.

Large IL2CPP methods get split into several `.pdata` entries. The ones after the first carry `UNW_FLAG_CHAININFO`
pointing back to it, and they're contiguous. Sizing a function from its first unwind entry alone misses most of
`AddSkillInternal` (300 of 4619 bytes).

TrialsSurvivors.WeaponSlots patches the immediates in memory and replaces the three modulo methods with Harmony prefixes.

The DPS meter (`UI_Module_Kikimeter`) only hardcodes 5 in `Awake`, which allocates 8 arrays of length 5 (`_rows`,
`_skillsBySlot`, `_dpsBuffer`, the index/percent buffers). Everything else loops to the array length, and
`EnsureRows` instantiates `_rowPrefab` into `_rowsContainer` for every null `_rows[i]`. Widening the arrays in an
`Awake` postfix is enough to get extra rows.

## Room objectives and completion

Each room is a `ChunkObjective` with one or more `ObjectiveController`s and a `Timer`. Score is turned into a tier by
`ComputeTier(score)`. It returns -1 below `TierThreshold0`, then 0/1/2 as each threshold is passed
(`TierCount = 3`, so "tier 3 met" is `CurrentTier >= 2`). Thresholds 0 and 1 can be disabled (negative).
`OnTierChanged(objective, from, to)` fires on change.

`UpdateRuntime` asks `_controllers[0].IsObjectiveCompleted(this)` each frame. Every controller type answers from
`TimeOut` (elapsed minus `_endlessStartOffsetSeconds` >= `_effectiveTimeDuration`, or `TimeDuration`). When it's
true it calls the private `CompleteObjective()`. That sets `_isCompleted`, closes spawn admission, exits timed
events, pauses the timer, notifies controllers, kills remaining mobs with no loot, cleans up destructibles and
hands out rewards. It's the only caller, and interop exposes it, so calling it directly is the same as the timer
running out.

The top-right panel is a `ChunkObjectiveUI` subclass per objective type, bound in `Setup(objective, reward,
floorNumber, totalRooms, isEndless)` and released in `Teardown()`.

## Difficulty, elites and the elite glow

**Difficulty chain.** `DifficultyManager._difficulties` holds six `SO_DifficultyData` assets. Each has a `Tier`
that `SO_EntityDifficultyScaling` uses to sum additive stat bonuses for every tier <= current. Past the last
classic difficulty, `GetLastClassicDifficulty()`, there's Unfair+. It's an int level in
`DATA_Saved_DifficultyProgress.currentUnfairPlusLevel`, capped by `maxUnlockedUnfairPlusLevel`, and adds
`UnfairPlusLevelBonus` on top of the highest tier. `SetCurrentDifficulty(data)` saves the id **and resets the Unfair+
level to 0**. `IsInUnfairPlusMode()` is `level > 0`.

`DifficultySelector` (the hub pedestal) keeps `_currentDifficultyIndex` and `_currentUnfairPlusLevel`.
`OnNextDifficulty`:
- with level > 0, it increments up to the max unlocked level
- otherwise it tries `TryGetNeighbourDifficulty(+1)`, which respects the current map's tier sets
- at the last index with max > 0, it enters Unfair+ 1
- at the true end, it does nothing

`OnPrevDifficulty` mirrors it, and going back from Unfair+ 1 calls `SetCurrentDifficulty(lastClassic)`. The popup is
`_currentDifficultyPopup` (`WorldSpaceUIDifficultyInteraction`), which has `SetDifficultyNameRaw`/`ColorRaw`/
`DescriptionRaw`/`SetLockedStateSimple` for text that isn't localised. `OnAsClosest` re-syncs both selector fields
from the manager and calls `RefreshDifficultyDisplay`. In-run escalation cards use
`SetRuntimeDifficultyOverride`, not `SetCurrentDifficulty`. `GetPersistentDifficulty()` ignores the override.

**Stats are additive.** `CombatEntityStatData.AddValue`/`SubstractValue` are the only mutators used by buffs. A
"multiplicative" monster card buff is `AddValue(stat.Value * (mult - 1))`. Monster health comes from the `Health`
attribute, whose max formula uses the `Max Health` stat (look it up by `entryName` in
`Manager_ARPGDatabase.Instance.GetStats()`). Spawn order: `ARPGEntity_Module_DifficultyScaling.OnEnableModule`
calls `ApplyEntry` (tier + Unfair+ bonuses), then `ActivateElite`, then `ChunkObjective.Register*` calls
`MonsterCardManager.ApplyAllEffects`.

**Elites.** There are 57 `EliteSettingsSO` assets, one per mob variant (`EliteSettings_Bat_Default`, ...). Most give
`Max Health` x30 (some x15/x25/x35) and `Movement speed` x1.1-1.3 via `statMultipliers` (applied to the
*template* base value, so they don't compound with tier bonuses), plus `Tenacity` +0.25. `EliteSettings_Frog_King`
has no multipliers.

The room's elite rate goes through `RoomEliteSpawnScheduler.Plan(..., eliteMultiplier, globalElitesPerSecond, ...)`.
`ChunkObjective.HandleMobSpawning` reads `MonsterCardManager._cachedEliteChanceMultiplier` **directly** (the
property getter is inlined), so patching `CumulativeEliteChanceMultiplier` does nothing. Patch `Plan`'s arguments
instead.

**Elite glow.** All elites use one `ARPGEntity_Module_Temp_ShaderModifierDataSO_EliteMob`, which drives the float
`IsActiveAura` (0/1) through `ARPGEntity_Module_Temp_ShaderModifier_IGPU`. The pink colour is `_EliteFresnelColor`
(HDR ~(0.85, 0, 4.2)) on the shared material `Shared_Color_palette_x8_Entities_Instancing` (shader
`SimpleToonShadingPalette8x1Full`). Some mobs also have an `AuraShader` shell with `_AuraColor`. **Both are
material-wide.** Mobs are drawn by BlackRose's InstancedAnimationSystem, and per-instance values are limited to the
`InstancedAnimationCustomValuesTemplate`: `Template_ToonFull` has `IsActiveAura`, `ChillValue`, `IgniteValue`,
`HitValue`, `DeathValue`, `Color_0..7` and `Emission_0..7`. So one elite can't get its own rim colour.
`MonsterRendererManager` sets the palette groups (`SetColors`/`SetEmissions`, 8 x `Vector4`) and calls
`RandomizeColors` from `OnEnable` and `Start`, so palette overrides reset when a mob comes out of the pool, and a
fresh mob's `Start` wipes anything written at elite activation.

What worked in practice:
- Calling `MonsterRendererManager.SetEmissions` from a mod throws a NullReferenceException.
- `InstancedRenderer.GetCustomShaderVectorValues()` lists each slot's `ShaderProperty` and `IdentifierIndex`, and
  `SetCustomShaderVectorValue(value, index)` writes it. Get the renderer from
  `ARPGEntity_Module_Temp_ShaderModifier_IGPU.InstancedRenderer`.
- Writes to `_Color_n` show up once they're re-applied after `SetPalette`.
- Red `_Emission_n` values (~1.5 HDR) gave **no visible glow**.
- Palettes are `ColorPaletteSO` with `_colorsLinear`/`_emissionsLinear` (what `SetPalette` uploads). Slime emissions
  are dim blues around 0.1-0.8, so the emission slots look like palette shading, not a glow channel.

**Reading asset values.** The SOs above can be read straight out of `Trials Survivors_Data\data.unity3d` with
UnityPy. IL2CPP strips the MonoBehaviour typetrees, but `obj.read(check_read=False)` still gives `m_Name` and
`m_Script` (to get the class name), and `get_raw_data()` after the header follows the dumped field order
(4-byte aligned, PPtr = int32 fileID + int64 pathID). `Material` objects read fully.

## Level-up card quality

Each card (`SO_CardCore`) lists the qualities it exists at in `QualityIdentifiers`. A quality is an
`SO_QualityIdentifier` asset, and `SO_QualityMaskConfig.Instance.Qualities` lists all of them. Each constellation
deck (`ConstellationDeckData`) holds several `WeightedCardQualityList`s that draw a (card, quality) pair together.
`ARPGEntity_Module_CardUpgradeWrapper.DrawCardFromDeck` calls `deck.TryDrawCard(out card, out quality, rerollChance)`,
then builds the card with `card.GenerateCardValueFromPool(quality)`.

**`GenerateCardValue(quality, cardCore)` is inlined** into `DrawCardFromDeck` (see the ISIL dump), so a Harmony patch
on it never fires, even though the interop exposes it. `GenerateCardValueFromPool` is a virtual call, so patching
the non-generic overrides on `SO_CardSkill` and `SO_CardStats` works. This is how All Legendary rewrites the quality.
Monster cards (`SO_CardMonster`) have their own override and don't come through this path.

Before patching a small private method, check the ISIL dump that its callers actually `Call` it.

## Other caps noted in passing

`MAX_ACTIVE_PROJECTILES`, `MAX_LOCKED_TARGETS`, `MAX_TARGET_SLOTS`,
`MAX_SKILL_COUNT`, `MAX_SANE_TOTAL_DAMAGE`, `MAX_CATCHUP_FIRES_PER_TICK`,
`MaxOverflowSpawnsPerFrame`, `MaxDeferredSpawnPlacementsPerFrame`, `_maxMobCount`,
`_maxTargets` / `_maxTargetsFromList` (on the target-locking + AoE-conditional
systems — a separate mechanic from the AoE hit cap).

`MAX_SANE_TOTAL_DAMAGE` is worth watching: a damage clamp could mask the effect
of hitting many more enemies at once.

## Useful for testing

`SO_DebugMonsterSpawner`, `SO_SkillDebugger`, `SO_DifficultyData`,
`SO_EndlessConfig`, `SO_WorldSpawnSettings`.

## Caveats

- Cpp2IL's `dll_il_recovery` recovers **signatures, fields and offsets reliably,
  but most method bodies come out as `throw null`** (they're native). Field
  layout and the call graph are trustworthy; control flow has to be read from an
  `isil` dump or a disassembler.
- Don't assume a type hierarchy from a shared name prefix. `SS_Effect_AOE_Line`
  looks like a specialisation of `SS_Effect_AOE` and is not one. Check the base
  type in the dump before writing a patch that relies on inheritance.
- Raising the cap costs frametime: every AoE walks more entities and resolves
  more damage. Expect this to matter most in late endless runs.
