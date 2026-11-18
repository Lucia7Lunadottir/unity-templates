using PG.HealthSystem;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Serialization;

public class ParticleDamage : MonoBehaviour, IParticleDamagable
{
    [Tooltip("Ультра-удар: барьер и блок не спасают, только уклонение (как в KH BBS).")]
    [FormerlySerializedAs("_useUltraDamage")]
    [SerializeField] protected bool _unblockable;
    [SerializeField] protected int _damage;
    [TagDropdown] public List<string> tags = new List<string>();
    [SerializeField] private AssetReferenceGameObject _damageTextReference;
    
    protected HashSet<string> _tagSet;
    private ParticleSystem _particleSystem;
    private List<ParticleCollisionEvent> _collisionEvents = new List<ParticleCollisionEvent>();
    private Camera _camera;
    

    private void Awake()
    {
        _camera = Camera.main;
        _tagSet = new HashSet<string>(tags);
        TryGetComponent(out _particleSystem);
    }

    private void OnParticleCollision(GameObject other)
    {
        if (!enabled) return;

        if (other.TryGetComponent(out IIgnorableParticles ignorableParticles))
        {
            return;
        }

        // Свои частицы не бьют хозяина (босс с атаками-частицами внутри)
        if (transform.IsChildOf(other.transform)) return;

        // Получаем точные координаты столкновения
        int numCollisionEvents = _particleSystem.GetCollisionEvents(other, _collisionEvents);
        
        if (numCollisionEvents > 0)
        {
            // Берем точку первого коснувшегося партикла
            Vector3 hitPoint = _collisionEvents[0].intersection;
            Damage(other, hitPoint);
        }
    }

    protected virtual void Damage(GameObject other, Vector3 hitPoint)
    {
        // Ультра-удар врага не должен задевать своих - только цели из тегов
        if (_unblockable && _tagSet.Count > 0 && !_tagSet.Contains(other.tag)) return;
        if (other.TryGetComponent(out IDamagable damagable))
        {
            damagable?.OnDamage(_damage, _unblockable, gameObject);
            SpawnDamageText(hitPoint);
        }
    }

    // Удар дошёл до цели (не ушёл в щит/блок/кулдаун) - только тогда вешать дебаффы
    protected bool ApplyDamage(IDamagable damagable) => ApplyDamage(damagable, _damage, _unblockable, gameObject);

    public static bool ApplyDamage(IDamagable damagable, float damage, bool unblockable, GameObject source)
    {
        bool applied = false;
        System.Action<float, GameObject> onDamaged = (_, _) => applied = true;
        damagable.damaged += onDamaged;
        damagable.OnDamage(damage, unblockable, source);
        damagable.damaged -= onDamaged;
        return applied;
    }

    protected void SpawnDamageText(Vector3 position)
    {
        
        if (!_damageTextReference.RuntimeKeyIsValid())
        {
            //Debug.LogWarning($"[ParticleDamage] На объекте {gameObject.name} не назначен _damageTextReference!");
            return;
        }
        
        // Пул эффектов переживает смену сцены: камеру берём заново, если старая уничтожена.
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;

        _damageTextReference.InstantiateAsync(position, _camera.transformHandle.rotation).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                if (handle.Result.TryGetComponent(out DamageText damageText))
                {
                    damageText.StartVisual(_damage, _damageTextReference);
                }
            }
        };
    }

}