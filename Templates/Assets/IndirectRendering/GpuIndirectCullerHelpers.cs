using UnityEngine;

namespace PG.Rendering
{
    // Общие статические куски, нужные и GpuIndirectCuller (GPU-путь), и гизмо-диагностике
    // (CPU-путь) - вынесены сюда, чтобы не держать две независимые копии одной и той же математики.
    public static class GpuIndirectCullerHelpers
    {
        // meshLod=0 (по умолчанию) - НЕ "проигнорировать Mesh LOD", а ровно то, что реально
        // нарисует Graphics.RenderMeshIndirect: эти indirect-API не поддерживают автовыбор LOD по
        // расстоянию и всегда форсируют LOD0 (что RenderParams.forceMeshLod не задан вообще, что
        // явно -1 - см. доки RenderParams.forceMeshLod, для indirect-путей это синонимы). А вот у
        // Mesh.GetIndexCount(sub, meshLod)/GetIndexStart они НЕ синонимы: -1 отдаёт размер всего
        // ОБЪЕДИНЁННОГО буфера индексов по ВСЕМ запечённым Mesh LOD уровням сразу (не "весь меш",
        // а буквально LOD0+LOD1+LOD2+... одним числом), а 0 - честный размер именно LOD0. Если
        // здесь передать -1 (как раньше), command buffer думает, что индексов больше, чем реально
        // относится к LOD0, GPU дочитывает данные СОСЕДНИХ LOD-уровней как продолжение той же
        // геометрии - обрубленный/мусорный меш. Для меша без запечённых LOD 0 и -1 дают одно и то
        // же число, так что 0 по умолчанию всегда безопасно, LOD там или нет.
        public static GraphicsBuffer BuildCommandBuffer(Mesh mesh, int instanceCount, int meshLod = 0)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            buffer.SetData(new[]
            {
                new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = mesh.GetIndexCount(0, meshLod),
                    instanceCount = (uint)instanceCount,
                    startIndex = mesh.GetIndexStart(0, meshLod),
                    baseVertexIndex = mesh.GetBaseVertex(0), // нет meshLod-перегрузки - вершинный буфер общий на все LOD
                    startInstance = 0
                }
            });
            return buffer;
        }

        // camera.projectionMatrix - условная "Unity"-конвенция, не конвенция реальной GPU-платформы.
        // Без этой коррекции сравнение с аппаратным depth-буфером в occlusion-тесте систематически
        // неверно. Docs/03-culling.html.
        public static Matrix4x4 BuildViewProjMatrix(Camera camera)
        {
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            return gpuProjection * camera.worldToCameraMatrix;
        }

        // CPU-копия IsInsideFrustum() из IndirectCulling.compute, для диагностических гизмо -
        // Docs/04-diagnostics.html.
        public static bool IsInsideFrustumCpu(Matrix4x4 viewProjection, Vector3 worldPos, float radius)
        {
            Vector4 clipPos = viewProjection * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);
            float w = clipPos.w + radius;

            bool inFront = w > 0f;
            bool withinDepth = clipPos.z <= w;
            bool withinX = Mathf.Abs(clipPos.x) <= w;
            bool withinY = Mathf.Abs(clipPos.y) <= w;
            return inFront && withinDepth && withinX && withinY;
        }
    }
}
