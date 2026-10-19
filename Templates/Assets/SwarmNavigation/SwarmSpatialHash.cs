using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PG.SwarmNavigation
{
    // Bucket-грид на NativeParallelMultiHashMap для локального избегания (SwarmSeparationJob) и LOD
    // (SwarmLodScheduler). Используется как поле джоба напрямую, не оборачивается в NativeArray<T> -
    // именно это (вложенный нативный контейнер КАК ТИП ЭЛЕМЕНТА другого контейнера) запрещено джоб-
    // системой и заставило легаси-систему считать в один поток, а не сам факт использования
    // NativeParallelMultiHashMap полем джоба - это стандартный поддерживаемый паттерн.
    public class SwarmSpatialHash : System.IDisposable
    {
        public float BucketSize { get; }
        public NativeParallelMultiHashMap<int, int> Buckets => _buckets;

        private NativeParallelMultiHashMap<int, int> _buckets;

        public SwarmSpatialHash(float bucketSize, int initialCapacity)
        {
            BucketSize = bucketSize;
            _buckets = new NativeParallelMultiHashMap<int, int>(initialCapacity, Allocator.Persistent);
        }

        public void Clear() => _buckets.Clear();

        // includeFar = false: дальние агенты (Far) в хэш не попадают - их разведение не считается, строить для них хэш незачем.
        public JobHandle ScheduleBuild(NativeArray<SwarmAgentState> agents, int agentCount, JobHandle dependency, bool includeFar = true)
        {
            Clear();
            if (_buckets.Capacity < agentCount) _buckets.Capacity = agentCount;

            BuildHashJob job = new BuildHashJob
            {
                Agents = agents,
                Writer = _buckets.AsParallelWriter(),
                BucketSize = BucketSize,
                IncludeFar = includeFar
            };
            return job.Schedule(agentCount, 64, dependency);
        }

        public int BucketKey(float3 worldPos) => BucketKeyOf(worldPos, BucketSize);

        public static int BucketKeyOf(float3 worldPos, float bucketSize)
        {
            int2 cell = (int2)math.floor(worldPos.xz / bucketSize);
            return BucketKeyFromCell(cell);
        }

        // Не криптографический хэш - просто разводит соседние bucket-ячейки по разным ключам
        // хэш-мапы; классические простые множители для 2D spatial hashing.
        public static int BucketKeyFromCell(int2 cell) => cell.x * 73856093 ^ cell.y * 19349663;

        public void Dispose()
        {
            if (_buckets.IsCreated) _buckets.Dispose();
        }

        [BurstCompile]
        private struct BuildHashJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SwarmAgentState> Agents;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter Writer;
            public float BucketSize;
            public bool IncludeFar;

            public void Execute(int index)
            {
                if (!IncludeFar && Agents[index].Tier == SwarmLodTier.Far) return;

                int key = BucketKeyOf(Agents[index].Position, BucketSize);
                Writer.Add(key, index);
            }
        }
    }
}
