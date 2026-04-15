namespace PG.BuffManagement
{
    public enum BuffStatType
    {
        // --- Combat ---
        Damage,
        MagicDamage,
        Defense,
        CriticalChance,
        CriticalDamage,

        // --- Vitals ---
        MaxHealth,
        HealthRegen,
        HealBonus,

        // --- Movement ---
        MoveSpeed,
        DashSpeed,

        // --- Survival ---
        Warmth,          // Flat degrees added to effective temperature target (clothing/food warmth)
        ColdResistance,  // 0..1 percent — reduces temperature change speed when cooling
        HeatResistance,  // 0..1 percent — reduces temperature change speed when heating (light clothing)

        // --- Progression ---
        ExpMultiplier,
    }
}
