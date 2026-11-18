using UnityEngine;
using PG.HealthSystem;

public class LootMob : MonoBehaviour
{
    [SerializeField] private bool _isDisableSelfObject;
    [SerializeField] private LootElement[] _prefabs;
    [SerializeField] private Vector2 _launchVelocity = new Vector2(3f, 5f);
    [SerializeField] private float _offsetY = 0.2f;
    
    [System.Serializable]
    public struct LootElement
    {
        public GameObject prefab;
        public int count;
    }
    [SerializeField] private GameObject _deathObject;
    private IDeath _death;

    private void Awake()
    {
        _deathObject.TryGetComponent(out _death);
    }
    private void OnEnable()
    {
        _death.dead += OnDeath;
    }
    private void OnDisable()
    {
        _death.dead -= OnDeath;
    }

    void OnDeath()
    {
        int totalLootCount = 0;
        for (int i = 0; i < _prefabs.Length; i++)
        {
            if (_prefabs[i].prefab != null) 
                totalLootCount += _prefabs[i].count;
        }

        if (totalLootCount == 0) return;

        int currentItemIndex = 0;
        float radius = 0.4f;

        for (int i = 0; i < _prefabs.Length; i++)
        {
            if (_prefabs[i].prefab == null) continue;

            for (int a = 0; a < _prefabs[i].count; a++)
            {
                // 2. Высчитываем угол для текущего предмета
                float angle = currentItemIndex * (360f / totalLootCount) * Mathf.Deg2Rad;
                
                // Сдвиг по осям X и Z (горизонтальная плоскость)
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                
                offset.y = _offsetY; 

                Vector3 spawnPosition = transform.position + offset;
                GameObject spawnedLoot = Instantiate(_prefabs[i].prefab, spawnPosition, Random.rotation);

                // 3. Физика разлета
                if (spawnedLoot.TryGetComponent<Rigidbody>(out var rb))
                {
                    // Направление импульса: от центра круга + немного вверх
                    Vector3 pushDirection = (offset.normalized + Vector3.up * 0.7f).normalized;
                    
                    // Случайная скорость разлета
                    float launchVelocity = Random.Range(_launchVelocity.x, _launchVelocity.y); 
                    
                    // VelocityChange меняет скорость НАПРЯМУЮ, игнорируя массу (mass) Rigidbody!
                    rb.AddForce(pushDirection * launchVelocity, ForceMode.VelocityChange);
                }

                currentItemIndex++;
            }
        }

        if (_isDisableSelfObject)
        {
            gameObject.SetActive(false);
        }
    }
}