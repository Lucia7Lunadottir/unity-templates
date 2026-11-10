using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace PG.TimelineTween
{
    [CustomTimelineEditor(typeof(TransformTweenClip))]
    public class TransformTweenClipEditor : ClipEditor
    {
        // New clips start at the end of the previous tween, or at the bound object's current pose.
        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clonedFrom != null || clip.asset is not TransformTweenClip tween)
                return;

            tween.tweenDuration = (float)clip.duration;
            Transform target = GetTarget(track);
            Transform parent = target != null ? target.parent : null;

            TransformTweenClip previous = FindPrevious(clip, track);
            Vector3 position;
            Quaternion rotation;
            if (previous != null)
                previous.GetPoint(false, parent, out position, out rotation);
            else if (target != null)
            {
                position = target.position;
                rotation = target.rotation;
            }
            else
                return;

            if (previous != null)
                tween.space = previous.space;
            tween.SetPoint(true, parent, position, rotation);
            tween.SetPoint(false, parent, position, rotation);
        }

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            var options = base.GetClipOptions(clip);
            if (clip.asset is TransformTweenClip tween)
                options.tooltip = $"{tween.space} · {tween.timing}" + (tween.timing == TweenTiming.Fixed ? $" {tween.tweenDuration:0.##}s" : "");
            return options;
        }

        internal static Transform GetTarget(TrackAsset track)
        {
            var director = TimelineEditor.inspectedDirector;
            return director != null && track != null ? director.GetGenericBinding(track) as Transform : null;
        }

        static TransformTweenClip FindPrevious(TimelineClip clip, TrackAsset track)
        {
            TimelineClip best = null;
            foreach (var other in track.GetClips())
            {
                if (other == clip || other.asset is not TransformTweenClip || other.start > clip.start)
                    continue;
                if (best == null || other.end > best.end)
                    best = other;
            }
            return best?.asset as TransformTweenClip;
        }
    }

    [InitializeOnLoad]
    static class TransformTweenSceneHandles
    {
        static readonly Color StartColor = new Color(0.3f, 0.9f, 0.4f);
        static readonly Color EndColor = new Color(0.95f, 0.4f, 0.3f);

        static TransformTweenSceneHandles() => SceneView.duringSceneGui += OnSceneGUI;

        static void OnSceneGUI(SceneView view)
        {
            var clips = TimelineEditor.selectedClips;
            if (clips == null)
                return;

            foreach (var clip in clips)
            {
                if (clip.asset is not TransformTweenClip tween)
                    continue;

                Transform target = TransformTweenClipEditor.GetTarget(clip.GetParentTrack());
                Transform parent = target != null ? target.parent : null;

                tween.GetPoint(true, parent, out var startPosition, out _);
                tween.GetPoint(false, parent, out var endPosition, out _);
                Handles.color = Color.white;
                Handles.DrawDottedLine(startPosition, endPosition, 4f);

                DrawPoint(tween, parent, true, "Start", StartColor);
                DrawPoint(tween, parent, false, "End", EndColor);
            }
        }

        static void DrawPoint(TransformTweenClip tween, Transform parent, bool start, string label, Color color)
        {
            tween.GetPoint(start, parent, out var position, out var rotation);

            Handles.color = color;
            Handles.SphereHandleCap(0, position, rotation, HandleUtility.GetHandleSize(position) * 0.12f, EventType.Repaint);
            Handles.Label(position, label);

            EditorGUI.BeginChangeCheck();
            if (Tools.current == Tool.Rotate)
                rotation = Handles.RotationHandle(rotation, position);
            else
                position = Handles.PositionHandle(position, Tools.pivotRotation == PivotRotation.Local ? rotation : Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(tween, start ? "Move Tween Start" : "Move Tween End");
            tween.SetPoint(start, parent, position, rotation);
            EditorUtility.SetDirty(tween);
            TimelineEditor.Refresh(RefreshReason.SceneNeedsUpdate);
        }
    }
}
