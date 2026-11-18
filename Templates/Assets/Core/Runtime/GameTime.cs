/// <summary>
/// Global game-time multiplier. Set this to speed up or slow down all
/// time-dependent systems (day/night, temperature, furnace, etc.)
/// without touching Time.timeScale — physics and AI stay at normal speed.
/// </summary>
public static class GameTime
{
    public static float timeMultiplier { get; private set; } = 1f;

    /// <summary>Time.deltaTime scaled by timeMultiplier. Use instead of Time.deltaTime
    /// in systems that should speed up during sleep (AI, temperature, day/night).</summary>
    public static float delta => UnityEngine.Time.deltaTime * timeMultiplier;

    public static void SetMultiplier(float value) => timeMultiplier = value;
    public static void Reset() => timeMultiplier = 1f;
}
