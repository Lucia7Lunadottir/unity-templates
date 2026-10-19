using UnityEngine;
using UnityEngine.Playables;

namespace PG
{
    // Клип хранит только данные — никакой логики симуляции.
    // Вся логика в ParticleTrackMixer, как в SubtitlesTrackMixer.
    public class ParticleBehaviour : PlayableBehaviour
    {
        // Зарезервировано для будущих данных клипа (например, speedMultiplier)
    }
}
