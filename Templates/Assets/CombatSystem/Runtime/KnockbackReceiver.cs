using System.Collections;
using PG.SwarmNavigation;
using UnityEngine;
using UnityEngine.AI;
using Unity.Burst;
using Unity.Mathematics;

public class KnockbackReceiver : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _defaultKnockbackForce = 5f;
    [SerializeField] private float _knockbackDuration = 0.2f;
    [SerializeField] private float _stillThreshold = 0.05f;
    [SerializeField] private string[] _damagingTags = { "Player", "Weapon", "Magic" };

    [Header("References")]
    [SerializeField] private NavMeshAgent _agent;
    [SerializeField] private SwarmAgent _swarmAgent;

    [SerializeField] private Rigidbody _rigidbody;
    
    private Coroutine _knockbackCoroutine;
    private TransformHandle _handle;

    private void Awake()
    {
        if (_agent == null) TryGetComponent(out _agent);
        if (_swarmAgent == null) TryGetComponent(out _swarmAgent);
        if (_rigidbody == null) TryGetComponent(out _rigidbody);
        // Со SwarmAgent NavMeshAgent выключен навсегда (AINavigation) - включать его после отбрасывания нельзя.
        if (_swarmAgent != null) _agent = null;
        _handle = transform.transformHandle;
    }

    public void ApplyKnockbackFrom(Transform source, float? forceStrength = null)
    {
        if (source == null) return;

        Vector3 direction = (transform.position - source.position).normalized;
        direction.y = 0;

        float finalForce = forceStrength ?? _defaultKnockbackForce;
        ApplyKnockback((float3)direction.normalized, finalForce);
    }

    private void OnParticleCollision(GameObject other)
    {
        Vector3 direction = (transform.position - other.transform.position).normalized;
        direction.y = 0;
        ApplyKnockback((float3)direction.normalized, _defaultKnockbackForce);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (HasValidTag(other.gameObject))
        {
            Vector3 direction = (transform.position - other.transform.position).normalized;
            direction.y = 0;
            ApplyKnockback((float3)direction, _defaultKnockbackForce);
        }
    }

    private bool HasValidTag(GameObject obj)
    {
        foreach (var t in _damagingTags)
        {
            if (obj.CompareTag(t)) return true;
        }
        return false;
    }

    private void ApplyKnockback(float3 direction, float force)
    {
        // Выключенного врага (AIManager/чанки) не толкаем: корутина на неактивном объекте не стартует
        if (!isActiveAndEnabled) return;
        if (_knockbackCoroutine != null) StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = StartCoroutine(KnockbackRoutine(direction, force));
    }

    private IEnumerator KnockbackRoutine(float3 direction, float force)
    {
        if (_agent != null) _agent.enabled = false;
        if (_swarmAgent != null) _swarmAgent.enabled = false;

        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
        _rigidbody.AddForce(direction * force);

        yield return new WaitForFixedUpdate();
        
        float knockBackTime = Time.time;

        // for (float i = 0; i < _knockbackDuration; i+= Time.deltaTime)
        // {
        //     // Вызываем Burst-метод из Главного Потока прямо внутри корутины
        //     KnockbackBurst.Step(ref _handle, direction, force, Time.deltaTime);
        //     yield return null;
        // }

        yield return new WaitUntil(() =>
            _rigidbody.linearVelocity.magnitude < _stillThreshold || Time.time > knockBackTime + _knockbackDuration);

        yield return new WaitForSeconds(0.25f);
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;
        

        if (_agent != null)
        {
            _agent.Warp(transformHandle.position);
            _agent.enabled = true;
            _agent.velocity = Vector3.zero;
        }
        if (_swarmAgent != null)
        {
            _swarmAgent.Warp(transformHandle.position);
            _swarmAgent.enabled = true;
        }

        _knockbackCoroutine = null;
    }

    // ── Вложенная Burst-функция ──────────────────────────────────────────────

    [BurstCompile]
    private static class KnockbackBurst
    {
        [BurstCompile]
        public static void Step(ref TransformHandle handle, in float3 direction, float force, float deltaTime)
        {
            handle.Translate(direction * force * deltaTime, Space.World);
        }
    }
}