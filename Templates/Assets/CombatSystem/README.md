# Combat System

Damage dealing and receiving for mobs and the player.

## What is inside
- `HealthMob` (mob health on top of `com.pg.health-system`), `Damager`, `RigidbodyDamager`, `AttackHitbox`, `ParticleDamage`, `AreaParticlesDamage`, `DangerSource`, `KnockbackReceiver`, `PlayerAttack`, `PlayerChargeAttack`, `LootMob`.
- `CombatFeedback`: camera shake, hit stop and screen feedback driven by `HealthMob` events.
- `DamageText`, `HealthAISlider` (health bar), enemy waves (`EnemyWave`, `EnemyWavesController`), `TrainingDummy`.

## Prefabs
- `Prefabs/Training Dummy`: capsule with `HealthMob` and `TrainingDummy` (counts hits, total damage and damage per second).
- `Prefabs/Mob Health Bar`: world space bar with the trailing hit effect. Put it under an object with `HealthMob`; it finds the mob itself.

## Requires
`com.pg.health-system`, `com.pg.core`, `com.pg.pgtween`, `com.pg.swarm-navigation` (the Git Package Installer installs them together).

## Not in the package (game side, connect through your own scripts)
Buff damage, upgrades that need saving, stamina costs for dodge and barrier, death animation on VAT characters and the boss storm effects depend on other systems. They use the events and interfaces above (`HealthMob.anyDamaged`, `ICombatReactor`, `IDamagable`).
