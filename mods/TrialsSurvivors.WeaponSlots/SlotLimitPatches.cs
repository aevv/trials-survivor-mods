using System.Collections.Generic;
using HarmonyLib;
using Iced.Intel;

namespace TrialsSurvivors.WeaponSlots;

internal static class SlotLimitPatches
{
    public const int VanillaSlots = 5;

    public static IReadOnlyList<PatchTarget> For(int slots)
    {
        var count = new ImmediateSite(default, VanillaSlots, slots, 1);
        var lastIndex = new ImmediateSite(default, VanillaSlots - 1, slots - 1, 1);
        var skills = typeof(ARPGEntity_Module_Skills);
        var cards = typeof(ARPGEntity_Module_CardUpgradeWrapper);

        return new[]
        {
            new PatchTarget("Skills.SkillBarIsFull", AccessTools.PropertyGetter(skills, nameof(ARPGEntity_Module_Skills.SkillBarIsFull)),
                count with { Register = Register.EAX }),
            new PatchTarget("Skills.TryGetSkillAtSlot", AccessTools.Method(skills, nameof(ARPGEntity_Module_Skills.TryGetSkillAtSlot)),
                lastIndex with { Register = Register.EBX }),
            new PatchTarget("Skills.TryFindFirstAvailableSlot", AccessTools.Method(skills, nameof(ARPGEntity_Module_Skills.TryFindFirstAvailableSlot)),
                count with { Register = Register.EAX }),
            new PatchTarget("Skills.AddSkill", AccessTools.Method(skills, nameof(ARPGEntity_Module_Skills.AddSkill)),
                count with { Register = Register.ECX }, count with { Register = Register.EBX }),
            new PatchTarget("Skills.RegisterSkill", AccessTools.Method(skills, nameof(ARPGEntity_Module_Skills.RegisterSkill)),
                count with { Register = Register.EBX }),
            new PatchTarget("Skills.ReplaceSkill", AccessTools.Method(skills, nameof(ARPGEntity_Module_Skills.ReplaceSkill)),
                lastIndex with { Register = Register.EDI }),
            new PatchTarget("Cards.AddSkillInternal", AccessTools.Method(cards, nameof(ARPGEntity_Module_CardUpgradeWrapper.AddSkillInternal)),
                count with { Register = Register.EBX }),
            new PatchTarget("Cards.BuildNormalDrawCandidates", AccessTools.Method(cards, nameof(ARPGEntity_Module_CardUpgradeWrapper.BuildNormalDrawCandidates)),
                count with { Register = Register.ESI }),
            new PatchTarget("Cards.TryDrawConstellation", AccessTools.Method(cards, nameof(ARPGEntity_Module_CardUpgradeWrapper.TryDrawConstellation)),
                count with { Register = Register.EAX })
        };
    }
}
