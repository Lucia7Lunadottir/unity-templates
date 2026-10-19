using System;
using UnityEngine.Playables;

namespace PG
{
    [Serializable]
    public struct ParticleSystemPlayableEvent
    {
        public double time;
        public bool isCommandPlay; 
    }

    public class ParticleSystemControlPlayableBehaviour : PlayableBehaviour
    {
        public double clipStart { get; set; }
        public double clipEnd { get; set; }
        public bool scrubbing { get; set; }
        public bool reinitEnter { get; set; }
        public bool reinitExit { get; set; }
        public uint startSeed { get; set; }
    }
}