using System.Collections.Generic;
using PG.SwarmNavigation;
using Unity.Mathematics;
using UnityEngine;

// Отладка SwarmNav без правок самого API: всё считается по публичному SwarmNav.Agents.
// Повесьте на любой GameObject. Работает в Play Mode, когда SwarmNav.Instance уже создан.
//  - OnGUI: счётчики (агенты, тиры, стоящие, пути за кадр, ожидание джобов);
//  - Gizmos (Scene View): точка агента, цвет по тиру, линия по Velocity;
//  - ПКМ в Game View: инспектор ближайшего агента (все поля SwarmAgentState).
public class SwarmNavDebugOverlay : MonoBehaviour
{
    [Header("Gizmos")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private int maxGizmoAgents = 2000;
    [SerializeField] private float steerLineLength = 1.5f;

    [Header("HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private float refreshInterval = 0.25f; // счётчики пересчитываются не каждый кадр

    // Призраки (неиспользуемые слоты) стоят в (1e6, 0, 1e6) - см. SwarmNav.MakeGhost.
    private const float GhostThreshold = 1e5f;

    private int _active, _near, _mid, _far, _idle;
    private float _nextRefresh;
    private int _selectedSlot = -1;

    private void Update()
    {
        var nav = SwarmNav.Instance;
        if (nav == null) return;

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + refreshInterval;
            RecountStats(nav.Agents);
        }

        if (Input.GetMouseButtonDown(1))
        {
            _selectedSlot = PickNearestAgentToMouse(nav.Agents);
        }
    }

    private void RecountStats(Unity.Collections.NativeArray<SwarmAgentState> agents)
    {
        _active = _near = _mid = _far = _idle = 0;

        for (var i = 0; i < agents.Length; i++)
        {
            var a = agents[i];
            if (a.Position.x > GhostThreshold) continue; // пустой слот

            _active++;
            switch (a.Tier)
            {
                case SwarmLodTier.Near: _near++; break;
                case SwarmLodTier.Mid: _mid++; break;
                default: _far++; break;
            }

            if (math.lengthsq(a.Velocity) < 1e-6f) _idle++;
        }

    }

    private int PickNearestAgentToMouse(Unity.Collections.NativeArray<SwarmAgentState> agents)
    {
        var cam = Camera.main;
        if (cam == null) return -1;

        var best = -1;
        var bestPixels = 40f; // радиус выбора в пикселях
        var mouse = (Vector2)Input.mousePosition;

        for (var i = 0; i < agents.Length; i++)
        {
            var p = agents[i].Position;
            if (p.x > GhostThreshold) continue;

            var sp = cam.WorldToScreenPoint((Vector3)p);
            if (sp.z <= 0f) continue;

            var d = Vector2.Distance(mouse, new Vector2(sp.x, sp.y));
            if (d < bestPixels) { bestPixels = d; best = i; }
        }
        return best;
    }

    private void OnGUI()
    {
        if (!showHud) return;
        var nav = SwarmNav.Instance;
        if (nav == null)
        {
            GUI.Label(new Rect(10, 10, 400, 22), "SwarmNav.Instance == null (ещё не создан или уже уничтожен)");
            return;
        }

        var text =
            $"SwarmNav\n" +
            $"Активных агентов: {_active}  (слотов всего: {nav.Agents.Length})\n" +
            $"Tier  Near/Mid/Far: {_near} / {_mid} / {_far}\n" +
            $"Путей за кадр: {nav.PathsLastFrame}  ожидание джобов: {nav.LastWaitMs:0.00} мс\n" +
            $"Steering == 0 (стоят/упёрлись): {_idle}\n" +
            $"ПКМ по агенту - инспектор";
        GUI.Box(new Rect(10, 10, 330, 118), text);

        if (_selectedSlot >= 0 && _selectedSlot < nav.Agents.Length)
        {
            var a = nav.Agents[_selectedSlot];
            var info =
                $"Агент slot={_selectedSlot}  gen={a.Generation}\n" +
                $"Position: {a.Position}\n" +
                $"Velocity: {a.Velocity}  |{math.length(a.Velocity):0.00}|\n" +
                $"Speed: {a.Speed}\n" +
                $"Tier: {a.Tier}";
            GUI.Box(new Rect(10, 136, 380, 78), info);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying) return;
        var nav = SwarmNav.Instance;
        if (nav == null) return;

        var agents = nav.Agents;
        var drawn = 0;
        for (var i = 0; i < agents.Length && drawn < maxGizmoAgents; i++)
        {
            var a = agents[i];
            if (a.Position.x > GhostThreshold) continue;
            drawn++;

            Gizmos.color = a.Tier == SwarmLodTier.Near ? Color.green
                         : a.Tier == SwarmLodTier.Mid ? Color.yellow : Color.red;
            var p = (Vector3)a.Position;
            Gizmos.DrawSphere(p + Vector3.up * 0.1f, i == _selectedSlot ? 0.35f : 0.12f);

            if (math.lengthsq(a.Velocity) > 1e-6f)
            {
                Gizmos.DrawLine(p, p + (Vector3)(a.Velocity * steerLineLength));
            }
        }
    }
}
