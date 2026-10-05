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
