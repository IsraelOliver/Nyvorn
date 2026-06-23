using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.World.Tissue
{
    /// <summary>
    /// Centraliza os parâmetros ajustáveis da teia cósmica de Tissue.
    /// Alterar estes valores muda toda nova geração determinística da rede.
    /// </summary>
    public static class TissueConfig
    {
        public static class Generation
        {
            public const int SampleCellSize = 128;
            public const float SurfaceDepthStart = 0.22f;
            public const float PointChanceSurface = 0.035f;
            public const float PointChanceDepth = 0.82f;
            public const float DensityCurvePower = 2.2f;
            public const float MinPointDistanceSurface = 150f;
            public const float MinPointDistanceDepth = 64f;
            public const float MaxPointChance = 0.98f;
            public const int PointSpatialCellSize = 64;

            public const int NestWidthDivisor = 12000;
            public const int NestCountMin = 2;
            public const int NestCountMax = 5;
            public const float NestDepthMin = 0.72f;
            public const float NestDepthMax = 0.95f;
            public const int NestRadiusMin = 280;
            public const int NestRadiusMax = 560;
            public const float NestMultiplierMin = 1.45f;
            public const float NestMultiplierMax = 2f;
            public const float NestInfluenceMax = 2.5f;
            public const float NestDistanceReduction = 0.28f;
        }

        public static class Network
        {
            public const float ConnectionDistanceMin = 48f;
            public const float ConnectionDistanceMax = 280f;
            public const float RepairConnectionDistanceMax = 448f;
            public const float MinAngularSeparationDegrees = 32f;
            public static readonly float MinAngularSeparation = MathF.PI * (MinAngularSeparationDegrees / 180f);
            public const int GraphSpatialCellSize = 280;
            public const int RenderSpatialChunkSize = 512;
            public const int MaximumNodeDegree = 8;
            public const float ConnectivityTarget = 0.99f;

            public const float MiddleLayerStart = 0.48f;
            public const float DeepLayerStart = 0.76f;
            public const int UpperConnectionsMin = 1;
            public const int UpperConnectionsMax = 2;
            public const int MiddleConnectionsMin = 2;
            public const int MiddleConnectionsMax = 4;
            public const int DeepConnectionsMin = 3;
            public const int DeepConnectionsMax = 6;
            public const float NestConnectionBoostThreshold = 0.35f;
            public const int NestConnectionBonus = 2;
        }

        public static class Nodes
        {
            public const int PrimaryDegreeThreshold = 4;
            public const float StrengthDepthWeight = 0.55f;
            public const float StrengthDegreeWeight = 0.30f;
            public const float StrengthNestWeight = 0.15f;
            public const float StrengthMin = 0.15f;
            public const float StrengthMax = 1f;
        }

        public static class Branches
        {
            public const float StrengthDepthWeight = 0.62f;
            public const float StrengthDegreeWeight = 0.24f;
            public const float StrengthNestWeight = 0.07f;
            public const float StrengthMin = 0.2f;
            public const float StrengthMax = 1f;
            public const float ThicknessMin = 0.65f;
            public const float ThicknessMax = 2.1f;
            public const float PrimaryStrengthThreshold = 0.72f;
        }

        public static class Paths
        {
            public const int NoiseSeedOffset = 7001;
            public const float NoiseScale = 0.008f;
            public const float WanderRetention = 0.86f;
            public const float WanderStrength = 0.82f;
            public const float StepSize = 3.4f;
            public const float StepNoiseStrength = 0.75f;
            public const float StepSizeMin = 2.2f;
            public const float StepSizeMax = 4.4f;
            public const float TargetSnapDistance = 3f;
            public const float StepsPerPixel = 0.5f;
            public const int MinimumSteps = 12;
        }

        public static class MicroFilaments
        {
            public const float GenerationChance = 0.46f;
            public const int MultipleFilamentDegree = 6;
            public const int FilamentCount = 1;
            public const int DenseFilamentCount = 2;
            public const int LengthMin = 18;
            public const int LengthMax = 70;
            public const float Thickness = 0.45f;
            public const float IntensityBase = 0.28f;
            public const float IntensityDepthWeight = 0.22f;
        }

        public static class Rasterization
        {
            public const float BranchVitality = 0.72f;
            public const float BranchFlow = 0.78f;
            public const float NodeVitality = 0.92f;
            public const float NodeFlow = 0.88f;
        }

        public static class EnvironmentSensor
        {
            public const int SampleDiameterTiles = 25;
            public const float SampleInterval = 0.25f;
            public const float NearestNodeDistance = 300f;
        }

        public static class Resonance
        {
            public const float NodeSearchDistance = 300f;
            public const float DefaultMaxPropagationDistance = 720f;
            public const float MinimumPropagationDistance = 320f;
            public const float ViewportDistanceScale = 1.25f;
            public const float PulseSpeed = 320f;
            public const float TrailLength = 100f;
            public const float DistanceFalloffPower = 1.4f;
            public const float NodePulseDuration = 0.58f;
            public const float CoverageAtFullStrength = 0.18f;
            public const float MinimumCoverageInfluence = 0.30f;
            public const float MinimumResponseStrength = 0.008f;
            public const float VisualGain = 2.8f;
        }

        public static class WorldVisual
        {
            public static readonly Color HaloViolet = new(100, 28, 190);
            public static readonly Color SheathMagenta = new(218, 58, 205);
            public static readonly Color CorePink = new(255, 132, 158);
            public static readonly Color CoreGold = new(255, 202, 92);

            public const float BranchDepthScaleMin = 0.72f;
            public const float BranchDepthScaleMax = 2.65f;
            public const float BranchDepthCurvePower = 1.35f;
            public const float NodeDepthScaleMin = 0.82f;
            public const float NodeDepthScaleMax = 1.65f;
            public const int MaximumPixelRadius = 6;

            public const int HaloCullingPadding = 48;
            public const int CoreCullingPadding = 24;

            public const float MicroHaloScale = 0.55f;
            public const float HaloThickness = 8f;
            public const float SheathHaloThickness = 4f;
            public const float HaloAlphaBase = 0.055f;
            public const float HaloAlphaIntensity = 0.055f;
            public const float SheathHaloAlphaBase = 0.07f;
            public const float SheathHaloAlphaIntensity = 0.07f;
            public const float CoreSheathThickness = 1.9f;
            public const float CoreThickness = 0.62f;
            public const float CoreSheathAlphaBase = 0.30f;
            public const float CoreSheathAlphaIntensity = 0.22f;
            public const float CoreAlphaBase = 0.58f;
            public const float CoreAlphaIntensity = 0.32f;
            public const float CoreGoldBlendStart = 0.68f;
            public const float CoreGoldBlendScale = 2.2f;
            public const float MinimumCoreThickness = 0.65f;
            public const float MicroCoreAlpha = 0.22f;
            public const float MinimumMicroThickness = 0.45f;

            public const float HaloNodeRadiusBase = 4f;
            public const float HaloNodeDegreeRadius = 1.15f;
            public const float HaloNodeNestRadius = 1.8f;
            public const float HaloNodeOuterScale = 3.1f;
            public const float HaloNodeInnerScale = 1.8f;
            public const float HaloNodeOuterAlphaBase = 0.08f;
            public const float HaloNodeOuterAlphaStrength = 0.08f;
            public const float HaloNodeInnerAlphaBase = 0.12f;
            public const float HaloNodeInnerAlphaStrength = 0.10f;

            public const float CoreNodeRadiusBase = 3f;
            public const float CoreNodeDegreeRadius = 0.72f;
            public const float CoreNodeNestRadius = 1f;
            public const float CoreNodeSheathScale = 1.35f;
            public const float CoreNodePinkScale = 0.72f;
            public const float CoreNodeGoldScale = 0.28f;
            public const float CoreNodeSheathAlpha = 0.48f;
            public const float CoreNodePinkAlpha = 0.86f;
            public const float CoreNodeGoldRadiusMin = 1.2f;

            public const float OrganicVariationBase = 0.88f;
            public const float OrganicVariationAmplitude = 0.12f;
            public const float OrganicVariationWaves = 4f;
            public const float OrganicBranchPhase = 0.37f;
            public const float PrimaryEndpointBulge = 0.24f;
            public const float SecondaryEndpointBulge = 0.08f;
        }

        public static class MinimapVisual
        {
            public static readonly Color MicroColor = new(160, 52, 205, 115);
            public static readonly Color BranchColorLow = new(202, 58, 215, 210);
            public static readonly Color BranchColorHigh = new(255, 154, 130, 245);
            public static readonly Color NodeColor = new(255, 205, 82, 255);
            public const float MicroDetailZoom = 3f;
            public const int MicroThickness = 1;
            public const int BranchThickness = 1;
            public const int PrimaryBranchThickness = 2;
            public const int NodeRadiusMin = 2;
            public const int NodeRadiusMax = 4;
            public const int NodeDegreeDivisor = 3;
        }

        public static class FieldDebugVisual
        {
            public static readonly Color LowPresenceColor = new(0, 255, 80);
            public static readonly Color HighPresenceColor = new(255, 235, 20);
            public const float Alpha = 0.68f;
            public const int TileInset = 1;
        }

        public static class RevealVisual
        {
            public static readonly Color LowPresenceColor = new(176, 52, 224);
            public static readonly Color HighVitalityColor = new(255, 174, 92);
            public static readonly Color CorruptionColor = new(102, 255, 72);
            public static readonly Color MemoryColor = new(255, 226, 132);
            public const float MinimumAlpha = 0.22f;
            public const float MaximumAlpha = 0.88f;
            public const float MemoryColorWeight = 0.42f;
            public const int TileInset = 1;
        }

        public static class ResonanceVisual
        {
            public static readonly Color HaloColor = new(178, 58, 255);
            public static readonly Color PulseColor = new(255, 118, 210);
            public static readonly Color CoreColor = new(255, 214, 100);
            public static readonly Color AfterglowColor = new(255, 142, 58);
            public const int MinimumCullingPadding = 72;
            public const float CullingMarginRatio = 1f / 3f;
            public const float MicroThicknessScale = 0.62f;
            public const float HaloThicknessScale = 7.5f;
            public const float CoreThicknessScale = 1.7f;
            public const float HaloAlpha = 0.42f;
            public const float CoreAlpha = 0.92f;
            public const float AfterglowHaloAlpha = 0.075f;
            public const float AfterglowCoreAlpha = 0.22f;
            public const float NodeRadiusBase = 5f;
            public const float NodeDegreeRadius = 0.8f;
            public const float NodeOuterScale = 3.2f;
            public const float NodeInnerScale = 1.35f;
            public const float NodePulseScale = 0.65f;
            public const float NodeHaloAlpha = 0.34f;
            public const float NodeCoreAlpha = 0.96f;
            public const float AfterglowNodeRadiusScale = 0.72f;
            public const float AfterglowNodeHaloAlpha = 0.095f;
            public const float AfterglowNodeCoreAlpha = 0.28f;
        }
    }
}
