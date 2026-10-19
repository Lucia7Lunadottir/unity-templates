using System;
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace PG
{
    [CustomTimelineEditor(typeof(ParticleSystemControlTrack))]
    public class ParticleSystemControlClipEditor : TrackEditor
    {
        public override void OnCreate(TrackAsset track, TrackAsset copiedFrom)
        {
            base.OnCreate(track, copiedFrom);
            if (copiedFrom == null && track.isEmpty)
            {
                track.CreateClip<ParticleSystemControlClip>();
            }
        }

        public override TrackDrawOptions GetTrackOptions(TrackAsset track, UnityEngine.Object binding)
        {
            var options = base.GetTrackOptions(track, binding);
            
            // Check if multiple tracks control the same ParticleSystem binding
            var director = TimelineEditor.inspectedDirector;
            if (director != null && binding is ParticleSystem ps)
            {
                var timeline = director.playableAsset as TimelineAsset;
                if (timeline != null)
                {
                    var siblingTracks = timeline.GetOutputTracks()
                        .Where(t => t != track && t is ParticleSystemControlTrack && director.GetGenericBinding(t) == ps);

                    if (siblingTracks.Any())
                    {
                        options.errorText = L10n.Tr("Multiple tracks are controlling the same ParticleSystem component. This may lead to unexpected behavior.");
                    }
                }
            }
            return options;
        }
    }

    [CustomTimelineEditor(typeof(ParticleSystemControlClip))]
    public class VisualEffectControlClipEditor : ClipEditor
    {
        public override void OnClipChanged(TimelineClip clip)
        {
            var controlClip = clip.asset as ParticleSystemControlClip;
            if (controlClip != null)
            {
                clip.displayName = "Particle Control";
            }
        }

        public static void ShadowLabel(Rect rect, GUIContent content, GUIStyle textStyle, GUIStyle shadowStyle)
        {
            var shadowRect = rect;
            shadowRect.xMin += Style.kShadowDistance;
            shadowRect.yMin += Style.kShadowDistance;
            shadowRect.width += Style.kShadowDistance;
            shadowRect.height += Style.kShadowDistance;
            GUI.Label(shadowRect, content, shadowStyle);
            GUI.Label(rect, content, textStyle);
        }

        static class Content
        {
            public static GUIContent scrubbingDisabled = new GUIContent("Scrubbing Disabled");
            public static GUIContent particleIcon = EditorGUIUtility.IconContent("d_ParticleSystem Icon");
        }

        static class Style
        {
            public static readonly Color kDarkGlobalBackground = new Color32(45, 45, 45, 255);
            public static readonly Color kDarkScrubbingBackground = new Color32(35, 35, 35, 255);
            public static readonly Color kLightGlobalBackground = new Color32(180, 180, 180, 255);
            public static readonly Color kLightScrubbingBackground = new Color32(150, 150, 150, 255);

            public static GUIStyle scrubbingDisabled = new GUIStyle(GUI.skin.GetStyle("Label"));
            public static GUIStyle scrubbingDisabledShadow = new GUIStyle(GUI.skin.GetStyle("Label"));

            public static readonly float kShadowDistance = 1.0f;
            public static readonly float kScrubbingBarHeight = 16.0f;

            static Style()
            {
                Color textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
                scrubbingDisabled.normal.textColor = textColor;
                scrubbingDisabled.alignment = TextAnchor.MiddleCenter;
                scrubbingDisabled.fontStyle = FontStyle.Bold;
                scrubbingDisabled.fontSize = 10;

                scrubbingDisabledShadow.normal.textColor = new Color(0, 0, 0, 0.5f);
                scrubbingDisabledShadow.alignment = TextAnchor.MiddleCenter;
                scrubbingDisabledShadow.fontStyle = FontStyle.Bold;
                scrubbingDisabledShadow.fontSize = 10;
            }
        }

        public override void DrawBackground(TimelineClip clip, ClipBackgroundRegion region)
        {
            var controlClip = clip.asset as ParticleSystemControlClip;
            if (controlClip == null) return;

            // Draw clean background colors based on skin preference
            Color backgroundColor = EditorGUIUtility.isProSkin ? Style.kDarkGlobalBackground : Style.kLightGlobalBackground;
            EditorGUI.DrawRect(region.position, backgroundColor);

            // Render absolute status indicator if scratching/scrubbing is disabled
            if (!controlClip.scrubbing)
            {
                var scrubbingRegion = new Rect(region.position.x, region.position.yMax - Style.kScrubbingBarHeight, region.position.width, Style.kScrubbingBarHeight);
                Color scrubbingColor = EditorGUIUtility.isProSkin ? Style.kDarkScrubbingBackground : Style.kLightScrubbingBackground;
                EditorGUI.DrawRect(scrubbingRegion, scrubbingColor);
                ShadowLabel(scrubbingRegion, Content.scrubbingDisabled, Style.scrubbingDisabled, Style.scrubbingDisabledShadow);
            }

            // Draw crisp borders to highlight clip boundaries
            Color lineIndicatorColor = new Color32(26, 171, 104, 255); // Emerald timeline theme accent
            Rect startLine = new Rect(region.position.x, region.position.y, 2f, region.position.height);
            Rect endLine = new Rect(region.position.xMax - 2f, region.position.y, 2f, region.position.height);
            EditorGUI.DrawRect(startLine, lineIndicatorColor);
            EditorGUI.DrawRect(endLine, lineIndicatorColor);

            // Draw a built-in particle icon in the center of the clip
            if (Content.particleIcon != null && Content.particleIcon.image != null)
            {
                var iconSize = 16f;
                var iconRect = new Rect(
                    region.position.x + (region.position.width - iconSize) * 0.5f,
                    region.position.y + (region.position.height - iconSize) * 0.5f,
                    iconSize,
                    iconSize
                );
                GUI.DrawTexture(iconRect, Content.particleIcon.image, ScaleMode.ScaleToFit, true, 1.0f, lineIndicatorColor, 0, 0);
            }

            base.DrawBackground(clip, region);
        }
    }
}