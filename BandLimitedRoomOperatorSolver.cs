using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAcousticsSim
{
    [DisallowMultipleComponent]
    public sealed class BandLimitedRoomOperatorSolver : MonoBehaviour
    {
        public enum SolverQuality
        {
            Fast,
            Balanced,
            Detailed
        }

        [Header("Operator Solver")]
        [SerializeField] private SolverQuality quality = SolverQuality.Balanced;
        [SerializeField] private float cellSizeMeters = 0.75f;
        [SerializeField] [Min(1)] private int iterationCount = 48;
        [SerializeField] [Range(0.01f, 0.95f)] private float transport = 0.22f;
        [SerializeField] [Range(0.001f, 1f)] private float sourceCoupling = 0.045f;
        [SerializeField] [Range(0.01f, 4f)] private float lateFieldScale = 0.35f;
        [SerializeField] [Range(0f, 0.25f)] private float globalDamping = 0.02f;
        [SerializeField] [Range(0f, 1f)] private float minDiffuseAbsorption = 0.05f;
        [SerializeField] private float maxSourceDistanceMeters = 60f;
        [SerializeField] private float wallInfluenceMeters = 1.25f;
        [SerializeField] private bool useSimulatorProbeHeight = true;
        [SerializeField] private float overrideProbeHeight = 1.45f;

        [Header("Debug")]
        [SerializeField] private Bounds lastBounds;
        [SerializeField] private Vector2Int gridSize;
        [SerializeField] private int effectiveBandCount = AcousticBands.Count;
        [SerializeField] private float effectiveCellSizeMeters = 0.75f;
        [SerializeField] private int effectiveIterationCount = 48;
        [SerializeField] private float effectiveTransport = 0.16f;
        [SerializeField] private float effectiveSourceCoupling = 0.025f;
        [SerializeField] private float effectiveLateFieldScale = 0.20f;
        [SerializeField] private float effectiveGlobalDamping = 0.05f;
        [SerializeField] [Min(1)] private int lateFieldHeightLayers = 3;
        [SerializeField] private float[] averageAbsorption = new float[AcousticBands.Count];
        [SerializeField] private float[] averageReflection = new float[AcousticBands.Count];

        [NonSerialized] private float[][][] layeredBandFields;
        [NonSerialized] private bool[][] blockedPerLayer;
        [NonSerialized] private float[] layerHeights;
        [NonSerialized] private int width;
        [NonSerialized] private int depth;
        [NonSerialized] private Bounds bounds;
        [NonSerialized] private float yPlane;
        [NonSerialized] private bool hasField;

        public bool HasField => hasField;

        public SolverQuality Quality => quality;
        public int EffectiveBandCount => effectiveBandCount;
        public float EffectiveCellSizeMeters => effectiveCellSizeMeters;
        public int EffectiveIterationCount => effectiveIterationCount;
        public float EffectiveTransport => effectiveTransport;
        public float EffectiveSourceCoupling => effectiveSourceCoupling;
        public float EffectiveLateFieldScale => effectiveLateFieldScale;
        public float EffectiveGlobalDamping => effectiveGlobalDamping;
        public int EffectiveLateFieldHeightLayers => lateFieldHeightLayers;

        private void OnValidate()
        {
            cellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
            iterationCount = Mathf.Max(1, iterationCount);
            maxSourceDistanceMeters = Mathf.Max(1f, maxSourceDistanceMeters);
            wallInfluenceMeters = Mathf.Max(0.05f, wallInfluenceMeters);
            overrideProbeHeight = Mathf.Max(-100f, overrideProbeHeight);
            lateFieldHeightLayers = Mathf.Max(1, lateFieldHeightLayers);
            RebuildEffectiveSettings();
            AcousticBands.EnsureArray(ref averageAbsorption, 0.15f);
            AcousticBands.EnsureArray(ref averageReflection, 0.80f);
        }

        public void ClearField()
        {
            hasField = false;
            layeredBandFields = null;
            blockedPerLayer = null;
            layerHeights = null;
            width = 0;
            depth = 0;
            gridSize = Vector2Int.zero;
        }

        public void BuildField(Bounds roomBounds, List<AcousticSpeaker> speakers, List<AcousticMaterial> materials, Onyx24Mixer mixer, LayerMask acousticLayerMask, float airTemperatureC, float humidityPercent, float simulatorProbeHeight, Func<Vector3, bool> isBlockedPoint)
        {
            ClearField();
            if (speakers == null || speakers.Count == 0) return;

            bounds = roomBounds;
            lastBounds = roomBounds;
            yPlane = useSimulatorProbeHeight ? simulatorProbeHeight : overrideProbeHeight;
            RebuildEffectiveSettings();

            width = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.x / effectiveCellSizeMeters));
            depth = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.z / effectiveCellSizeMeters));
            gridSize = new Vector2Int(width, depth);
            int cellCount = width * depth;

            int layerCount = Mathf.Max(1, lateFieldHeightLayers);
            layerHeights = new float[layerCount];
            blockedPerLayer = new bool[layerCount][];
            layeredBandFields = new float[layerCount][][];
            for (int ly = 0; ly < layerCount; ly++)
            {
                blockedPerLayer[ly] = new bool[cellCount];
                layeredBandFields[ly] = new float[AcousticBands.Count][];
                for (int b = 0; b < AcousticBands.Count; b++) layeredBandFields[ly][b] = new float[cellCount];
                float t = layerCount <= 1 ? 0.5f : ly / (float)(layerCount - 1);
                layerHeights[ly] = Mathf.Lerp(bounds.min.y + 0.1f, bounds.max.y - 0.1f, t);
            }

            List<Bounds> materialBounds = new List<Bounds>();
            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    if (materials[i] != null) materialBounds.Add(materials[i].GetBounds());
                }
            }

            ComputeAverageMaterialCoefficients(materials);

            int bandCount = Mathf.Clamp(effectiveBandCount, 1, AcousticBands.Count);

            float[] transmissionScratch = new float[AcousticBands.Count];
            for (int ly = 0; ly < layerCount; ly++)
            {
                float[][] source = new float[AcousticBands.Count][];
                for (int b = 0; b < AcousticBands.Count; b++) source[b] = new float[cellCount];
                float sampleY = layerHeights[ly];

                for (int z = 0; z < depth; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = IndexOf(x, z);
                        Vector3 point = CellCenter(x, z, sampleY);
                        bool pointBlocked = isBlockedPoint != null && isBlockedPoint(point);
                        blockedPerLayer[ly][idx] = pointBlocked;
                        if (pointBlocked) continue;

                        float enclosure = EvaluateEnclosureFactor(point, materialBounds);

                        for (int s = 0; s < speakers.Count; s++)
                        {
                            AcousticSpeaker speaker = speakers[s];
                            if (speaker == null || !speaker.EmitToRoom) continue;

                            Vector3 speakerPos = speaker.transform.position;
                            Vector3 delta = point - speakerPos;
                            float distance = delta.magnitude;
                            if (distance > maxSourceDistanceMeters || distance <= 0.05f) continue;

                            Vector3 direction = delta / distance;
                            float directivity = speaker.GetDirectivityAmplitude(direction);
                            if (directivity <= 0.0001f) continue;

                            FillTransmissionAmplitudeAlongSegment(speakerPos, point, acousticLayerMask, GetPrimaryCollider(speaker), null, transmissionScratch);

                            for (int b = 0; b < bandCount; b++)
                            {
                                float sourcePhase = 0f;
                                float driveDb = mixer != null ? mixer.GetSpeakerBandEmissionOffsetDb(speaker, b, out sourcePhase) : -speaker.NominalInputLevelDbuForMaxSpl;

                                float sourcePressurePa = speaker.GetNominalPressureAt1m(b, driveDb);
                                float airDb = AcousticBands.AirAbsorptionDbPerMeter(AcousticBands.CenterFrequenciesHz[b], airTemperatureC, humidityPercent) * distance;
                                float airAmp = AcousticBands.DbToAmplitude(-airDb);
                                float bandCoupling = Mathf.Lerp(1.18f, 0.42f, b / (float)(AcousticBands.Count - 1));

                                float injected = sourcePressurePa * (directivity / Mathf.Max(1f, distance)) * transmissionScratch[b] * airAmp * effectiveSourceCoupling * enclosure * bandCoupling;
                                source[b][idx] += injected;
                            }
                        }
                    }
                }

                for (int b = 0; b < bandCount; b++)
                {
                    float[] field = new float[cellCount];
                    float[] next = new float[cellCount];
                    Array.Copy(source[b], field, cellCount);

                    float bandAbsorption = Mathf.Clamp01(Mathf.Max(minDiffuseAbsorption, averageAbsorption[b]));
                    float bandReflection = Mathf.Clamp01(averageReflection[b]);
                    float freqNorm = b / (float)(AcousticBands.Count - 1);
                    float bandTransport = effectiveTransport * Mathf.Lerp(1.05f, 0.58f, freqNorm);
                    float retain = Mathf.Clamp01(0.975f - effectiveGlobalDamping - (bandAbsorption * 0.14f));
                    float stepAirDb = AcousticBands.AirAbsorptionDbPerMeter(AcousticBands.CenterFrequenciesHz[b], airTemperatureC, humidityPercent) * effectiveCellSizeMeters;
                    float stepAirAmp = AcousticBands.DbToAmplitude(-stepAirDb);

                    for (int it = 0; it < effectiveIterationCount; it++)
                    {
                        for (int z = 0; z < depth; z++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int idx = IndexOf(x, z);
                                if (blockedPerLayer[ly][idx])
                                {
                                    next[idx] = 0f;
                                    continue;
                                }

                                float current = field[idx];
                                float left = SampleNeighbor(field, blockedPerLayer[ly], x - 1, z, current * bandReflection);
                                float right = SampleNeighbor(field, blockedPerLayer[ly], x + 1, z, current * bandReflection);
                                float down = SampleNeighbor(field, blockedPerLayer[ly], x, z - 1, current * bandReflection);
                                float up = SampleNeighbor(field, blockedPerLayer[ly], x, z + 1, current * bandReflection);

                                float neighborMean = 0.25f * (left + right + down + up);
                                float diffused = current + ((neighborMean - current) * bandTransport);
                                float damped = diffused * retain * stepAirAmp;
                                float injected = source[b][idx];

                                next[idx] = Mathf.Max(0f, damped + injected);
                            }
                        }
                        float[] swap = field;
                        field = next;
                        next = swap;
                    }

                    for (int i = 0; i < cellCount; i++)
                    {
                        layeredBandFields[ly][b][i] = field[i] * effectiveLateFieldScale;
                    }
                }
            }

            hasField = true;
        }

        public void SampleBandPressures(Vector3 point, float[] destinationPa)
        {
            if (destinationPa == null) return;
            for (int b = 0; b < destinationPa.Length; b++) destinationPa[b] = 0f;

            if (!hasField || layeredBandFields == null || width <= 0 || depth <= 0) return;

            float gx = ((point.x - bounds.min.x) / cellSizeMeters) - 0.5f;
            float gz = ((point.z - bounds.min.z) / cellSizeMeters) - 0.5f;

            int x0 = Mathf.FloorToInt(gx);
            int z0 = Mathf.FloorToInt(gz);
            float tx = gx - x0;
            float tz = gz - z0;

            x0 = Mathf.Clamp(x0, 0, width - 1);
            z0 = Mathf.Clamp(z0, 0, depth - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
            int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);

            int bandCount = Mathf.Clamp(effectiveBandCount, 1, AcousticBands.Count);
            int layerCount = layerHeights != null ? layerHeights.Length : 0;
            if (layerCount <= 0) return;

            int lowerLayer = 0;
            int upperLayer = layerCount - 1;
            float layerT = 0f;
            if (layerCount > 1)
            {
                lowerLayer = 0;
                while (lowerLayer + 1 < layerCount && layerHeights[lowerLayer + 1] <= point.y)
                {
                    lowerLayer++;
                }
                upperLayer = Mathf.Min(layerCount - 1, lowerLayer + 1);
                float h0 = layerHeights[lowerLayer];
                float h1 = layerHeights[upperLayer];
                layerT = upperLayer == lowerLayer ? 0f : Mathf.InverseLerp(h0, h1, point.y);
            }

            for (int b = 0; b < bandCount; b++)
            {
                float low00 = layeredBandFields[lowerLayer][b][IndexOf(x0, z0)];
                float low10 = layeredBandFields[lowerLayer][b][IndexOf(x1, z0)];
                float low01 = layeredBandFields[lowerLayer][b][IndexOf(x0, z1)];
                float low11 = layeredBandFields[lowerLayer][b][IndexOf(x1, z1)];

                float high00 = layeredBandFields[upperLayer][b][IndexOf(x0, z0)];
                float high10 = layeredBandFields[upperLayer][b][IndexOf(x1, z0)];
                float high01 = layeredBandFields[upperLayer][b][IndexOf(x0, z1)];
                float high11 = layeredBandFields[upperLayer][b][IndexOf(x1, z1)];

                float lowA = Mathf.Lerp(low00, low10, tx);
                float lowC = Mathf.Lerp(low01, low11, tx);
                float lowLayerValue = Mathf.Lerp(lowA, lowC, tz);

                float highA = Mathf.Lerp(high00, high10, tx);
                float highC = Mathf.Lerp(high01, high11, tx);
                float highLayerValue = Mathf.Lerp(highA, highC, tz);

                destinationPa[b] = Mathf.Lerp(lowLayerValue, highLayerValue, layerT);
            }
        }

        private void ComputeAverageMaterialCoefficients(List<AcousticMaterial> materials)
        {
            AcousticBands.EnsureArray(ref averageAbsorption, 0.15f);
            AcousticBands.EnsureArray(ref averageReflection, 0.80f);

            if (materials == null || materials.Count == 0)
            {
                for (int b = 0; b < AcousticBands.Count; b++)
                {
                    averageAbsorption[b] = 0.15f;
                    averageReflection[b] = 0.80f;
                }
                return;
            }

            float validArea = 0f;
            for (int b = 0; b < AcousticBands.Count; b++)
            {
                averageAbsorption[b] = 0f;
                averageReflection[b] = 0f;
            }

            for (int i = 0; i < materials.Count; i++)
            {
                AcousticMaterial material = materials[i];
                if (material == null) continue;
                Bounds bounds = material.GetBounds();
                float areaWeight = Mathf.Max(0.01f, 2f * ((bounds.size.x * bounds.size.y) + (bounds.size.y * bounds.size.z) + (bounds.size.x * bounds.size.z)));
                validArea += areaWeight;
                for (int b = 0; b < AcousticBands.Count; b++)
                {
                    averageAbsorption[b] += material.GetAbsorption(b) * areaWeight;
                    averageReflection[b] += material.GetReflectionAmplitude(b) * areaWeight;
                }
            }

            if (validArea <= 0f)
            {
                for (int b = 0; b < AcousticBands.Count; b++)
                {
                    averageAbsorption[b] = 0.15f;
                    averageReflection[b] = 0.80f;
                }
                return;
            }

            for (int b = 0; b < AcousticBands.Count; b++)
            {
                averageAbsorption[b] = Mathf.Clamp01(averageAbsorption[b] / validArea);
                averageReflection[b] = Mathf.Clamp01(averageReflection[b] / validArea);
            }
        }

        private float EvaluateEnclosureFactor(Vector3 point, List<Bounds> materialBounds)
        {
            if (materialBounds == null || materialBounds.Count == 0) return 1f;

            float minDistance = float.PositiveInfinity;
            for (int i = 0; i < materialBounds.Count; i++)
            {
                float d = AcousticsMath.DistanceToBounds(materialBounds[i], point);
                if (d < minDistance) minDistance = d;
            }
            if (float.IsPositiveInfinity(minDistance)) return 1f;

            float effectiveWallInfluence = Mathf.Min(wallInfluenceMeters, 0.9f);
            float wallProximity = 1f - Mathf.Clamp01(minDistance / Mathf.Max(0.05f, effectiveWallInfluence));
            return 1f + (wallProximity * 0.18f);
        }

        private float SampleNeighbor(float[] field, bool[] blockedLayer, int x, int z, float fallback)
        {
            if (x < 0 || x >= width || z < 0 || z >= depth) return fallback;
            int idx = IndexOf(x, z);
            if (blockedLayer != null && blockedLayer[idx]) return fallback;
            return field[idx];
        }

        private int IndexOf(int x, int z) => x + (z * width);

        private Vector3 CellCenter(int x, int z, float layerY)
        {
            return new Vector3(
                bounds.min.x + ((x + 0.5f) * effectiveCellSizeMeters),
                layerY,
                bounds.min.z + ((z + 0.5f) * effectiveCellSizeMeters)
            );
        }

        private void RebuildEffectiveSettings()
        {
            switch (quality)
            {
                case SolverQuality.Fast:
                    effectiveBandCount = 4;
                    effectiveCellSizeMeters = Mathf.Max(0.1f, cellSizeMeters * 1.4f);
                    effectiveIterationCount = Mathf.Max(1, Mathf.RoundToInt(iterationCount * 0.55f));
                    lateFieldHeightLayers = Mathf.Max(1, Mathf.Min(lateFieldHeightLayers, 2));
                    break;

                case SolverQuality.Detailed:
                    effectiveBandCount = AcousticBands.Count;
                    effectiveCellSizeMeters = Mathf.Max(0.1f, cellSizeMeters * 0.75f);
                    effectiveIterationCount = Mathf.Max(1, Mathf.RoundToInt(iterationCount * 1.45f));
                    lateFieldHeightLayers = Mathf.Max(3, lateFieldHeightLayers);
                    break;

                default:
                    effectiveBandCount = 6;
                    effectiveCellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
                    effectiveIterationCount = Mathf.Max(1, iterationCount);
                    lateFieldHeightLayers = Mathf.Max(2, lateFieldHeightLayers);
                    break;
            }

            effectiveTransport = Mathf.Min(transport, 0.16f);
            effectiveSourceCoupling = Mathf.Min(sourceCoupling, 0.025f);
            effectiveLateFieldScale = Mathf.Min(lateFieldScale, 0.20f);
            effectiveGlobalDamping = Mathf.Max(globalDamping, 0.05f);
        }

        private void FillTransmissionAmplitudeAlongSegment(Vector3 a, Vector3 b, LayerMask acousticLayerMask, Collider ignoreA, Collider ignoreB, float[] transmissionOut)
        {
            if (transmissionOut == null) return;
            for (int i = 0; i < transmissionOut.Length; i++) transmissionOut[i] = 1f;

            Vector3 delta = b - a;
            float distance = delta.magnitude;
            if (distance <= 0.01f) return;

            RaycastHit[] hits = Physics.RaycastAll(a, delta / distance, distance, acousticLayerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c == ignoreA || c == ignoreB) continue;

                AcousticMaterial material = GetMaterial(c);
                if (material != null)
                {
                    for (int bnd = 0; bnd < AcousticBands.Count; bnd++)
                        transmissionOut[bnd] *= material.GetTransmissionAmplitude(bnd);
                }
                else
                {
                    for (int bnd = 0; bnd < AcousticBands.Count; bnd++)
                        transmissionOut[bnd] *= 0.10f;
                }
            }
        }

        private static AcousticMaterial GetMaterial(Collider collider)
        {
            if (collider == null) return null;
            AcousticMaterial material = collider.GetComponent<AcousticMaterial>();
            if (material != null) return material;
            return collider.GetComponentInParent<AcousticMaterial>();
        }

        private static Collider GetPrimaryCollider(Component component)
        {
            return component != null ? component.GetComponent<Collider>() : null;
        }
    }
}
