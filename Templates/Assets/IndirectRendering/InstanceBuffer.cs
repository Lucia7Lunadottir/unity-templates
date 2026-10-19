using UnityEngine;

namespace PG.Rendering
{
    // GPU-хранилище позиций/масштаба/поворота инстансов + минимальный API для их подвижки.
    // Не знает про culling, LOD или рендер - только данные. Docs/02-architecture.html.
    public class InstanceBuffer : System.IDisposable
    {
        public GraphicsBuffer Buffer { get; }
        public int Count => _data.Length;

        private readonly InstanceData[] _data;

        public InstanceBuffer(InstanceData[] data)
        {
            _data = data;
            Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, InstanceData.Stride);
            Buffer.SetData(data);
        }

        public InstanceData Get(int index) => _data[index];

        public void SetPosition(int index, Vector3 worldPosition)
        {
            _data[index].Position = worldPosition;
            Buffer.SetData(_data, index, index, 1);
        }

        public void Offset(Vector3 delta)
        {
            for (int i = 0; i < _data.Length; i++)
                _data[i].Position += delta;
            Buffer.SetData(_data);
        }

        public void Dispose()
        {
            Buffer?.Dispose();
        }
    }
}
