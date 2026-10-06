using System;
using System.Collections.Generic;

namespace TrialsSurvivors.RunHistory;

public sealed class RunRecord
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime EndedAtUtc { get; set; }
    public string Result { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int ClassId { get; set; }
    public string Difficulty { get; set; } = "";
    public int DifficultyTier { get; set; }
    public int UnfairPlusLevel { get; set; }
    public string Map { get; set; } = "";
    public float DurationSeconds { get; set; }
    public int PlayerLevel { get; set; }
    public int CardDraws { get; set; }
    public int Kills { get; set; }
    public int EliteKills { get; set; }
    public int RoomsCompleted { get; set; }
    public double DamageDealt { get; set; }
    public float DamageTaken { get; set; }
    public int RelicsCollected { get; set; }
    public int ClassXpGained { get; set; }
    public List<SkillRecord> Skills { get; set; } = new();
    public Dictionary<int, double> DamageByDamageType { get; set; } = new();
    public Dictionary<int, int> KillsByEntityType { get; set; } = new();
}

public sealed class SkillRecord
{
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string IconSprite { get; set; } = "";
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public float DamageDealt { get; set; }
    public int Kills { get; set; }
    public float DamagePercent { get; set; }
}
