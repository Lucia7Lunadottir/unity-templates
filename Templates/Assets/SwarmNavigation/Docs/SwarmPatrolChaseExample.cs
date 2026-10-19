using System;
using PG.SwarmNavigation;
using UnityEngine;

namespace PG.SwarmNavigation.Docs
{
    // Патруль -> погоня -> атака для толпы SwarmAgent БЕЗ Update() на агента: один менеджер, один цикл
    // по массиву. Навигацию (куда идти) делает SwarmNav, здесь только решения "что делать сейчас".
    // Патруль - точки на NavMesh (у каждого агента свой путь), погоня - цель-Transform (позиция игрока читается
    // каждый кадр один раз на всех, путь агента пересчитывается, когда игрок заметно сместился).
    public class SwarmPatrolChaseExample : MonoBehaviour
    {
        private enum State : byte { Patrol, Chase, Attack }

        [SerializeField] private SwarmAgent[] agents;
        [SerializeField] private Transform player;
        [SerializeField] private float sightRange = 25f;
        [SerializeField] private float attackRange = 1.8f;
        [SerializeField] private float patrolRadius = 40f;
        [SerializeField] private float attackCooldown = 1f;

        // Индекс агента в массиве agents, нанёсшего удар - урон применяйте в обработчике.
        public event Action<int> Attacked;

        private TransformHandle[] _handles;
        private TransformHandle _playerHandle;
        private State[] _state;
        private float[] _nextAttackTime;

        private void Start()
        {
            _playerHandle = player.transformHandle;
            _handles = new TransformHandle[agents.Length];
            _state = new State[agents.Length];
            _nextAttackTime = new float[agents.Length];

            for (int i = 0; i < agents.Length; i++)
            {
                _handles[i] = agents[i].transformHandle;
                agents[i].stoppingDistance = attackRange * 0.9f; // в погоне агент остановится на дистанции удара
                StartPatrol(i);
            }
        }

        private void Update()
        {
            Vector3 playerPosition = _playerHandle.position;
            float sightSq = sightRange * sightRange;
            float loseSq = sightSq * 2.25f;      // потеряли игрока на 1.5x дальше, чем заметили - гистерезис
            float attackSq = attackRange * attackRange;
            float releaseSq = attackSq * 1.44f;  // вышел из зоны удара на 1.2x - снова погоня

            for (int i = 0; i < agents.Length; i++)
            {
                float distanceSq = (playerPosition - _handles[i].position).sqrMagnitude;

                switch (_state[i])
                {
                    case State.Patrol:
                        if (distanceSq <= sightSq) StartChase(i);
                        else if (!agents[i].hasDestination || agents[i].remainingDistance <= agents[i].stoppingDistance + 0.5f) PickPatrolPoint(i);
                        break;

                    case State.Chase:
                        if (distanceSq > loseSq) StartPatrol(i);
                        else if (distanceSq <= attackSq) StartAttack(i);
                        break;

                    case State.Attack:
                        if (distanceSq > releaseSq)
                        {
                            agents[i].isStopped = false;
                            StartChase(i);
                        }
                        else if (Time.time >= _nextAttackTime[i])
                        {
                            _nextAttackTime[i] = Time.time + attackCooldown;
                            Attacked?.Invoke(i);
                        }
                        break;
                }
            }
        }

        private void StartPatrol(int i)
        {
            _state[i] = State.Patrol;
            PickPatrolPoint(i);
        }

        private void PickPatrolPoint(int i)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * patrolRadius;
            Vector3 point = _handles[i].position + new Vector3(offset.x, 0f, offset.y);
            agents[i].SetDestination(point);
        }

        private void StartChase(int i)
        {
            _state[i] = State.Chase;
            agents[i].SetDestination(_playerHandle); // цель читается из Transform каждый кадр сама
        }

        private void StartAttack(int i)
        {
            _state[i] = State.Attack;
            agents[i].isStopped = true;
        }
    }
}
