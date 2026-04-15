namespace PG.BuffManagement
{
    public enum BuffModifierType
    {
        /// <summary>Adds a flat value: result = base + flat</summary>
        Flat,

        /// <summary>Adds a multiplier fraction: result = (base + flat) * (1 + percent)</summary>
        Percent,
    }
}
