namespace PG.BuffManagement
{
    public class ActiveBuff
    {
        public BuffData  data;
        public float     remainingTime;
        public int       stacks;
        public BuffSource source;

        public bool  IsPermanent    => data.IsPermanent;
        public bool  IsExpired      => !IsPermanent && remainingTime <= 0f;
        public float NormalizedTime => data.IsPermanent ? 1f : remainingTime / data.duration;

        public ActiveBuff(BuffData data, BuffSource source)
        {
            this.data          = data;
            this.source        = source;
            this.remainingTime = data.duration;
            this.stacks        = 1;
        }

        public float GetValue(BuffStatType statType, BuffModifierType modType)
        {
            float total = 0f;
            foreach (var mod in data.modifiers)
            {
                if (mod.statType == statType && mod.modifierType == modType)
                    total += mod.value * stacks;
            }
            return total;
        }
    }
}
