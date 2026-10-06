# WeaponSlots

Raises `ARPGEntity_Module_Skills.MAX_SKILL_COUNT` (a const, so it was inlined at compile time) by patching the
game's native code in memory. docs/findings.md ("The weapon slot cap") lists every site and how it was found.

- `SlotLimitPatches` is the table of `cmp reg,imm` sites: register, vanilla value, and expected count per method.
  `ImmediatePatcher` checks all of them before writing anything. One mismatch means no patches and no Harmony
  hooks, so the game stays vanilla. Keep it all-or-nothing: a partial patch leaves `SkillBarIsFull` and the
  registration loops disagreeing.
- Function bounds come from `RtlLookupFunctionEntry`, extended through contiguous chained (`UNW_FLAG_CHAININFO`)
  entries. Big methods are split, and the first entry alone misses sites.
- Don't Harmony-hook a method in the patch table. A detour moves its code pointer, and the start check fails.
- After a game update, check the table against the ISIL before anything else:
  `task dump FORMAT=isil`, then grep `cmp [a-z0-9]+,[45]$` in the Disassembly sections of
  `ARPGEntity_Module_Skills.txt` / `ARPGEntity_Module_CardUpgradeWrapper.txt`. Also look for new `66666667h`
  (`% 5`) sites, which need a Harmony replacement rather than a byte patch.
- The cycle replacements size by `_skillSlots.Length`, so they're correct even with an unwidened array.
- Everything runtime-visible logs at Info: each patched instruction, the slot array widening, the spell bar slot
  clone (with whether its parent has a LayoutGroup), and each weapon added with its slot index.
