# UncapAoE

Rewrites `_limitDetectionCount` on AoE effects before they play. docs/findings.md has the full cap chain.

- The field is per effect instance and rewritten in a prefix on `OnPlayBehaviour`. `AuthoredLimits` keeps
  the original value per instance, so `Multiplier` mode never compounds across casts.
- Leave `-1` (already unlimited) and `-2` (designer formula) alone.
- **Uncapping is only safe for damage AoEs.** Some AoEs use the cap to pick targets for spawns, and
  uncapping them spawned one orb per enemy (Aqua Nova). `SpawnDetector` asks the game's own
  `GetChildrenEffectInstance` walk for `SSV2_ProjectileInstance` / `SSV2_ChainingInstance` children and keeps
  those caps. Aimed projectile launchers are the same pattern by construction, so they're opt-in
  (`UncapAimedProjectiles`).
- With `LogOriginalLimits` on, each authored cap and each kept cap is logged once. Use that before
  changing the rules.
