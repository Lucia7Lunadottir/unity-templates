# Buffs (1.0.0)

Timed effects on a character. `BuffManager` holds the `Buff` components of a character, `BuffLinker` points a hit collider at it, `BuffData` is the icon and name, `ActiveBuffsManager` and `ActiveBuffCell` show active effects.

In the package: `PoisonBuff` (damage over time) and `RegenerationBuff` (heal over time).

## Your own effects
Make a class that inherits `Buff`, override `ApplyBuff`, then register it so damagers and abilities can find it by type:

```csharp
CheckBuffType.Register(BuffType.Burn, typeof(MyFireBuff));   // call once at startup
```

`BuffType` is the key (Hot, Burn, Frozen, Poison, Heal). Poison and Heal are registered by the package.
