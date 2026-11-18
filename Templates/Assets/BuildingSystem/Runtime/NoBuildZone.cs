using System.Collections.Generic;
using UnityEngine;

// Здесь строить нельзя (головоломки, сюжетные места): объём-триггер, Build Mode не ставит в нём постройки.
// Коллайдер - Box/Sphere/Capsule или выпуклый Mesh.
[RequireComponent(typeof(Collider))]
public class NoBuildZone : MonoBehaviour
{
    private static readonly List<NoBuildZone> Zones = new List<NoBuildZone>();

    private Collider _collider;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        _collider.isTrigger = true;
    }

    private void OnEnable() => Zones.Add(this);
    private void OnDisable() => Zones.Remove(this);

    // Пересекается ли объём постройки с какой-нибудь зоной
    public static bool Blocks(Bounds bounds)
    {
        foreach (var zone in Zones)
            if (zone != null && zone._collider.bounds.Intersects(bounds)) return true;
        return false;
    }

    private void OnDrawGizmos()
    {
        var c = GetComponent<Collider>();
        if (c == null) return;
        Gizmos.color = new Color(0.85f, 0.5f, 0.42f, 0.25f);
        Gizmos.DrawCube(c.bounds.center, c.bounds.size);
    }
}
