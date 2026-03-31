using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenProgressReporter
    {
        private readonly object sync = new();
        private readonly Dictionary<string, PhaseProgress> phases = new(StringComparer.Ordinal);

        public WorldGenProgressReporter(IReadOnlyList<WorldGenPhaseDefinition> phaseDefinitions)
        {
            if (phaseDefinitions == null)
                throw new ArgumentNullException(nameof(phaseDefinitions));

            for (int i = 0; i < phaseDefinitions.Count; i++)
            {
                WorldGenPhaseDefinition definition = phaseDefinitions[i];
                phases[definition.Name] = new PhaseProgress(definition.Label);
            }
        }

        public void Begin(string phaseName, string message = null)
        {
            lock (sync)
            {
                if (!phases.TryGetValue(phaseName, out PhaseProgress phase))
                    return;

                phase.Progress = 0f;
                phase.Message = string.IsNullOrWhiteSpace(message) ? phase.Label : message;
            }
        }

        public void Report(string phaseName, float progress, string message = null)
        {
            lock (sync)
            {
                if (!phases.TryGetValue(phaseName, out PhaseProgress phase))
                    return;

                phase.Progress = Math.Clamp(progress, 0f, 1f);
                if (!string.IsNullOrWhiteSpace(message))
                    phase.Message = message;
            }
        }

        public void Complete(string phaseName, string message = null)
        {
            lock (sync)
            {
                if (!phases.TryGetValue(phaseName, out PhaseProgress phase))
                    return;

                phase.Progress = 1f;
                if (!string.IsNullOrWhiteSpace(message))
                    phase.Message = message;
            }
        }

        public float GetPhaseProgress(string phaseName)
        {
            lock (sync)
            {
                return phases.TryGetValue(phaseName, out PhaseProgress phase) ? phase.Progress : 0f;
            }
        }

        public string GetPhaseMessage(string phaseName, string fallbackLabel)
        {
            lock (sync)
            {
                if (!phases.TryGetValue(phaseName, out PhaseProgress phase))
                    return fallbackLabel;

                return string.IsNullOrWhiteSpace(phase.Message) ? fallbackLabel : phase.Message;
            }
        }

        private sealed class PhaseProgress
        {
            public PhaseProgress(string label)
            {
                Label = label;
                Message = label;
            }

            public string Label { get; }
            public float Progress { get; set; }
            public string Message { get; set; }
        }
    }
}
