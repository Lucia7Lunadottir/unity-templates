using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PG.Rendering
{
    // Один identity-буфер на весь проект вместо компонента на каждый объект - на 200k+ объектов
    // компонент+MaterialPropertyBlock на каждый был бы костылём (свой SetPropertyBlock вызов и своя
    // GetPropertyBlock проверка на объект, ломает батчинг). _InstanceBuffer в IndirectInstancing.hlsl -
    // обычный shader-глобал файла, не Blackboard-property (см. комментарий там же), поэтому
    // Shader.SetGlobalBuffer виден сразу всем материалам с ApplyInstanceTransform, без единого
    // вызова на объект. Индиректные батчи (IndirectRenderBatch/IndirectMeshRenderer) как и раньше
    // перекрывают его своим MaterialPropertyBlock на конкретный draw call - конфликта нет.
    //
    // [InitializeOnLoad] (Edit Mode - Scene View, без Play) + [RuntimeInitializeOnLoadMethod] (Play/билд) -
    // разные жизненные циклы, одно не подменяет другое. RuntimeInitializeOnLoadMethod НЕ выполняется
    // в Edit Mode вообще - без InitializeOnLoad обычный MeshRenderer с этим шейдером был бы пуст/чёрным
    // в Scene View до первого нажатия Play.
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class IndirectInstanceGlobalDefault
    {
        private static readonly int InstanceBufferId = Shader.PropertyToID("_InstanceBuffer");
        private static GraphicsBuffer s_identityBuffer;

#if UNITY_EDITOR
        static IndirectInstanceGlobalDefault()
        {
            // Не вызывать Bind() синхронно отсюда - статический конструктор [InitializeOnLoad]
            // выполняется очень рано при загрузке домена редактора, раньше, чем графическое
            // устройство гарантированно готово. Создание GraphicsBuffer в этот момент падало молча
            // (см. Vulkan-ошибку "_InstanceBuffer... none provided" - буфер так и не создавался).
            // delayCall откладывает на следующий тик редактора, когда GfxDevice точно инициализирован.
            EditorApplication.delayCall += Bind;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bind()
        {
            if (s_identityBuffer != null) return; // уже забинжено (Edit Mode delayCall или предыдущий вызов)

            var identity = new[]
            {
                new InstanceData { Position = Vector3.zero, Scale = 1f, Rotation = Quaternion.identity }
            };
            s_identityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, InstanceData.Stride);
            s_identityBuffer.SetData(identity);
            Shader.SetGlobalBuffer(InstanceBufferId, s_identityBuffer);

            Application.quitting += Dispose;
        }

        private static void Dispose()
        {
            Application.quitting -= Dispose;
            s_identityBuffer?.Dispose();
            s_identityBuffer = null;
        }
    }
}
