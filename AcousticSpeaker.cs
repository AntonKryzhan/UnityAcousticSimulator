using UnityEngine;

namespace PhysicalAcousticsSim
{
    [DisallowMultipleComponent]
    public sealed class AcousticSpeaker : MonoBehaviour
    {
        public enum EmissionFace { Forward, Back, Left, Right, Up, Down }

        [SerializeField] private string modelName = "Speaker";
        [SerializeField] private SpeakerRole role = SpeakerRole.FullRange;
        [SerializeField] private bool emitToRoom = true;

        [Header("Electrical / Acoustic")]
        [SerializeField] private float rmsPowerW = 500f;
        [SerializeField] private float lfPowerW = 350f;
        [SerializeField] private float hfPowerW = 150f;
        [SerializeField] private Vector2 frequencyRangeHz = new Vector2(60f, 18000f);
        [SerializeField] private float maxSplDb = 128f;
        [SerializeField] private float subLowFilterHz = 45f;
        [SerializeField] private float inputImpedanceOhm = 20000f;
        [SerializeField] private float nominalInputLevelDbuForMaxSpl = 4f;
        [SerializeField] private float amplifierLatencyMs = 0.35f;
        [SerializeField] private float polarityDegrees = 0f;

        [Header("Construction")]
        [SerializeField] private string commutation = "XLR / TRS";
        [SerializeField] [TextArea(2, 4)] private string driverDescription = "1.7” aluminum compression driver with polyamide diaphragm";
        [SerializeField] private Vector2 horizontalCoverageDegrees = new Vector2(100f, 50f);
        [SerializeField] private float verticalCoverageDeg = 45f;
        [SerializeField] private float crossoverHz = 1600f;
        [SerializeField] private float weightKg = 18f;
        [SerializeField] private Vector3 dimensionsMeters = new Vector3(0.32f, 0.36f, 0.58f);

        [Header("Emission Side")]
        [SerializeField] private EmissionFace emissionFace = EmissionFace.Forward;
        [SerializeField] private bool drawEmissionGizmo = true;
        [SerializeField] private float gizmoArrowLength = 0.75f;
        [SerializeField] private float gizmoArrowHeadLength = 0.18f;
        [SerializeField] private float gizmoArrowHeadAngleDeg = 24f;

        [Header("Simulation Tuning")]
        [SerializeField] private float masterGainDb = 0f;
        [SerializeField] private float[] bandTrimDb = new float[AcousticBands.Count];

        public string ModelName => modelName;
        public SpeakerRole Role => role;
        public bool EmitToRoom => emitToRoom;
        public float InputImpedanceOhm => inputImpedanceOhm;
        public float NominalInputLevelDbuForMaxSpl => nominalInputLevelDbuForMaxSpl;
        public float CrossoverHz => crossoverHz;
        public Vector2 HorizontalCoverageDegrees => horizontalCoverageDegrees;
        public float VerticalCoverageDeg => verticalCoverageDeg;
        public EmissionFace SoundEmissionFace => emissionFace;

        private void Reset()
        {
            AcousticBands.EnsureArray(ref bandTrimDb, 0f);
        }

        private void OnValidate()
        {
            AcousticBands.EnsureArray(ref bandTrimDb, 0f);
            rmsPowerW = Mathf.Max(1f, rmsPowerW);
            lfPowerW = Mathf.Max(0f, lfPowerW);
            hfPowerW = Mathf.Max(0f, hfPowerW);
            frequencyRangeHz.x = Mathf.Max(15f, frequencyRangeHz.x);
            frequencyRangeHz.y = Mathf.Max(frequencyRangeHz.x + 10f, frequencyRangeHz.y);
            maxSplDb = Mathf.Max(60f, maxSplDb);
            subLowFilterHz = Mathf.Max(10f, subLowFilterHz);
            inputImpedanceOhm = Mathf.Max(1f, inputImpedanceOhm);
            nominalInputLevelDbuForMaxSpl = Mathf.Clamp(nominalInputLevelDbuForMaxSpl, -20f, 24f);
            amplifierLatencyMs = Mathf.Max(0f, amplifierLatencyMs);
            verticalCoverageDeg = Mathf.Clamp(verticalCoverageDeg, 1f, 180f);
            horizontalCoverageDegrees.x = Mathf.Clamp(horizontalCoverageDegrees.x, 1f, 180f);
            horizontalCoverageDegrees.y = Mathf.Clamp(horizontalCoverageDegrees.y, 1f, 180f);
            crossoverHz = Mathf.Max(30f, crossoverHz);
            weightKg = Mathf.Max(0.1f, weightKg);
            dimensionsMeters.x = Mathf.Max(0.02f, dimensionsMeters.x);
            dimensionsMeters.y = Mathf.Max(0.02f, dimensionsMeters.y);
            dimensionsMeters.z = Mathf.Max(0.02f, dimensionsMeters.z);
            gizmoArrowLength = Mathf.Max(0.05f, gizmoArrowLength);
            gizmoArrowHeadLength = Mathf.Max(0.02f, gizmoArrowHeadLength);
            gizmoArrowHeadAngleDeg = Mathf.Clamp(gizmoArrowHeadAngleDeg, 5f, 80f);
        }

        public float GetEffectiveSimulationCrossoverHz()
        {
            if (role == SpeakerRole.Subwoofer)
            {
                return Mathf.Clamp(crossoverHz, 60f, 140f);
            }
            return Mathf.Max(30f, crossoverHz);
        }

        public float GetEffectiveSimulationHorizontalCoverageLeftDeg()
        {
            if (role == SpeakerRole.Subwoofer) return 180f;
            return Mathf.Clamp(horizontalCoverageDegrees.x, 1f, 180f);
        }

        public float GetEffectiveSimulationHorizontalCoverageRightDeg()
        {
            if (role == SpeakerRole.Subwoofer) return 180f;
            return Mathf.Clamp(horizontalCoverageDegrees.y, 1f, 180f);
        }

        public float GetEffectiveSimulationVerticalCoverageDeg()
        {
            if (role == SpeakerRole.Subwoofer) return 180f;
            return Mathf.Clamp(verticalCoverageDeg, 1f, 180f);
        }

        public float GetBandResponseAmplitude(int band)
        {
            float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
            float response = AcousticBands.BandPassWeight(frequencyRangeHz.x, frequencyRangeHz.y, f);
            response *= AcousticBands.HPFWeight(subLowFilterHz, f);

            float lowPower = Mathf.Max(1f, lfPowerW);
            float highPower = Mathf.Max(1f, hfPowerW);
            float total = Mathf.Max(1f, lowPower + highPower);

            float effectiveCrossoverHz = GetEffectiveSimulationCrossoverHz();
            float lowBlend = AcousticBands.LPFWeight(Mathf.Max(60f, effectiveCrossoverHz), f);
            float highBlend = 1f - (lowBlend * 0.85f);
            float driverMix = ((lowPower / total) * lowBlend) + ((highPower / total) * highBlend);

            if (role == SpeakerRole.Subwoofer)
            {
                float subLowPass = AcousticBands.LPFWeight(Mathf.Max(70f, effectiveCrossoverHz), f);
                float deepLowPass = AcousticBands.LPFWeight(Mathf.Max(90f, effectiveCrossoverHz * 1.2f), f);
                float subRollOff = f > 250f ? 0.02f : 1f;
                driverMix = 1f;
                response *= subLowPass * deepLowPass * 1.55f * subRollOff;
            }
            else if (role == SpeakerRole.Satellite)
            {
                driverMix *= AcousticBands.HPFWeight(Mathf.Max(70f, effectiveCrossoverHz * 0.65f), f);
            }
            else if (role == SpeakerRole.Monitor)
            {
                driverMix *= 0.95f;
            }

            return Mathf.Max(0.0001f, response * Mathf.Max(0.05f, driverMix));
        }

        public float GetBandBaseSplAt1m(int band)
        {
            float spl = maxSplDb + masterGainDb + bandTrimDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
            spl += AcousticBands.AmplitudeToDb(GetBandResponseAmplitude(band));
            return spl;
        }

        public float GetNominalPressureAt1m(int band, float driveOffsetDb)
        {
            return AcousticBands.DbToPressure(GetBandBaseSplAt1m(band) + driveOffsetDb);
        }

        public float GetPressureAt1mPerVolt(int band)
        {
            float nominalVoltage = 0.775f * AcousticBands.DbToAmplitude(nominalInputLevelDbuForMaxSpl);
            return GetNominalPressureAt1m(band, 0f) / Mathf.Max(0.001f, nominalVoltage);
        }

        public float GetElectricalDelaySeconds()
        {
            return amplifierLatencyMs * 0.001f;
        }

        public float GetElectricalPhaseRadians(int band)
        {
            float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
            return (polarityDegrees * Mathf.Deg2Rad) + (2f * Mathf.PI * f * GetElectricalDelaySeconds());
        }

        public float GetDirectivityAmplitude(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 1e-8f) return 1f;

            Vector3 dir = worldDirection.normalized;
            Vector3 forward = GetEmissionForwardWorld().normalized;

            if (role == SpeakerRole.Subwoofer)
            {
                float frontness = Mathf.Clamp01(0.5f + (0.5f * Vector3.Dot(forward, dir)));
                return Mathf.Lerp(0.82f, 1f, frontness);
            }

            Vector3 up = GetEmissionUpWorld().normalized;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
            {
                up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            }

            Vector3 right = Vector3.Cross(up, forward).normalized;
            up = Vector3.Cross(forward, right).normalized;

            float x = Vector3.Dot(dir, right);
            float y = Vector3.Dot(dir, up);
            float z = Vector3.Dot(dir, forward);

            float hAngle = Mathf.Atan2(x, Mathf.Max(0.0001f, z)) * Mathf.Rad2Deg;
            float vAngle = Mathf.Atan2(y, Mathf.Max(0.0001f, z)) * Mathf.Rad2Deg;

            float horizontalCoverage = hAngle < 0f ? GetEffectiveSimulationHorizontalCoverageLeftDeg() : GetEffectiveSimulationHorizontalCoverageRightDeg();
            float verticalCoverage = GetEffectiveSimulationVerticalCoverageDeg();

            float hNorm = Mathf.Abs(hAngle) / Mathf.Max(1f, horizontalCoverage * 0.5f);
            float vNorm = Mathf.Abs(vAngle) / Mathf.Max(1f, verticalCoverage * 0.5f);

            float amp = BeamAmplitude(hNorm) * BeamAmplitude(vNorm);
            if (z < 0f) amp *= 0.08f;

            return Mathf.Clamp01(amp);
        }

        public Vector3 GetEmissionForwardWorld()
        {
            switch (emissionFace)
            {
                case EmissionFace.Back: return -transform.forward;
                case EmissionFace.Left: return -transform.right;
                case EmissionFace.Right: return transform.right;
                case EmissionFace.Up: return transform.up;
                case EmissionFace.Down: return -transform.up;
                default: return transform.forward;
            }
        }

        public Vector3 GetEmissionUpWorld()
        {
            switch (emissionFace)
            {
                case EmissionFace.Up:
                case EmissionFace.Down: return transform.forward;
                default: return transform.up;
            }
        }

        public Vector3 GetEmissionFaceCenterWorld()
        {
            Bounds bounds = GetBounds();
            switch (emissionFace)
            {
                case EmissionFace.Back: return bounds.center - (transform.forward * bounds.extents.z);
                case EmissionFace.Left: return bounds.center - (transform.right * bounds.extents.x);
                case EmissionFace.Right: return bounds.center + (transform.right * bounds.extents.x);
                case EmissionFace.Up: return bounds.center + (transform.up * bounds.extents.y);
                case EmissionFace.Down: return bounds.center - (transform.up * bounds.extents.y);
                default: return bounds.center + (transform.forward * bounds.extents.z);
            }
        }

        public Bounds GetBounds()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) return c.bounds;
            return new Bounds(transform.position, dimensionsMeters);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawEmissionGizmo) return;

            Vector3 origin = GetEmissionFaceCenterWorld();
            Vector3 direction = GetEmissionForwardWorld().normalized;
            Vector3 up = GetEmissionUpWorld().normalized;

            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.99f)
            {
                up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            }

            Vector3 right = Vector3.Cross(up, direction).normalized;
            up = Vector3.Cross(direction, right).normalized;

            float shaftLength = gizmoArrowLength;
            float headLength = Mathf.Min(gizmoArrowHeadLength, shaftLength * 0.6f);

            Vector3 end = origin + (direction * shaftLength);

            Gizmos.color = emitToRoom ? new Color(0.1f, 1f, 1f, 0.95f) : new Color(0.6f, 0.6f, 0.6f, 0.9f);
            Gizmos.DrawSphere(origin, Mathf.Max(0.01f, Mathf.Min(dimensionsMeters.x, Mathf.Min(dimensionsMeters.y, dimensionsMeters.z)) * 0.08f));
            Gizmos.DrawLine(origin, end);

            Quaternion leftHead = Quaternion.AngleAxis(180f - gizmoArrowHeadAngleDeg, up);
            Quaternion rightHead = Quaternion.AngleAxis(-(180f - gizmoArrowHeadAngleDeg), up);
            Quaternion upHead = Quaternion.AngleAxis(180f - gizmoArrowHeadAngleDeg, right);
            Quaternion downHead = Quaternion.AngleAxis(-(180f - gizmoArrowHeadAngleDeg), right);

            Gizmos.DrawLine(end, end + ((leftHead * direction) * headLength));
            Gizmos.DrawLine(end, end + ((rightHead * direction) * headLength));
            Gizmos.DrawLine(end, end + ((upHead * direction) * headLength));
            Gizmos.DrawLine(end, end + ((downHead * direction) * headLength));
        }

        private static float BeamAmplitude(float normalizedAngle)
        {
            if (normalizedAngle <= 1f)
            {
                float x = Mathf.Cos(normalizedAngle * Mathf.PI * 0.5f);
                return x * x;
            }
            return Mathf.Exp(-(normalizedAngle - 1f) * 5f);
        }
    }
}