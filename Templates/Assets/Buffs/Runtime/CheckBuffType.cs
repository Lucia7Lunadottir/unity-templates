using System;
using System.Collections.Generic;

namespace PG.BuffSystem
{
    public enum BuffType
    {
        Hot,
        Burn,
        Frozen,
        Poison,
        Heal
    }

    // Какой класс баффа соответствует типу. Яд и лечение есть в пакете, остальные игра регистрирует сама
    public static class CheckBuffType
    {
        private static readonly Dictionary<BuffType, Type> _types = new Dictionary<BuffType, Type>
        {
            { BuffType.Poison, typeof(PoisonBuff) },
            { BuffType.Heal, typeof(RegenerationBuff) },
        };

        public static void Register(BuffType buffType, Type buff) => _types[buffType] = buff;

        public static Type GetBuffType(BuffType buffType) => _types.TryGetValue(buffType, out var type) ? type : null;
    }
}
