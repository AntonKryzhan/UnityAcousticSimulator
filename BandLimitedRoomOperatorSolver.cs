using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAcousticsSim
{
    [DisallowMultipleComponent]
    public sealed class BandLimitedRoomOperatorSolver : MonoBehaviour
    {
        [Header("Operator Solver")]
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
        [SerializeField] private float[] averageAbsorption = new float[AcousticBands.Count];
        [SerializeField] private float[] averageReflection = new float[AcousticBands.Count];

        [NonSerialized] private float[][] bandFields;
        [NonSerialized] private bool[] blocked;
        [NonSerialized] private int width;
        [NonSerialized] private int depth;
        [NonSerialized] private Bounds bounds;
        [NonSerialized] private float yPlane;
        [NonSerialized] private bool hasField;

        public bool HasField => hasField;

        private void OnValidate()
        {
            cellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
            iterationCount = Mathf.Max(1, iterationCount);
            maxSourceDistanceMeters = Mathf.Max(1f, maxSourceDistanceMeters);
            wallInfluenceMeters = Mathf.Max(0.05f, wallInfluenceMeters);
            overrideProbeHeight = Mathf.Max(-100f, overrideProbeHeight);
            AcousticBands.EnsureArray(ref averageAbsorption, 0.15f);
            AcousticBands.EnsureArray(ref averageReflection, 0.80f);
        }

        public void ClearField()
        {
            hasField = false;
            bandFields = null;
            blocked = null;
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

            width = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.x / cellSizeMeters));
            depth = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.z / cellSizeMeters));
            gridSize = new Vector2Int(width, depth);
            int cellCount = width * depth;

            blocked = new bool[cellCount];
            bandFields = new float[AcousticBands.Count][];
            for (int b = 0; b < AcousticBands.Count; b++) bandFields[b] = new float[cellCount];

            List<Bounds> materialBounds = new List<Bounds>();
            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    if (materials[i] != null) materialBounds.Add(materials[i].GetBounds());
                }
            }

            ComputeAverageMaterialCoefficients(materials);

            float effectiveTransport = Mathf.Min(transport, 0.16f);
            float effectiveSourceCoupling = Mathf.Min(sourceCoupling, 0.025f);
            float effectiveLateFieldScale = Mathf.Min(lateFieldScale, 0.20f);
            float effectiveGlobalDamping = Mathf.Max(globalDamping, 0.05f);

            float[][] source = new float[AcousticBands.Count][];
            for (int b = 0; b < AcousticBands.Count; b++) source[b] = new float[cellCount];

            float[] transmissionScratch = new float[AcousticBands.Count];

            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = IndexOf(x, z);
                    Vector3 point = CellCenter(x, z);
                    bool pointBlocked = isBlockedPoint != null && isBlockedPoint(point);
                    blocked[idx] = pointBlocked;
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

                        for (int b = 0; b < AcousticBands.Count; b++)
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

            for (int b = 0; b < AcousticBands.Count; b++)
            {
                float[] field = new float[cellCount];
                float[] next = new float[cellCount];
                Array.Copy(source[b], field, cellCount);

                float bandAbsorption = Mathf.Clamp01(Mathf.Max(minDiffuseAbsorption, averageAbsorption[b]));
                float bandReflection = Mathf.Clamp01(averageReflection[b]);
                float freqNorm = b / (float)(AcousticBands.Count - 1);
                float bandTransport = effectiveTransport * Mathf.Lerp(1.05f, 0.58f, freqNorm);
                float retain = Mathf.Clamp01(0.975f - effectiveGlobalDamping - (bandAbsorption * 0.14f));
                float stepAirDb = AcousticBands.AirAbsorptionDbPerMeter(AcousticBands.CenterFrequenciesHz[b], airTemperatureC, humidityPercent) * cellSizeMeters;
                float stepAirAmp = AcousticBands.DbToAmplitude(-stepAirDb);

                for (int it = 0; it < iterationCount; it++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = IndexOf(x, z);
                            if (blocked[idx])
                            {
                                next[idx] = 0f;
                                continue;
                            }

                            float current = field[idx];
                            float left = SampleNeighbor(field, x - 1, z, current * bandReflection);
                            float right = SampleNeighbor(field, x + 1, z, current * bandReflection);
                            float down = SampleNeighbor(field, x, z - 1, current * bandReflection);
                            float up = SampleNeighbor(field, x, z + 1, current * bandReflection);

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
                    bandFields[b][i] = field[i] * effectiveLateFieldScale;
                }
            }

            hasField = true;
        }

        public void SampleBandPressures(Vector3 point, float[] destinationPa)
        {
            if (destinationPa == null) return;
            for (int b = 0; b < destinationPa.Length; b++) destinationPa[b] = 0f;

            if (!hasField || bandFields == null || width <= 0 || depth <= 0) return;

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

            for (int b = 0; b < AcousticBands.Count; b++)
            {
                float v00 = bandFields[b][IndexOf(x0, z0)];
                float v10 = bandFields[b][IndexOf(x1, z0)];
                float v01 = bandFields[b][IndexOf(x0, z1)];
                float v11 = bandFields[b][IndexOf(x1, z1)];

                float a = Mathf.Lerp(v00, v10, tx);
                float c = Mathf.Lerp(v01, v11, tx);
                destinationPa[b] = Mathf.Lerp(a, c, tz);
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

            int validCount = 0;
            for (int b = 0; b < AcousticBands.Count; b++)
            {
                averageAbsorption[b] = 0f;
                averageReflection[b] = 0f;
            }

            for (int i = 0; i < materials.Count; i++)
            {
                AcousticMaterial material = materials[i];
                if (material == null) continue;
                validCount++;
                for (int b = 0; b < AcousticBands.Count; b++)
                {
                    averageAbsorption[b] += material.GetAbsorption(b);
                    averageReflection[b] += material.GetReflectionAmplitude(b);
                }
            }

            if (validCount <= 0)
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
                averageAbsorption[b] = Mathf.Clamp01(averageAbsorption[b] / validCount);
                averageReflection[b] = Mathf.Clamp01(averageReflection[b] / validCount);
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

        private float SampleNeighbor(float[] field, int x, int z, float fallback)
        {
            if (x < 0 || x >= width || z < 0 || z >= depth) return fallback;
            int idx = IndexOf(x, z);
            if (blocked != null && blocked[idx]) return fallback;
            return field[idx];
        }

        private int IndexOf(int x, int z) => x + (z * width);

        private Vector3 CellCenter(int x, int z)
        {
            return new Vector3(
                bounds.min.x + ((x + 0.5f) * cellSizeMeters),
                yPlane,
                bounds.min.z + ((z + 0.5f) * cellSizeMeters)
            );
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