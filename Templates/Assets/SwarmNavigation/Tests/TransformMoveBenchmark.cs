using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Jobs;

namespace PG.SwarmNavigation
{
    // Контрольный замер без навигации: N кубов (те же примитив/материал, те же случайные точки NavMesh, что у SwarmCrowdStressTest),
    // которые двигает только IJobParallelForTransform по прямой. Показывает, сколько кадра стоит сам Unity на N двигающихся GameObject.
    public class TransformMoveBenchmark : MonoBehaviour
    {
        public static int ForceCount = 20000;
        public static bool ForceMove = true;

        private TransformAccessArray _transforms;
        private NativeArray<float3> _origin;
        private NativeArray<float3> _direction;
        private GameObject[] _objects;
        private bool _move;
        private int _frames;
        private double _sumMs;

        private bool _ready;
        private int _warmup;

        private System.Collections.IEnumerator Start()
        {
            int count = ForceCount;
            _move = ForceMove;

            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Material material = template.GetComponent<MeshRenderer>().sharedMaterial;
            Destroy(template);

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            System.Random rng = new System.Random(12345);

            _objects = new GameObject[count];
            _transforms = new TransformAccessArray(count, 128);
            _origin = new NativeArray<float3>(count, Allocator.Persistent);
            _direction = new NativeArray<float3>(count, Allocator.Persistent);
            for (int i = 0; i < count; i++)
            {
                if (i % 2000 == 1999) yield return null;   // порциями, чтобы не повесить кадр и eval
                int t = rng.Next(tri.indices.Length / 3) * 3;
                Vector3 p = (tri.vertices[tri.indices[t]] + tri.vertices[tri.indices[t + 1]] + tri.vertices[tri.indices[t + 2]]) / 3f + Vector3.up * 0.25f;
                GameObject go = new GameObject("TMB_" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.transform.localScale = Vector3.one * 0.5f;
                go.transform.position = p;
                _objects[i] = go;
                _transforms.Add(go.transformHandle);
                _origin[i] = p;
                float angle = (float)(rng.NextDouble() * math.PI * 2.0);
                _direction[i] = new float3(math.cos(angle), 0f, math.sin(angle));
            }
            _ready = true;
            Debug.Log($"TMB готов: {count} кубов, движение {_move}");
        }

        private void Update()
        {
            if (!_ready) return;

            if (_move)
            {
                new MoveJob { Origin = _origin, Direction = _direction, Time = Time.time, Speed = 3.5f }.Schedule(_transforms).Complete();
            }

            // Первые кадры (компиляция Burst, шейдеры, прогрев) не считаем.
            if (_warmup < 120) { _warmup++; return; }

            _frames++;
            _sumMs += Time.unscaledDeltaTime * 1000.0;
            if (_frames < 120) return;

            Debug.Log($"TMB N={_objects.Length} движение {_move} кадр {_sumMs / _frames:F1} мс ({1000.0 / (_sumMs / _frames):F0} FPS)");
            _frames = 0;
            _sumMs = 0;
        }

        private void OnDestroy()
        {
            if (_transforms.isCreated) _transforms.Dispose();
            if (_origin.IsCreated) _origin.Dispose();
            if (_direction.IsCreated) _direction.Dispose();
            if (_objects != null) foreach (GameObject go in _objects) if (go != null) Destroy(go);
        }

        [BurstCompile]
        private struct MoveJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<float3> Origin;
            [ReadOnly] public NativeArray<float3> Direction;
            public float Time, Speed;

            public void Execute(int index, TransformAccess transform)
            {
                // Туда-обратно на 60 м, чтобы кубы не уходили далеко от сцены.
                float phase = math.abs(math.frac(Time * Speed / 120f + index * 0.0137f) * 2f - 1f) * 60f;
                float3 p = Origin[index] + Direction[index] * phase;
                transform.SetPositionAndRotation(new Vector3(p.x, p.y, p.z), quaternion.LookRotationSafe(Direction[index], math.up()));
            }
        }
    }
}
