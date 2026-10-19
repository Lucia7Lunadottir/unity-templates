using UnityEngine;
using UnityEngine.Rendering;

namespace PG.Rendering
{
    // Настройки освещения/теней для RenderParams - раньше были захардкожены внутри
    // IndirectRenderBatch/IndirectMeshRenderer, теперь настраиваются в инспекторе.
    // LightProbeUsage/ReflectionProbeUsage важны для GPU-driven рендера: без них инстансы
    // не получают запечённое/реалтайм окружающее освещение как обычные объекты сцены.
    [System.Serializable]
    public struct IndirectLightingSettings
    {
        public ShadowCastingMode ShadowCastingMode;
        public bool ReceiveShadows;
        public LightProbeUsage LightProbeUsage;
        public ReflectionProbeUsage ReflectionProbeUsage;

        public static IndirectLightingSettings Default => new()
        {
            ShadowCastingMode = ShadowCastingMode.On,
            ReceiveShadows = true,
            LightProbeUsage = LightProbeUsage.BlendProbes,
            ReflectionProbeUsage = ReflectionProbeUsage.BlendProbes
        };

        public void Apply(ref RenderParams rparams)
        {
            rparams.shadowCastingMode = ShadowCastingMode;
            rparams.receiveShadows = ReceiveShadows;
            rparams.lightProbeUsage = LightProbeUsage;
            rparams.reflectionProbeUsage = ReflectionProbeUsage;
        }
    }
}
