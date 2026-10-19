using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace PG
{
    public class ParticleSystemControlTrackController
    {
        struct Event
        {
            public bool isCommandPlay;
            public double time;
        }

        struct Chunk
        {
            public bool scrubbing;
            public bool reinitEnter;
            public bool reinitExit;
            public uint startSeed;

            public double begin;
            public double end;

            public Event[] events;
        }

        private const int kErrorIndex = int.MinValue;
        private int m_LastChunk = kErrorIndex;
        private int m_LastEvent = kErrorIndex;
        private double m_LastPlayableTime = double.MinValue;
        private List<int> m_EventListIndexCache = new List<int>();

        private ParticleSystem m_Target;
        private bool m_BackupReseedOnPlay;
        private uint m_BackupStartSeed;
        private Chunk[] m_Chunks;

        private void OnEnterChunk(int currentChunk)
        {
            var chunk = m_Chunks[currentChunk];
            if (chunk.reinitEnter && Application.isPlaying)
            {
                m_Target.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                m_Target.useAutoRandomSeed = false;
                m_Target.randomSeed = chunk.startSeed;
                m_Target.Play(true);
            }
        }

        private void OnLeaveChunk(int previousChunkIndex, bool leavingGoingBeforeClip)
        {
            var previousChunk = m_Chunks[previousChunkIndex];
            if (previousChunk.reinitExit && Application.isPlaying)
            {
                // Stop emitting new particles but allow active ones to fade smoothly
                m_Target.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            else
            {
                ProcessNoScrubbingEvents(previousChunk, m_LastPlayableTime, leavingGoingBeforeClip ? double.NegativeInfinity : double.PositiveInfinity);
            }
            RestoreParticleState(previousChunk.scrubbing, previousChunk.reinitEnter);
        }

        private bool IsTimeInChunk(double time, int index)
        {
            var chunk = m_Chunks[index];
            return chunk.begin <= time && time < chunk.end;
        }

        private static readonly double kEpsilonEvent = 1e-12;

        public void Update(double playableTime, float deltaTime)
        {
            var playableTimeForEvent = playableTime + kEpsilonEvent;
            var currentChunkIndex = kErrorIndex;

            if (m_LastChunk != kErrorIndex && IsTimeInChunk(playableTime, m_LastChunk))
            {
                currentChunkIndex = m_LastChunk;
            }

            if (currentChunkIndex == kErrorIndex)
            {
                for (int i = 0; i < m_Chunks.Length; i++)
                {
                    if (IsTimeInChunk(playableTime, i))
                    {
                        currentChunkIndex = i;
                        break;
                    }
                }
            }

            if (m_LastChunk != currentChunkIndex)
            {
                if (m_LastChunk != kErrorIndex)
                {
                    var before = playableTime < m_Chunks[m_LastChunk].begin;
                    OnLeaveChunk(m_LastChunk, before);
                }
                if (currentChunkIndex != kErrorIndex)
                {
                    OnEnterChunk(currentChunkIndex);
                }
                m_LastChunk = currentChunkIndex;
                m_LastEvent = kErrorIndex;
            }

            if (currentChunkIndex != kErrorIndex)
            {
                var chunk = m_Chunks[currentChunkIndex];
                if (chunk.scrubbing && !Application.isPlaying)
                {
                    // Handle accurate timeline scrubbing in editor mode
                    float localClipTime = (float)(playableTime - chunk.begin);
                    m_Target.Simulate(localClipTime, true, true, false);
                }
                else
                {
                    // Regular runtime playback trigger logic
                    ProcessNoScrubbingEvents(chunk, m_LastPlayableTime, playableTimeForEvent);
                }
            }
            m_LastPlayableTime = playableTime;
        }

        private void ProcessNoScrubbingEvents(Chunk chunk, double oldTime, double newTime)
        {
            if (newTime < oldTime) 
            {
                m_LastEvent = kErrorIndex;
            }
            else
            {
                GetEventsIndex(chunk, oldTime, newTime, m_LastEvent, m_EventListIndexCache);
                foreach (var itEvent in m_EventListIndexCache)
                {
                    ProcessEvent(itEvent, chunk);
                }
            }
        }

        private void ProcessEvent(int eventIndex, Chunk currentChunk)
        {
            if (eventIndex == kErrorIndex) return;
            m_LastEvent = eventIndex;
            var currentEvent = currentChunk.events[eventIndex];

            if (Application.isPlaying)
            {
                if (currentEvent.isCommandPlay)
                {
                    m_Target.Play(true);
                }
                else
                {
                    // Smooth cutoff handler
                    m_Target.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private static void GetEventsIndex(Chunk chunk, double minTime, double maxTime, int lastIndex, List<int> eventListIndex)
        {
            eventListIndex.Clear();
            int startIndex = lastIndex == kErrorIndex ? 0 : lastIndex + 1;
            for (int i = startIndex; i < chunk.events.Length; ++i)
            {
                var currentEvent = chunk.events[i];
                if (currentEvent.time >= maxTime) break;
                if (minTime <= currentEvent.time) eventListIndex.Add(i);
            }
        }

        public void RestoreParticleState(bool restorePause = true, bool restoreSeedState = true)
        {
            if (m_Target == null) return;
            if (restoreSeedState)
            {
                if (!Application.isPlaying)
                {
                    // In the editor, fully stop the system to reset the seed without warnings
                    m_Target.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    m_Target.useAutoRandomSeed = m_BackupReseedOnPlay;
                    m_Target.randomSeed = m_BackupStartSeed;
                }
                else if (!m_Target.isPlaying)
                {
                    // In play mode, only restore the seed if all active particles have already expired
                    m_Target.useAutoRandomSeed = m_BackupReseedOnPlay;
                    m_Target.randomSeed = m_BackupStartSeed;
                }
            }
        }

        class BehaviourComparer : IComparer<ParticleSystemControlPlayableBehaviour>
        {
            public int Compare(ParticleSystemControlPlayableBehaviour x, ParticleSystemControlPlayableBehaviour y)
            {
                return x.clipStart.CompareTo(y.clipStart);
            }
        }

        public void Init(Playable playable, ParticleSystem ps, ParticleSystemControlTrack parentTrack)
        {
            m_Target = ps;
            m_BackupStartSeed = m_Target.randomSeed;
            m_BackupReseedOnPlay = m_Target.useAutoRandomSeed;

            int inputCount = playable.GetInputCount();
            var playableBehaviors = new List<ParticleSystemControlPlayableBehaviour>();

            for (int i = 0; i < inputCount; ++i)
            {
                var inputPlayable = playable.GetInput(i);
                if (inputPlayable.GetPlayableType() != typeof(ParticleSystemControlPlayableBehaviour)) continue;

                var inputVFXPlayable = (ScriptPlayable<ParticleSystemControlPlayableBehaviour>)inputPlayable;
                var inputBehavior = inputVFXPlayable.GetBehaviour();
                if (inputBehavior != null) playableBehaviors.Add(inputBehavior);
            }

            playableBehaviors.Sort(new BehaviourComparer());
            var chunksList = new List<Chunk>();

            foreach (var inputBehavior in playableBehaviors)
            {
                var chunk = new Chunk
                {
                    begin = inputBehavior.clipStart,
                    end = inputBehavior.clipEnd,
                    scrubbing = inputBehavior.scrubbing,
                    startSeed = inputBehavior.startSeed,
                    reinitEnter = inputBehavior.reinitEnter,
                    reinitExit = inputBehavior.reinitExit,
                    events = new Event[]
                    {
                        new Event { time = inputBehavior.clipStart, isCommandPlay = true },
                        new Event { time = inputBehavior.clipEnd, isCommandPlay = false }
                    }
                };
                chunksList.Add(chunk);
            }

            m_Chunks = chunksList.ToArray();
        }

        public void Release()
        {
            RestoreParticleState();
        }
    }

    public class ParticleSystemControlTrackMixerBehaviour : PlayableBehaviour
    {
        private ParticleSystemControlTrackController m_CacheHelper;
        private ParticleSystemControlTrack m_ParentTrack;
        private ParticleSystem m_Target;
        private bool m_ReinitBinding;
        private bool m_ReinitUnbinding;

        public void Init(ParticleSystemControlTrack parentTrack, bool reinitBinding, bool reinitUnbinding)
        {
            m_ParentTrack = parentTrack;
            m_ReinitBinding = reinitBinding;
            m_ReinitUnbinding = reinitUnbinding;
        }

        private void ApplyFrame(Playable playable, FrameData data)
        {
            if (m_Target == null) return;

            if (m_CacheHelper == null)
            {
                m_CacheHelper = new ParticleSystemControlTrackController();
                m_CacheHelper.Init(playable, m_Target, m_ParentTrack);
            }

            var globalTime = playable.GetOutput(0).GetTime();
            var deltaTime = data.deltaTime;
            m_CacheHelper.Update(globalTime, deltaTime);
        }

        private void BindPS(ParticleSystem ps)
        {
            m_Target = ps;
            if (m_Target != null && m_ReinitBinding && Application.isPlaying)
            {
                m_Target.Clear(true);
            }
        }

        private void UnbindPS()
        {
            if (m_Target != null && m_ReinitUnbinding && Application.isPlaying)
            {
                m_Target.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            m_Target = null;
        }

        public override void PrepareFrame(Playable playable, FrameData data)
        {
            var ps = data.output.GetUserData() as ParticleSystem;
            if (m_Target != ps)
            {
                UnbindPS();
                BindPS(ps);
                InvalidateHelper();
            }
            ApplyFrame(playable, data);
        }

        public override void OnBehaviourPause(Playable playable, FrameData data)
        {
            base.OnBehaviourPause(playable, data);
            ApplyFrame(playable, data);
        }

        private void InvalidateHelper()
        {
            if (m_CacheHelper != null)
            {
                m_CacheHelper.Release();
                m_CacheHelper = null;
            }
        }

        public override void OnPlayableCreate(Playable playable)
        {
            InvalidateHelper();
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            InvalidateHelper();
            UnbindPS();
        }
    }
}