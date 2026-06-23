using System;

namespace Nyvorn.Source.World.Generation
{
    public readonly struct TissueCellState
    {
        public const float PresenceThreshold = 0.001f;

        public TissueCellState(
            float presence,
            float vitality,
            float corruption,
            float memoryDensity,
            float flow)
        {
            Presence = Clamp01(presence);
            Vitality = Clamp01(vitality);
            Corruption = Clamp01(corruption);
            MemoryDensity = Clamp01(memoryDensity);
            Flow = Clamp01(flow);
        }

        public static TissueCellState Neutral { get; } = new(0f, 0f, 0f, 0f, 0f);

        public float Presence { get; }
        public float Vitality { get; }
        public float Corruption { get; }
        public float MemoryDensity { get; }
        public float Flow { get; }
        public bool HasBiologicalPresence => Presence > PresenceThreshold;
        public bool IsNeutral => Presence <= PresenceThreshold &&
                                 Vitality <= PresenceThreshold &&
                                 Corruption <= PresenceThreshold &&
                                 MemoryDensity <= PresenceThreshold &&
                                 Flow <= PresenceThreshold;

        public static TissueCellState FromLegacyPresence(bool hasTissue)
        {
            return hasTissue
                ? new TissueCellState(1f, 1f, 0f, 0f, 0f)
                : Neutral;
        }

        public TissueCellState With(
            float? presence = null,
            float? vitality = null,
            float? corruption = null,
            float? memoryDensity = null,
            float? flow = null)
        {
            return new TissueCellState(
                presence ?? Presence,
                vitality ?? Vitality,
                corruption ?? Corruption,
                memoryDensity ?? MemoryDensity,
                flow ?? Flow);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;

            return Math.Clamp(value, 0f, 1f);
        }
    }
}
