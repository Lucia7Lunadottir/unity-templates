using UnityEngine;

namespace PG.SaveSystem
{
    public interface ISaveableGuid
    {
        string guid { get; set; }
        GameObject gameObject { get; }
        public void SetGuid();
    }
}