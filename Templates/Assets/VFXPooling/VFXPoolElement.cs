using UnityEngine;

namespace PG.VFXPool
{
    // Пустышка эффекта: её позиция, поворот и размер - то, где и каким появится эффект из пула.
    public class VFXPoolElement : MonoBehaviour
    {
        [SerializeField] private string _vfxID;
        [SerializeField] private bool _stopWithDisable;
        [Tooltip("Эффект едет за пустышкой, пока играет (лапа, голова). Выключено - остаётся там, где появился.")]
        [SerializeField] private bool _follow;
        [Tooltip("Для эффектов, которые рождают частицы на поверхности меша зверя (Shape: Mesh/Skinned Mesh Renderer).")]
        [SerializeField] private Renderer _shapeRenderer;
        private ParticleSystem _vfx;
        private int _playId;

        public string vfxID => _vfxID;

        public void Play()
        {
            if (VFXPool.instance == null)
            {
                Debug.LogWarning($"[VFXPoolElement] На сцене нет VFXPool, '{_vfxID}' не сыгран.", this);
                return;
            }

            // Петля, запущенная повторно, не должна остаться висеть без хозяина.
            if (IsPlaying()) VFXPool.instance.StopVFX(_vfx, allowParticlesToFinish: false);

            _vfx = VFXPool.instance.PlayVFX(_vfxID, transform, _follow, _shapeRenderer);
            _playId = VFXPool.instance.GetPlayId(_vfx);
        }

        public void Stop(bool allowParticlesToFinish = true)
        {
            if (IsPlaying()) VFXPool.instance.StopVFX(_vfx, allowParticlesToFinish);
            _vfx = null;
            _playId = 0;
        }

        // Экземпляр всё ещё наш: пул мог забрать его по таймеру и выдать другому.
        public bool IsPlaying()
        {
            return _vfx != null && VFXPool.instance != null && _playId != 0 && VFXPool.instance.GetPlayId(_vfx) == _playId;
        }

        public ParticleSystem GetParticleSystem()
        {
            return IsPlaying() ? _vfx : null;
        }

        private void OnDisable()
        {
            if (_stopWithDisable)
            {
                // Ensure the active VFX is properly stopped when this element is disabled
                Stop(allowParticlesToFinish: false);
            }
        }
    }
}
