using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PhysicalAcousticsSim
{
	[DisallowMultipleComponent]
	public sealed class AcousticSimulator : MonoBehaviour
	{
		[Header("Collection")]
		[SerializeField] private bool autoCollectOnSimulate = true;
		[SerializeField] private Onyx24Mixer mixer;
		[SerializeField] private BandLimitedRoomOperatorSolver lateFieldOperatorSolver;
		[SerializeField] private List<AcousticSpeaker> speakers = new List<AcousticSpeaker>();
		[SerializeField] private List<AcousticMicrophone> microphones = new List<AcousticMicrophone>();
		[SerializeField] private List<AcousticMaterial> materials = new List<AcousticMaterial>();
		[SerializeField] private List<AcousticListenerProbe> listenerProbes = new List<AcousticListenerProbe>();

		[Header("Environment")]
		[SerializeField] private LayerMask acousticLayerMask = ~0;
		[SerializeField] private float airTemperatureC = 20f;
		[SerializeField] [Range(0f, 100f)] private float humidityPercent = 45f;

		[Header("Reflections")]
		[SerializeField] [Range(0f, 3f)] private float reflectionContributionScale = 0.70f;
		[SerializeField] [Min(0)] private int maxReflectionOrder = 2;
		[SerializeField] [Min(1)] private int raysPerSpeaker = 96;
		[SerializeField] [Min(1f)] private float maxRayDistanceMeters = 60f;
		[SerializeField] [Min(0.001f)] private float surfaceOffset = 0.02f;

		[Header("Structure Transmission")]
		[SerializeField] private bool enableStructureTransmission = true;
		[SerializeField] [Min(0.001f)] private float structureContactGapMeters = 0.025f;
		[SerializeField] [Min(0.10f)] private float structureSurfaceInfluenceMeters = 1.5f;
		[SerializeField] [Min(0.01f)] private float structureDecayPerMeter = 0.70f;
		[SerializeField] [Min(10f)] private float structureWaveSpeedMps = 900f;

		[Header("Analysis Grid")]
		[SerializeField] [Min(0.10f)] private float gridStepMeters = 0.75f;
		[SerializeField] private float probeHeight = 1.45f;
		[SerializeField] private AcousticMicrophone virtualProbeTemplate;

		[Header("Debug Draw")]
		[SerializeField] private bool drawHotspots = true;
		[SerializeField] private bool drawSpeakerMicLinks = true;
		[SerializeField] private bool drawPropagationRays = true;
		[SerializeField] [Min(1)] private int gizmoRaysPerSpeaker = 24;
		[SerializeField] [Min(0)] private int gizmoRayBounceDepth = 2;
		[SerializeField] [Range(0.05f, 1f)] private float gizmoRayIntensity = 0.7f;
		[SerializeField] [Min(0.01f)] private float gizmoSphereRadius = 0.10f;

		[Header("Console Output")]
		[SerializeField] private bool logResultsToConsole = true;
		[SerializeField] private bool logPerBandResults = true;
		[SerializeField] [Min(1)] private int maxLoggedHotspots = 20;

		[Header("Results")]
		[SerializeField] private Bounds roomBounds;
		[SerializeField] private List<MicFeedbackResult> microphoneResults = new List<MicFeedbackResult>();
		[SerializeField] private List<HotspotResult> hotspots = new List<HotspotResult>();

		private readonly Dictionary<AcousticMaterial, int> materialIndices = new Dictionary<AcousticMaterial, int>();
		private readonly List<AcousticMaterial> structuralMaterials = new List<AcousticMaterial>();
		private readonly List<Bounds> materialBounds = new List<Bounds>();
		private readonly List<List<GraphEdge>> materialGraph = new List<List<GraphEdge>>();
		private readonly List<DebugRaySegment> debugRaySegments = new List<DebugRaySegment>();

		private struct GraphEdge
		{
			public int to;
			public float cost;
		}

		private struct DebugRaySegment
		{
			public Vector3 from;
			public Vector3 to;
			public float intensity;
			public int bounce;
		}

		private sealed class ComplexField
		{
			public readonly float[] re = new float[AcousticBands.Count];
			public readonly float[] im = new float[AcousticBands.Count];

			public void AddPolar(int band, float amplitude, float phaseRad)
			{
				re[band] += amplitude * Mathf.Cos(phaseRad);
				im[band] += amplitude * Mathf.Sin(phaseRad);
			}

			public float Magnitude(int band)
			{
				return Mathf.Sqrt((re[band] * re[band]) + (im[band] * im[band]));
			}

			public void AddScaledRotated(ComplexField source, int band, float scale, float phaseRad)
			{
				float c = Mathf.Cos(phaseRad);
				float s = Mathf.Sin(phaseRad);
				re[band] += scale * ((source.re[band] * c) - (source.im[band] * s));
				im[band] += scale * ((source.re[band] * s) + (source.im[band] * c));
			}

			public void AddField(ComplexField source)
			{
				for (int i = 0; i < AcousticBands.Count; i++)
				{
					re[i] += source.re[i];
					im[i] += source.im[i];
				}
			}
		}

		private sealed class PointBreakdown
		{
			public readonly float[] directBandDb = new float[AcousticBands.Count];
			public readonly float[] reflectionBandDb = new float[AcousticBands.Count];
			public readonly float[] lateBandDb = new float[AcousticBands.Count];
			public readonly float[] structureBandDb = new float[AcousticBands.Count];
			public readonly float[] totalBandDb = new float[AcousticBands.Count];

			public float directTotalDb;
			public float reflectionTotalDb;
			public float lateTotalDb;
			public float structureTotalDb;
			public float totalDb;
		}

		public List<MicFeedbackResult> MicrophoneResults => microphoneResults;
		public List<HotspotResult> Hotspots => hotspots;

		[ContextMenu("Auto Collect Scene Objects")]
		public void AutoCollect()
		{
			speakers = new List<AcousticSpeaker>(FindObjectsByType<AcousticSpeaker>(FindObjectsSortMode.None));
			microphones = new List<AcousticMicrophone>(FindObjectsByType<AcousticMicrophone>(FindObjectsSortMode.None));
			materials = new List<AcousticMaterial>(FindObjectsByType<AcousticMaterial>(FindObjectsSortMode.None));
			listenerProbes = new List<AcousticListenerProbe>(FindObjectsByType<AcousticListenerProbe>(FindObjectsSortMode.None));

			if (mixer == null)
			{
				Onyx24Mixer[] mixers = FindObjectsByType<Onyx24Mixer>(FindObjectsSortMode.None);
				if (mixers.Length > 0)
				{
					mixer = mixers[0];
				}
			}

			if (lateFieldOperatorSolver == null)
			{
				lateFieldOperatorSolver = GetComponent<BandLimitedRoomOperatorSolver>();
				if (lateFieldOperatorSolver == null)
				{
					BandLimitedRoomOperatorSolver[] solvers = FindObjectsByType<BandLimitedRoomOperatorSolver>(FindObjectsSortMode.None);
					if (solvers.Length > 0)
					{
						lateFieldOperatorSolver = solvers[0];
					}
				}
			}
		}

		[ContextMenu("Simulate Acoustics")]
		public void Simulate()
		{
			if (autoCollectOnSimulate)
			{
				AutoCollect();
			}

			RebuildMaterialGraph();
			roomBounds = ComputeRoomBounds();
			microphoneResults.Clear();
			hotspots.Clear();
			ClearDebugPropagationRays();

			if (lateFieldOperatorSolver != null && lateFieldOperatorSolver.isActiveAndEnabled)
			{
				lateFieldOperatorSolver.BuildField(
					roomBounds,
					speakers,
					materials,
					mixer,
					acousticLayerMask,
					airTemperatureC,
					humidityPercent,
					probeHeight,
					IsPointInsideMaterial
				);
			}

			BuildDebugPropagationRays();
			AnalyzeMicrophones();
			AnalyzeListeners();
			AnalyzeGrid();

			if (logResultsToConsole)
			{
				LogSimulationResults();
			}
		}

		[ContextMenu("Clear Results")]
		public void ClearResults()
		{
			microphoneResults.Clear();
			hotspots.Clear();
			ClearDebugPropagationRays();

			if (lateFieldOperatorSolver != null)
			{
				lateFieldOperatorSolver.ClearField();
			}
		}

		private void AnalyzeMicrophones()
		{
			if (microphones == null)
			{
				return;
			}

			for (int i = 0; i < microphones.Count; i++)
			{
				AcousticMicrophone mic = microphones[i];
				if (mic == null)
				{
					continue;
				}

				float[] bandSplDb = new float[AcousticBands.Count];
				float totalSplDb;
				float structureDb;
				ComputeActualPointState(mic.transform.position, bandSplDb, out totalSplDb, out structureDb);

				MicFeedbackResult result = new MicFeedbackResult();
				result.microphone = mic;
				result.totalSplDb = totalSplDb;
				result.structureVibrationDb = structureDb;
				AcousticBands.Copy(bandSplDb, result.bandSplDb);

				float worstLoopDb;
				AcousticSpeaker worstSpeaker;
				int worstBand;
				worstLoopDb = EstimateFeedbackLoopDbAtPoint(mic, mic.transform.position, result.loopGainDb, out worstSpeaker, out worstBand);
				result.worstSpeaker = worstSpeaker;
				result.feedbackMarginDb = -worstLoopDb;
				result.worstBandFrequencyHz = worstBand >= 0 ? AcousticBands.CenterFrequenciesHz[worstBand] : 0f;
				result.dominantDirection = worstSpeaker != null ? (worstSpeaker.transform.position - mic.transform.position).normalized : Vector3.zero;
				microphoneResults.Add(result);
			}
		}

		private void AnalyzeListeners()
		{
			if (listenerProbes == null)
			{
				return;
			}

			for (int i = 0; i < listenerProbes.Count; i++)
			{
				AcousticListenerProbe probe = listenerProbes[i];
				if (probe == null)
				{
					continue;
				}

				float[] bandSplDb = new float[AcousticBands.Count];
				float totalSplDb;
				float structureDb;
				ComputeActualPointState(probe.transform.position, bandSplDb, out totalSplDb, out structureDb);

				int peakBand = 0;
				float peakDb = float.NegativeInfinity;
				for (int b = 0; b < AcousticBands.Count; b++)
				{
					if (bandSplDb[b] > peakDb)
					{
						peakDb = bandSplDb[b];
						peakBand = b;
					}
				}

				probe.StoreResult(totalSplDb, AcousticBands.CenterFrequenciesHz[peakBand], structureDb, bandSplDb);
			}
		}

		private void AnalyzeGrid()
		{
			if (roomBounds.size.sqrMagnitude < 1e-6f)
			{
				return;
			}

			float actualWorstMarginDb = GetWorstActualFeedbackMarginDb();
			float minX = roomBounds.min.x + (gridStepMeters * 0.5f);
			float maxX = roomBounds.max.x - (gridStepMeters * 0.5f);
			float minZ = roomBounds.min.z + (gridStepMeters * 0.5f);
			float maxZ = roomBounds.max.z - (gridStepMeters * 0.5f);
			float y = Mathf.Clamp(probeHeight, roomBounds.min.y + 0.1f, roomBounds.max.y - 0.1f);

			for (float x = minX; x <= maxX; x += gridStepMeters)
			{
				for (float z = minZ; z <= maxZ; z += gridStepMeters)
				{
					Vector3 point = new Vector3(x, y, z);
					if (IsPointInsideMaterial(point))
					{
						continue;
					}

					float[] bandSplDb = new float[AcousticBands.Count];
					float totalSplDb;
					float structureDb;
					ComputeActualPointState(point, bandSplDb, out totalSplDb, out structureDb);

					AcousticSpeaker worstSpeaker = null;
					int worstBand = -1;
					float[] loopBuffer = new float[AcousticBands.Count];
					float worstLoopDb = -120f;

					if (virtualProbeTemplate != null)
					{
						worstLoopDb = EstimateFeedbackLoopDbAtPoint(virtualProbeTemplate, point, loopBuffer, out worstSpeaker, out worstBand);
					}

					int peakBand = 0;
					float peakDb = float.NegativeInfinity;
					for (int b = 0; b < AcousticBands.Count; b++)
					{
						if (bandSplDb[b] > peakDb)
						{
							peakDb = bandSplDb[b];
							peakBand = b;
						}
					}

					float rawRisk = Mathf.Clamp01(Mathf.InverseLerp(-10f, 3f, worstLoopDb));
					float risk = NormalizeHotspotRisk(rawRisk, actualWorstMarginDb, totalSplDb);

					if (risk <= 0.04f && totalSplDb < 72f)
					{
						continue;
					}

					HotspotResult spot = new HotspotResult();
					spot.position = point;
					spot.totalSplDb = totalSplDb;
					spot.risk = risk;
					spot.peakFrequencyHz = worstBand >= 0 ? AcousticBands.CenterFrequenciesHz[worstBand] : AcousticBands.CenterFrequenciesHz[peakBand];
					spot.structureVibrationDb = structureDb;
					AcousticBands.Copy(bandSplDb, spot.bandSplDb);
					hotspots.Add(spot);
				}
			}
		}

		private float NormalizeHotspotRisk(float rawRisk, float actualWorstMarginDb, float totalSplDb)
		{
			float safety01 = Mathf.Clamp01(actualWorstMarginDb / 24f);
			float normalization = Mathf.Lerp(1f, 0.30f, safety01);
			float splBoost = Mathf.Lerp(0f, 0.20f, Mathf.InverseLerp(95f, 120f, totalSplDb));
			return Mathf.Clamp01(rawRisk * (normalization + splBoost));
		}

		private float GetWorstActualFeedbackMarginDb()
		{
			if (microphoneResults == null || microphoneResults.Count == 0)
			{
				return 0f;
			}

			float worst = float.PositiveInfinity;
			bool found = false;
			for (int i = 0; i < microphoneResults.Count; i++)
			{
				MicFeedbackResult result = microphoneResults[i];
				if (result == null || result.microphone == null)
				{
					continue;
				}

				found = true;
				worst = Mathf.Min(worst, result.feedbackMarginDb);
			}

			return found ? worst : 0f;
		}

		private void ComputeActualPointState(Vector3 point, float[] bandSplDb, out float totalSplDb, out float structureDb)
		{
			ComplexField total = new ComplexField();
			ComplexField structureOnly = new ComplexField();

			for (int i = 0; i < speakers.Count; i++)
			{
				AcousticSpeaker speaker = speakers[i];
				if (speaker == null || !speaker.EmitToRoom)
				{
					continue;
				}

				ComplexField geometric = ComputeGeometricTransferRelative(speaker, point);
				ComplexField structure = enableStructureTransmission ? ComputeStructureTransferRelative(speaker, point) : new ComplexField();

				for (int b = 0; b < AcousticBands.Count; b++)
				{
					float sourcePhase = 0f;
					float driveDb;
					if (mixer != null)
					{
						driveDb = mixer.GetSpeakerBandEmissionOffsetDb(speaker, b, out sourcePhase);
					}
					else
					{
						driveDb = -speaker.NominalInputLevelDbuForMaxSpl;
					}

					float p1m = speaker.GetNominalPressureAt1m(b, driveDb);
					float phase = sourcePhase + speaker.GetElectricalPhaseRadians(b);
					total.AddScaledRotated(geometric, b, p1m, phase);
					total.AddScaledRotated(structure, b, p1m, phase);
					structureOnly.AddScaledRotated(structure, b, p1m, phase);
				}
			}

			float[] lateFieldBandPressuresPa = new float[AcousticBands.Count];
			if (lateFieldOperatorSolver != null && lateFieldOperatorSolver.HasField)
			{
				lateFieldOperatorSolver.SampleBandPressures(point, lateFieldBandPressuresPa);
			}

			float[] bandPressuresPa = new float[AcousticBands.Count];
			float[] structureBandPressuresPa = new float[AcousticBands.Count];

			for (int b = 0; b < AcousticBands.Count; b++)
			{
				float coherentPa = total.Magnitude(b);
				float latePa = lateFieldBandPressuresPa[b];
				bandPressuresPa[b] = Mathf.Sqrt((coherentPa * coherentPa) + (latePa * latePa));
				structureBandPressuresPa[b] = structureOnly.Magnitude(b);
				bandSplDb[b] = AcousticBands.PressureToDb(bandPressuresPa[b]);
			}

			totalSplDb = AcousticBands.SumPressureBandsToDb(bandPressuresPa);
			structureDb = AcousticBands.SumPressureBandsToDb(structureBandPressuresPa);
		}

		private PointBreakdown EvaluatePointBreakdown(Vector3 point)
		{
			PointBreakdown breakdown = new PointBreakdown();
			ComplexField directTotal = new ComplexField();
			ComplexField reflectionTotal = new ComplexField();
			ComplexField structureTotal = new ComplexField();
			ComplexField coherentTotal = new ComplexField();

			for (int i = 0; i < speakers.Count; i++)
			{
				AcousticSpeaker speaker = speakers[i];
				if (speaker == null || !speaker.EmitToRoom)
				{
					continue;
				}

				ComplexField directField = new ComplexField();
				ComplexField reflectionField = new ComplexField();
				ComputeGeometricTransferComponentsRelative(speaker, point, directField, reflectionField);
				ComplexField structureField = enableStructureTransmission ? ComputeStructureTransferRelative(speaker, point) : new ComplexField();

				for (int b = 0; b < AcousticBands.Count; b++)
				{
					float sourcePhase = 0f;
					float driveDb;
					if (mixer != null)
					{
						driveDb = mixer.GetSpeakerBandEmissionOffsetDb(speaker, b, out sourcePhase);
					}
					else
					{
						driveDb = -speaker.NominalInputLevelDbuForMaxSpl;
					}

					float p1m = speaker.GetNominalPressureAt1m(b, driveDb);
					float phase = sourcePhase + speaker.GetElectricalPhaseRadians(b);
					directTotal.AddScaledRotated(directField, b, p1m, phase);
					reflectionTotal.AddScaledRotated(reflectionField, b, p1m, phase);
					structureTotal.AddScaledRotated(structureField, b, p1m, phase);
					coherentTotal.AddScaledRotated(directField, b, p1m, phase);
					coherentTotal.AddScaledRotated(reflectionField, b, p1m, phase);
					coherentTotal.AddScaledRotated(structureField, b, p1m, phase);
				}
			}

			float[] directPa = new float[AcousticBands.Count];
			float[] reflectionPa = new float[AcousticBands.Count];
			float[] structurePa = new float[AcousticBands.Count];
			float[] coherentPa = new float[AcousticBands.Count];
			float[] latePa = new float[AcousticBands.Count];
			float[] totalPa = new float[AcousticBands.Count];

			if (lateFieldOperatorSolver != null && lateFieldOperatorSolver.HasField)
			{
				lateFieldOperatorSolver.SampleBandPressures(point, latePa);
			}

			for (int b = 0; b < AcousticBands.Count; b++)
			{
				directPa[b] = directTotal.Magnitude(b);
				reflectionPa[b] = reflectionTotal.Magnitude(b);
				structurePa[b] = structureTotal.Magnitude(b);
				coherentPa[b] = coherentTotal.Magnitude(b);
				totalPa[b] = Mathf.Sqrt((coherentPa[b] * coherentPa[b]) + (latePa[b] * latePa[b]));
				breakdown.directBandDb[b] = AcousticBands.PressureToDb(directPa[b]);
				breakdown.reflectionBandDb[b] = AcousticBands.PressureToDb(reflectionPa[b]);
				breakdown.structureBandDb[b] = AcousticBands.PressureToDb(structurePa[b]);
				breakdown.lateBandDb[b] = AcousticBands.PressureToDb(latePa[b]);
				breakdown.totalBandDb[b] = AcousticBands.PressureToDb(totalPa[b]);
			}

			breakdown.directTotalDb = AcousticBands.SumPressureBandsToDb(directPa);
			breakdown.reflectionTotalDb = AcousticBands.SumPressureBandsToDb(reflectionPa);
			breakdown.structureTotalDb = AcousticBands.SumPressureBandsToDb(structurePa);
			breakdown.lateTotalDb = AcousticBands.SumPressureBandsToDb(latePa);
			breakdown.totalDb = AcousticBands.SumPressureBandsToDb(totalPa);
			return breakdown;
		}

		private float EstimateFeedbackLoopDbAtPoint(AcousticMicrophone microphone, Vector3 point, float[] loopGainDb, out AcousticSpeaker worstSpeaker, out int worstBand)
		{
			worstSpeaker = null;
			worstBand = -1;

			if (loopGainDb != null)
			{
				for (int i = 0; i < loopGainDb.Length; i++)
				{
					loopGainDb[i] = -120f;
				}
			}

			if (microphone == null || mixer == null)
			{
				return -120f;
			}

			float worstLoopDb = -120f;
			for (int i = 0; i < speakers.Count; i++)
			{
				AcousticSpeaker speaker = speakers[i];
				if (speaker == null || !speaker.EmitToRoom)
				{
					continue;
				}

				ComplexField roomField = ComputeGeometricTransferRelative(speaker, point);
				if (enableStructureTransmission)
				{
					roomField.AddField(ComputeStructureTransferRelative(speaker, point));
				}

				float micPolarAmp = microphone.GetPolarAmplitudeTo(speaker.transform.position);
				for (int b = 0; b < AcousticBands.Count; b++)
				{
					float chainPhase;
					float chainGain = mixer.GetMicToSpeakerVoltageGainAmplitude(microphone, speaker, b, out chainPhase);
					if (chainGain <= 0f)
					{
						continue;
					}

					float roomAmp = roomField.Magnitude(b);
					float roomPhase = Mathf.Atan2(roomField.im[b], roomField.re[b]);
					float micSensitivityVPerPa = microphone.SensitivityVoltsPerPascal * microphone.GetFrequencyResponseAmplitude(b) * micPolarAmp;
					float speakerPaPerV = speaker.GetPressureAt1mPerVolt(b);
					float loopAmplitude = roomAmp * micSensitivityVPerPa * chainGain * speakerPaPerV;
					float totalPhase = roomPhase + chainPhase + speaker.GetElectricalPhaseRadians(b);
					float phaseWrapped = Mathf.Repeat(totalPhase, Mathf.PI * 2f);
					float phaseError = Mathf.Min(phaseWrapped, (Mathf.PI * 2f) - phaseWrapped);
					float phaseWeight = Mathf.Lerp(0.35f, 1f, 1f - Mathf.Clamp01(phaseError / Mathf.PI));
					float effectiveLoop = Mathf.Max(1e-8f, loopAmplitude * phaseWeight);
					float loopDb = AcousticBands.AmplitudeToDb(effectiveLoop);

					if (loopGainDb != null)
					{
						loopGainDb[b] = Mathf.Max(loopGainDb[b], loopDb);
					}

					if (loopDb > worstLoopDb)
					{
						worstLoopDb = loopDb;
						worstSpeaker = speaker;
						worstBand = b;
					}
				}
			}

			return worstLoopDb;
		}

		private ComplexField ComputeGeometricTransferRelative(AcousticSpeaker speaker, Vector3 point)
		{
			ComplexField directField = new ComplexField();
			ComplexField reflectionField = new ComplexField();
			ComputeGeometricTransferComponentsRelative(speaker, point, directField, reflectionField);
			directField.AddField(reflectionField);
			return directField;
		}

		private void ComputeGeometricTransferComponentsRelative(AcousticSpeaker speaker, Vector3 point, ComplexField directField, ComplexField reflectionField)
		{
			if (speaker == null)
			{
				return;
			}

			Vector3 origin = speaker.transform.position;
			Vector3 toPoint = point - origin;
			float distance = Mathf.Max(0.01f, toPoint.magnitude);
			Vector3 dir = toPoint / distance;
			float directivity = speaker.GetDirectivityAmplitude(dir);
			float[] directTransmission = GetTransmissionAmplitudeAlongSegment(origin, point, GetPrimaryCollider(speaker), null);
			float speed = AcousticBands.SpeedOfSound(airTemperatureC);

			for (int b = 0; b < AcousticBands.Count; b++)
			{
				float f = AcousticBands.CenterFrequenciesHz[b];
				float airDb = AcousticBands.AirAbsorptionDbPerMeter(f, airTemperatureC, humidityPercent) * distance;
				float airAmp = AcousticBands.DbToAmplitude(-airDb);
				float amp = (directivity / Mathf.Max(1f, distance)) * directTransmission[b] * airAmp;
				float phase = 2f * Mathf.PI * f * (distance / speed);
				directField.AddPolar(b, amp, phase);
			}

			if (maxReflectionOrder <= 0 || raysPerSpeaker <= 0)
			{
				return;
			}

			uint seed = AcousticsMath.Hash((uint)speaker.GetInstanceID()) ^ AcousticsMath.Hash(point);
			Vector3 sampleForward = speaker.GetEmissionForwardWorld();
			Vector3 sampleUp = speaker.GetEmissionUpWorld();

			for (int r = 0; r < raysPerSpeaker; r++)
			{
				float hLeft = speaker.GetEffectiveSimulationHorizontalCoverageLeftDeg();
				float hRight = speaker.GetEffectiveSimulationHorizontalCoverageRightDeg();
				float v = speaker.GetEffectiveSimulationVerticalCoverageDeg();
				Vector3 rayDir = AcousticsMath.SampleAsymmetricCone(
					sampleForward,
					sampleUp,
					hLeft,
					hRight,
					v,
					ref seed
				);

				Vector3 rayOrigin = origin + (rayDir * surfaceOffset);
				float traveled = 0f;
				float[] weights = new float[AcousticBands.Count];
				for (int b = 0; b < AcousticBands.Count; b++)
				{
					weights[b] = 1f;
				}

				for (int bounce = 0; bounce < maxReflectionOrder; bounce++)
				{
					RaycastHit hit;
					if (!Physics.Raycast(rayOrigin, rayDir, out hit, maxRayDistanceMeters, acousticLayerMask, QueryTriggerInteraction.Ignore))
					{
						break;
					}

					traveled += hit.distance;
					AcousticMaterial material = GetMaterial(hit.collider);
					if (material == null)
					{
						rayDir = Vector3.Reflect(rayDir, hit.normal).normalized;
						rayOrigin = hit.point + (rayDir * surfaceOffset);
						continue;
					}

					Vector3 bounceToPoint = point - hit.point;
					float tailDistance = bounceToPoint.magnitude;
					if (tailDistance > 0.05f)
					{
						float[] tailTransmission = GetTransmissionAmplitudeAlongSegment(hit.point + (hit.normal * surfaceOffset), point, hit.collider, null);
						float lambert = Mathf.Clamp01(Vector3.Dot(hit.normal, bounceToPoint.normalized));
						float totalPathDistance = traveled + tailDistance;

						for (int b = 0; b < AcousticBands.Count; b++)
						{
							float f = AcousticBands.CenterFrequenciesHz[b];
							float airDb = AcousticBands.AirAbsorptionDbPerMeter(f, airTemperatureC, humidityPercent) * totalPathDistance;
							float airAmp = AcousticBands.DbToAmplitude(-airDb);
							float reflectAmp = material.GetReflectionAmplitude(b);
							float amp = weights[b] * reflectAmp * Mathf.Max(0.05f, lambert) * tailTransmission[b] * (reflectionContributionScale / Mathf.Max(1, raysPerSpeaker)) * (1f / Mathf.Max(1f, totalPathDistance)) * airAmp;
							float phase = 2f * Mathf.PI * f * (totalPathDistance / speed);
							reflectionField.AddPolar(b, amp, phase);
						}
					}

					float scatter = material.GetScattering(4);
					Vector3 reflected = Vector3.Reflect(rayDir, hit.normal).normalized;
					Vector3 diffuse = AcousticsMath.SampleHemisphere(hit.normal, ref seed);
					rayDir = Vector3.Slerp(reflected, diffuse, scatter).normalized;
					rayOrigin = hit.point + (rayDir * surfaceOffset);

					for (int b = 0; b < AcousticBands.Count; b++)
					{
						weights[b] *= material.GetReflectionAmplitude(b);
					}
				}
			}
		}

		private ComplexField ComputeStructureTransferRelative(AcousticSpeaker speaker, Vector3 point)
		{
			ComplexField field = new ComplexField();
			if (!enableStructureTransmission || speaker == null || structuralMaterials.Count == 0)
			{
				return field;
			}

			List<int> touched = GetTouchingMaterialIndices(speaker.GetBounds());
			if (touched.Count == 0)
			{
				return field;
			}

			float[] shortest = ComputeGraphDistances(touched);
			for (int i = 0; i < structuralMaterials.Count; i++)
			{
				AcousticMaterial material = structuralMaterials[i];
				if (material == null)
				{
					continue;
				}

				float pathDistance = shortest[i];
				if (float.IsPositiveInfinity(pathDistance))
				{
					continue;
				}

				float surfaceDistance = AcousticsMath.DistanceToBounds(materialBounds[i], point);
				if (surfaceDistance > structureSurfaceInfluenceMeters)
				{
					continue;
				}

				for (int b = 0; b < AcousticBands.Count; b++)
				{
					float f = AcousticBands.CenterFrequenciesHz[b];
					float structureCutoffHz = speaker.Role == SpeakerRole.Subwoofer
						? Mathf.Clamp(speaker.GetEffectiveSimulationCrossoverHz() * 1.15f, 70f, 160f)
						: Mathf.Clamp(speaker.GetEffectiveSimulationCrossoverHz() * 0.60f, 70f, 160f);

					float lowWeight = AcousticBands.LPFWeight(structureCutoffHz, f);
					if (lowWeight < 0.01f)
					{
						continue;
					}

					float bandShape = speaker.GetBandResponseAmplitude(b);
					float decay = Mathf.Exp(-pathDistance * structureDecayPerMeter * Mathf.Lerp(0.90f, 2.40f, b / 7f));
					float reradiation = 1f / Mathf.Max(1f, 0.50f + (surfaceDistance * 4.00f));
					float amp = 0.08f * lowWeight * bandShape * material.GetStructureAmplitude(b) * (1f - material.Damping * 0.70f) * decay * reradiation;
					float phase = 2f * Mathf.PI * f * (pathDistance / Mathf.Max(10f, structureWaveSpeedMps));
					field.AddPolar(b, amp, phase);
				}
			}

			return field;
		}

		private float[] GetTransmissionAmplitudeAlongSegment(Vector3 a, Vector3 b, Collider ignoreA, Collider ignoreB)
		{
			float[] transmission = new float[AcousticBands.Count];
			for (int i = 0; i < AcousticBands.Count; i++)
			{
				transmission[i] = 1f;
			}

			Vector3 delta = b - a;
			float distance = delta.magnitude;
			if (distance <= 0.01f)
			{
				return transmission;
			}

			RaycastHit[] hits = Physics.RaycastAll(a, delta / distance, distance - surfaceOffset, acousticLayerMask, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < hits.Length; i++)
			{
				Collider c = hits[i].collider;
				if (c == null || c == ignoreA || c == ignoreB)
				{
					continue;
				}

				AcousticMaterial material = GetMaterial(c);
				if (material != null)
				{
					for (int bnd = 0; bnd < AcousticBands.Count; bnd++)
					{
						transmission[bnd] *= material.GetTransmissionAmplitude(bnd);
					}
				}
				else
				{
					for (int bnd = 0; bnd < AcousticBands.Count; bnd++)
					{
						transmission[bnd] *= 0.10f;
					}
				}
			}

			return transmission;
		}

		private bool IsPointInsideMaterial(Vector3 point)
		{
			for (int i = 0; i < materials.Count; i++)
			{
				AcousticMaterial material = materials[i];
				if (material == null)
				{
					continue;
				}

				Collider c = material.GetComponent<Collider>();
				if (c == null)
				{
					continue;
				}

				Vector3 closest = c.ClosestPoint(point);
				if ((closest - point).sqrMagnitude < 0.000001f)
				{
					return true;
				}
			}

			return false;
		}

		private void RebuildMaterialGraph()
		{
			materialIndices.Clear();
			structuralMaterials.Clear();
			materialBounds.Clear();
			materialGraph.Clear();

			for (int i = 0; i < materials.Count; i++)
			{
				AcousticMaterial material = materials[i];
				if (!ShouldMaterialParticipateInStructureGraph(material))
				{
					continue;
				}

				materialIndices[material] = materialBounds.Count;
				structuralMaterials.Add(material);
				materialBounds.Add(material.GetBounds());
				materialGraph.Add(new List<GraphEdge>());
			}

			for (int i = 0; i < materialBounds.Count; i++)
			{
				for (int j = i + 1; j < materialBounds.Count; j++)
				{
					if (!AcousticsMath.BoundsTouchOrIntersect(materialBounds[i], materialBounds[j], structureContactGapMeters))
					{
						continue;
					}

					float cost = Vector3.Distance(materialBounds[i].center, materialBounds[j].center);
					cost = Mathf.Max(0.05f, cost);
					materialGraph[i].Add(new GraphEdge { to = j, cost = cost });
					materialGraph[j].Add(new GraphEdge { to = i, cost = cost });
				}
			}
		}

		private static bool ShouldMaterialParticipateInStructureGraph(AcousticMaterial material)
		{
			if (material == null)
			{
				return false;
			}

			switch (material.Preset)
			{
				case MaterialPreset.Concrete:
				case MaterialPreset.Wood:
				case MaterialPreset.Metal:
				case MaterialPreset.Glass:
				case MaterialPreset.Plaster:
					break;

				default:
					return false;
			}

			Bounds bounds = material.GetBounds();
			float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
			float footprint = bounds.size.x * bounds.size.z;
			if (maxDim < 0.35f && footprint < 0.40f)
			{
				return false;
			}

			return true;
		}

		private List<int> GetTouchingMaterialIndices(Bounds speakerBounds)
		{
			List<int> touched = new List<int>();
			for (int i = 0; i < materialBounds.Count; i++)
			{
				if (AcousticsMath.BoundsTouchOrIntersect(speakerBounds, materialBounds[i], structureContactGapMeters))
				{
					touched.Add(i);
				}
			}

			return touched;
		}

		private float[] ComputeGraphDistances(List<int> sources)
		{
			int count = materialGraph.Count;
			float[] distance = new float[count];
			bool[] visited = new bool[count];

			for (int i = 0; i < count; i++)
			{
				distance[i] = float.PositiveInfinity;
			}

			for (int i = 0; i < sources.Count; i++)
			{
				int s = sources[i];
				if (s >= 0 && s < count)
				{
					distance[s] = 0f;
				}
			}

			for (int step = 0; step < count; step++)
			{
				int bestIndex = -1;
				float bestDistance = float.PositiveInfinity;

				for (int i = 0; i < count; i++)
				{
					if (!visited[i] && distance[i] < bestDistance)
					{
						bestDistance = distance[i];
						bestIndex = i;
					}
				}

				if (bestIndex < 0)
				{
					break;
				}

				visited[bestIndex] = true;
				List<GraphEdge> edges = materialGraph[bestIndex];
				for (int e = 0; e < edges.Count; e++)
				{
					GraphEdge edge = edges[e];
					float nd = distance[bestIndex] + edge.cost;
					if (nd < distance[edge.to])
					{
						distance[edge.to] = nd;
					}
				}
			}

			return distance;
		}

		private Bounds ComputeRoomBounds()
		{
			Bounds bounds = new Bounds(transform.position, Vector3.zero);
			bool initialized = false;

			for (int i = 0; i < materials.Count; i++)
			{
				AcousticMaterial material = materials[i];
				if (material == null)
				{
					continue;
				}

				if (!initialized)
				{
					bounds = material.GetBounds();
					initialized = true;
				}
				else
				{
					bounds.Encapsulate(material.GetBounds());
				}
			}

			for (int i = 0; i < speakers.Count; i++)
			{
				AcousticSpeaker speaker = speakers[i];
				if (speaker == null)
				{
					continue;
				}

				if (!initialized)
				{
					bounds = speaker.GetBounds();
					initialized = true;
				}
				else
				{
					bounds.Encapsulate(speaker.GetBounds());
				}
			}

			for (int i = 0; i < microphones.Count; i++)
			{
				AcousticMicrophone mic = microphones[i];
				if (mic == null)
				{
					continue;
				}

				if (!initialized)
				{
					bounds = new Bounds(mic.transform.position, Vector3.one * 0.2f);
					initialized = true;
				}
				else
				{
					bounds.Encapsulate(mic.transform.position);
				}
			}

			if (!initialized)
			{
				bounds = new Bounds(transform.position, new Vector3(10f, 4f, 10f));
			}

			bounds.Expand(new Vector3(0.5f, 0.5f, 0.5f));
			return bounds;
		}

		private void BuildDebugPropagationRays()
		{
			if (!drawPropagationRays || speakers == null || speakers.Count == 0 || gizmoRaysPerSpeaker <= 0)
			{
				return;
			}

			for (int i = 0; i < speakers.Count; i++)
			{
				AcousticSpeaker speaker = speakers[i];
				if (speaker == null || !speaker.EmitToRoom)
				{
					continue;
				}

				uint seed = AcousticsMath.Hash((uint)speaker.GetInstanceID()) ^ 0x5A17C9E3u;
				Vector3 emissionForward = speaker.GetEmissionForwardWorld();
				Vector3 emissionUp = speaker.GetEmissionUpWorld();
				Vector3 origin = speaker.GetEmissionFaceCenterWorld();

				for (int r = 0; r < gizmoRaysPerSpeaker; r++)
				{
					Vector3 rayDir = AcousticsMath.SampleAsymmetricCone(
						emissionForward,
						emissionUp,
						speaker.GetEffectiveSimulationHorizontalCoverageLeftDeg(),
						speaker.GetEffectiveSimulationHorizontalCoverageRightDeg(),
						speaker.GetEffectiveSimulationVerticalCoverageDeg(),
						ref seed
					);

					Vector3 rayOrigin = origin + (rayDir * surfaceOffset);
					float intensity = gizmoRayIntensity;

					for (int bounce = 0; bounce <= gizmoRayBounceDepth; bounce++)
					{
						RaycastHit hit;
						if (Physics.Raycast(rayOrigin, rayDir, out hit, maxRayDistanceMeters, acousticLayerMask, QueryTriggerInteraction.Ignore))
						{
							AddDebugRaySegment(rayOrigin, hit.point, intensity, bounce);

							AcousticMaterial material = GetMaterial(hit.collider);
							float scatter = material != null ? material.GetScattering(4) : 0.25f;
							float reflect = material != null ? material.GetReflectionAmplitude(4) : 0.65f;
							Vector3 reflected = Vector3.Reflect(rayDir, hit.normal).normalized;
							Vector3 diffuse = AcousticsMath.SampleHemisphere(hit.normal, ref seed);
							rayDir = Vector3.Slerp(reflected, diffuse, scatter).normalized;
							rayOrigin = hit.point + (rayDir * surfaceOffset);
							intensity *= Mathf.Clamp(reflect, 0.1f, 1f);

							if (intensity < 0.03f)
							{
								break;
							}
						}
						else
						{
							Vector3 end = rayOrigin + (rayDir * Mathf.Min(maxRayDistanceMeters, 12f));
							AddDebugRaySegment(rayOrigin, end, intensity, bounce);
							break;
						}
					}
				}
			}
		}

		private void AddDebugRaySegment(Vector3 from, Vector3 to, float intensity, int bounce)
		{
			DebugRaySegment segment = new DebugRaySegment();
			segment.from = from;
			segment.to = to;
			segment.intensity = Mathf.Clamp01(intensity);
			segment.bounce = bounce;
			debugRaySegments.Add(segment);
		}

		private void ClearDebugPropagationRays()
		{
			debugRaySegments.Clear();
		}

		private void LogSimulationResults()
		{
			StringBuilder sb = new StringBuilder(16384);
			sb.AppendLine("========== ACOUSTIC SIMULATION REPORT ==========");
			sb.AppendLine("Scene Object: " + name);
			sb.AppendLine("Room Bounds Center: " + FormatVector3(roomBounds.center));
			sb.AppendLine("Room Bounds Size: " + FormatVector3(roomBounds.size));
			sb.AppendLine("Temperature: " + airTemperatureC.ToString("F1") + " C");
			sb.AppendLine("Humidity: " + humidityPercent.ToString("F1") + " %");
			sb.AppendLine("Speakers: " + CountValid(speakers));
			sb.AppendLine("Microphones: " + CountValid(microphones));
			sb.AppendLine("Materials: " + CountValid(materials));
			sb.AppendLine("Structural Materials: " + structuralMaterials.Count);
			sb.AppendLine("Listeners: " + CountValid(listenerProbes));
			sb.AppendLine("Hotspots: " + hotspots.Count);
			sb.AppendLine("Late Solver: " + ((lateFieldOperatorSolver != null && lateFieldOperatorSolver.HasField) ? "ENABLED" : "DISABLED"));
			sb.AppendLine("Worst Actual Mic Feedback Margin: " + GetWorstActualFeedbackMarginDb().ToString("F2") + " dB");
			sb.AppendLine();

			AppendMixerSummary(sb);
			AppendSpeakerSummary(sb);
			AppendMicrophoneResults(sb);
			AppendListenerResults(sb);
			AppendHotspotSummary(sb);

			Debug.Log(sb.ToString(), this);
		}

		private void AppendMixerSummary(StringBuilder sb)
		{
			sb.AppendLine("---- MIXER / ROUTING ----");
			if (mixer == null)
			{
				sb.AppendLine("Mixer: none");
				sb.AppendLine();
				return;
			}

			sb.AppendLine("Mixer Model: " + mixer.MixerModel);
			List<MixerInputChannel> channels = mixer.Channels;
			List<MixerBusOutput> outputs = mixer.Outputs;
			int enabledChannels = 0;
			int enabledOutputs = 0;

			if (channels != null)
			{
				for (int i = 0; i < channels.Count; i++)
				{
					MixerInputChannel channel = channels[i];
					if (channel != null && channel.microphoneInput != null && channel.enabled && !channel.muted)
					{
						enabledChannels++;
					}
				}
			}

			if (outputs != null)
			{
				for (int i = 0; i < outputs.Count; i++)
				{
					MixerBusOutput output = outputs[i];
					if (output != null && output.targetSpeaker != null && output.enabled && !output.muted)
					{
						enabledOutputs++;
					}
				}
			}

			sb.AppendLine("Active Input Channels: " + enabledChannels);
			sb.AppendLine("Active Outputs: " + enabledOutputs);

			if (channels != null && channels.Count > 0)
			{
				sb.AppendLine("Input Channels:");
				for (int i = 0; i < channels.Count; i++)
				{
					MixerInputChannel channel = channels[i];
					if (channel == null || channel.microphoneInput == null)
					{
						continue;
					}

					sb.Append(" [");
					sb.Append(i + 1);
					sb.Append("] ");
					sb.Append(channel.channelName);
					sb.Append(" -> ");
					sb.Append(channel.microphoneInput.name);
					sb.Append(" | enabled=");
					sb.Append(channel.enabled);
					sb.Append(" muted=");
					sb.Append(channel.muted);
					sb.Append(" phantom=");
					sb.Append(channel.phantomPower);
					sb.Append(" preamp=");
					sb.Append(channel.preampGainDb.ToString("F1"));
					sb.Append("dB");
					sb.Append(" fader=");
					sb.Append(channel.faderDb.ToString("F1"));
					sb.Append("dB");
					sb.Append(" HPF=");
					sb.Append(channel.hpfHz.ToString("F0"));
					sb.Append("Hz");
					sb.Append(" LPF=");
					sb.Append(channel.lpfHz.ToString("F0"));
					sb.Append("Hz");
					sb.Append(" bus=");
					sb.Append(channel.assignedBus);

					if (channel.inputWire != null)
					{
						sb.Append(" wire=");
						sb.Append(channel.inputWire.WireName);
						sb.Append(" ");
						sb.Append(channel.inputWire.LengthMeters.ToString("F1"));
						sb.Append("m");
					}

					sb.AppendLine();
				}
			}

			if (outputs != null && outputs.Count > 0)
			{
				sb.AppendLine("Outputs:");
				for (int i = 0; i < outputs.Count; i++)
				{
					MixerBusOutput output = outputs[i];
					if (output == null || output.targetSpeaker == null)
					{
						continue;
					}

					sb.Append(" [");
					sb.Append(i + 1);
					sb.Append("] ");
					sb.Append(output.busName);
					sb.Append(" -> ");
					sb.Append(output.targetSpeaker.name);
					sb.Append(" | enabled=");
					sb.Append(output.enabled);
					sb.Append(" muted=");
					sb.Append(output.muted);
					sb.Append(" level=");
					sb.Append(output.levelDb.ToString("F1"));
					sb.Append("dB");
					sb.Append(" trim=");
					sb.Append(output.outputTrimDb.ToString("F1"));
					sb.Append("dB");
					sb.Append(" polarityInvert=");
					sb.Append(output.polarityInvert);

					if (output.outputWire != null)
					{
						sb.Append(" wire=");
						sb.Append(output.outputWire.WireName);
						sb.Append(" ");
						sb.Append(output.outputWire.LengthMeters.ToString("F1"));
						sb.Append("m");
					}

					sb.AppendLine();
				}
			}

			sb.AppendLine();
		}

		private void AppendSpeakerSummary(StringBuilder sb)
		{
			sb.AppendLine("---- SPEAKERS ----");
			if (speakers == null || speakers.Count == 0)
			{
				sb.AppendLine("No speakers found.");
				sb.AppendLine();
				return;
			}

			for (int i = 0; i < speakers.Count; i++)
			{
				AcousticSpeaker speaker = speakers[i];
				if (speaker == null)
				{
					continue;
				}

				sb.Append(" ");
				sb.Append(speaker.name);
				sb.Append(" | role=");
				sb.Append(speaker.Role);
				sb.Append(" emit=");
				sb.Append(speaker.EmitToRoom);
				sb.Append(" pos=");
				sb.Append(FormatVector3(speaker.transform.position));
				sb.Append(" forward=");
				sb.Append(FormatVector3(speaker.GetEmissionForwardWorld()));
				sb.Append(" crossover=");
				sb.Append(speaker.GetEffectiveSimulationCrossoverHz().ToString("F0"));
				sb.Append("Hz");
				sb.Append(" hCov=");
				sb.Append(speaker.GetEffectiveSimulationHorizontalCoverageLeftDeg().ToString("F0"));
				sb.Append("/");
				sb.Append(speaker.GetEffectiveSimulationHorizontalCoverageRightDeg().ToString("F0"));
				sb.Append(" deg");
				sb.Append(" vCov=");
				sb.Append(speaker.GetEffectiveSimulationVerticalCoverageDeg().ToString("F0"));
				sb.Append(" deg");
				sb.AppendLine();
			}

			sb.AppendLine();
		}

		private void AppendMicrophoneResults(StringBuilder sb)
		{
			sb.AppendLine("---- MICROPHONES / FEEDBACK ----");
			if (microphoneResults == null || microphoneResults.Count == 0)
			{
				sb.AppendLine("No microphone results.");
				sb.AppendLine();
				return;
			}

			List<MicFeedbackResult> sorted = new List<MicFeedbackResult>(microphoneResults);
			sorted.Sort(CompareMicRisk);

			for (int i = 0; i < sorted.Count; i++)
			{
				MicFeedbackResult result = sorted[i];
				if (result == null || result.microphone == null)
				{
					continue;
				}

				PointBreakdown breakdown = EvaluatePointBreakdown(result.microphone.transform.position);

				sb.Append(" ");
				sb.Append(i + 1);
				sb.Append(". ");
				sb.Append(result.microphone.name);
				sb.Append(" | SPL=");
				sb.Append(result.totalSplDb.ToString("F2"));
				sb.Append(" dB");
				sb.Append(" | feedbackMargin=");
				sb.Append(result.feedbackMarginDb.ToString("F2"));
				sb.Append(" dB");
				sb.Append(" | worstBand=");
				sb.Append(result.worstBandFrequencyHz.ToString("F0"));
				sb.Append(" Hz");
				sb.Append(" | structure=");
				sb.Append(result.structureVibrationDb.ToString("F2"));
				sb.Append(" dB");
				sb.Append(" | worstSpeaker=");
				sb.Append(result.worstSpeaker != null ? result.worstSpeaker.name : "none");
				sb.Append(" | pos=");
				sb.Append(FormatVector3(result.microphone.transform.position));
				sb.AppendLine();

				sb.AppendLine(
					" Contributions: direct=" + breakdown.directTotalDb.ToString("F2") + " dB | reflections=" + breakdown.reflectionTotalDb.ToString("F2") + " dB | lateField=" + breakdown.lateTotalDb.ToString("F2") + " dB | structure=" + breakdown.structureTotalDb.ToString("F2") + " dB | total=" + breakdown.totalDb.ToString("F2") + " dB"
				);

				if (logPerBandResults)
				{
					sb.AppendLine(" TOTAL by band: " + FormatBandArray(breakdown.totalBandDb, " dB"));
					sb.AppendLine(" DIRECT by band: " + FormatBandArray(breakdown.directBandDb, " dB"));
					sb.AppendLine(" REFLECTION by band: " + FormatBandArray(breakdown.reflectionBandDb, " dB"));
					sb.AppendLine(" LATE FIELD by band: " + FormatBandArray(breakdown.lateBandDb, " dB"));
					sb.AppendLine(" STRUCTURE by band: " + FormatBandArray(breakdown.structureBandDb, " dB"));
					sb.AppendLine(" LOOP GAIN by band: " + FormatBandArray(result.loopGainDb, " dB"));
				}
			}

			sb.AppendLine();
		}

		private void AppendListenerResults(StringBuilder sb)
		{
			sb.AppendLine("---- LISTENER PROBES ----");
			if (listenerProbes == null || listenerProbes.Count == 0)
			{
				sb.AppendLine("No listener probes.");
				sb.AppendLine();
				return;
			}

			for (int i = 0; i < listenerProbes.Count; i++)
			{
				AcousticListenerProbe probe = listenerProbes[i];
				if (probe == null)
				{
					continue;
				}

				ListenerResult result = probe.LastResult;
				if (result == null)
				{
					continue;
				}

				PointBreakdown breakdown = EvaluatePointBreakdown(probe.transform.position);

				sb.Append(" ");
				sb.Append(probe.name);
				sb.Append(" | SPL=");
				sb.Append(result.totalSplDb.ToString("F2"));
				sb.Append(" dB");
				sb.Append(" | peak=");
				sb.Append(result.peakFrequencyHz.ToString("F0"));
				sb.Append(" Hz");
				sb.Append(" | structure=");
				sb.Append(result.structureVibrationDb.ToString("F2"));
				sb.Append(" dB");
				sb.Append(" | pos=");
				sb.Append(FormatVector3(probe.transform.position));
				sb.AppendLine();

				sb.AppendLine(
					" Contributions: direct=" + breakdown.directTotalDb.ToString("F2") + " dB | reflections=" + breakdown.reflectionTotalDb.ToString("F2") + " dB | lateField=" + breakdown.lateTotalDb.ToString("F2") + " dB | structure=" + breakdown.structureTotalDb.ToString("F2") + " dB | total=" + breakdown.totalDb.ToString("F2") + " dB"
				);

				if (logPerBandResults)
				{
					sb.AppendLine(" TOTAL by band: " + FormatBandArray(breakdown.totalBandDb, " dB"));
					sb.AppendLine(" DIRECT by band: " + FormatBandArray(breakdown.directBandDb, " dB"));
					sb.AppendLine(" REFLECTION by band: " + FormatBandArray(breakdown.reflectionBandDb, " dB"));
					sb.AppendLine(" LATE FIELD by band: " + FormatBandArray(breakdown.lateBandDb, " dB"));
					sb.AppendLine(" STRUCTURE by band: " + FormatBandArray(breakdown.structureBandDb, " dB"));
				}
			}

			sb.AppendLine();
		}

		private void AppendHotspotSummary(StringBuilder sb)
		{
			sb.AppendLine("---- HOTSPOTS ----");
			if (hotspots == null || hotspots.Count == 0)
			{
				sb.AppendLine("No hotspots above current threshold.");
				sb.AppendLine();
				return;
			}

			List<HotspotResult> sorted = new List<HotspotResult>(hotspots);
			sorted.Sort(CompareHotspotRisk);

			float maxRisk = 0f;
			float avgRisk = 0f;
			float maxSpl = float.NegativeInfinity;
			float avgSpl = 0f;

			for (int i = 0; i < sorted.Count; i++)
			{
				HotspotResult h = sorted[i];
				if (h == null)
				{
					continue;
				}

				maxRisk = Mathf.Max(maxRisk, h.risk);
				maxSpl = Mathf.Max(maxSpl, h.totalSplDb);
				avgRisk += h.risk;
				avgSpl += h.totalSplDb;
			}

			avgRisk /= Mathf.Max(1, sorted.Count);
			avgSpl /= Mathf.Max(1, sorted.Count);

			sb.AppendLine("Total Hotspots: " + sorted.Count);
			sb.AppendLine("Max Risk: " + maxRisk.ToString("F3"));
			sb.AppendLine("Average Risk: " + avgRisk.ToString("F3"));
			sb.AppendLine("Max SPL: " + maxSpl.ToString("F2") + " dB");
			sb.AppendLine("Average SPL: " + avgSpl.ToString("F2") + " dB");
			sb.AppendLine("Top Hotspots:");

			int limit = Mathf.Min(maxLoggedHotspots, sorted.Count);
			for (int i = 0; i < limit; i++)
			{
				HotspotResult spot = sorted[i];
				if (spot == null)
				{
					continue;
				}

				PointBreakdown breakdown = EvaluatePointBreakdown(spot.position);

				sb.Append(" ");
				sb.Append(i + 1);
				sb.Append(". pos=");
				sb.Append(FormatVector3(spot.position));
				sb.Append(" | risk=");
				sb.Append(spot.risk.ToString("F3"));
				sb.Append(" | SPL=");
				sb.Append(spot.totalSplDb.ToString("F2"));
				sb.Append(" dB");
				sb.Append(" | peak=");
				sb.Append(spot.peakFrequencyHz.ToString("F0"));
				sb.Append(" Hz");
				sb.Append(" | structure=");
				sb.Append(spot.structureVibrationDb.ToString("F2"));
				sb.Append(" dB");
				sb.AppendLine();

				sb.AppendLine(
					" Contributions: direct=" + breakdown.directTotalDb.ToString("F2") + " dB | reflections=" + breakdown.reflectionTotalDb.ToString("F2") + " dB | lateField=" + breakdown.lateTotalDb.ToString("F2") + " dB | structure=" + breakdown.structureTotalDb.ToString("F2") + " dB | total=" + breakdown.totalDb.ToString("F2") + " dB"
				);

				if (logPerBandResults)
				{
					sb.AppendLine(" TOTAL by band: " + FormatBandArray(breakdown.totalBandDb, " dB"));
					sb.AppendLine(" DIRECT by band: " + FormatBandArray(breakdown.directBandDb, " dB"));
					sb.AppendLine(" REFLECTION by band: " + FormatBandArray(breakdown.reflectionBandDb, " dB"));
					sb.AppendLine(" LATE FIELD by band: " + FormatBandArray(breakdown.lateBandDb, " dB"));
					sb.AppendLine(" STRUCTURE by band: " + FormatBandArray(breakdown.structureBandDb, " dB"));
				}
			}

			sb.AppendLine();
		}

		private static int CompareMicRisk(MicFeedbackResult a, MicFeedbackResult b)
		{
			if (ReferenceEquals(a, b))
			{
				return 0;
			}

			if (a == null)
			{
				return 1;
			}

			if (b == null)
			{
				return -1;
			}

			int byMargin = a.feedbackMarginDb.CompareTo(b.feedbackMarginDb);
			if (byMargin != 0)
			{
				return byMargin;
			}

			return b.totalSplDb.CompareTo(a.totalSplDb);
		}

		private static int CompareHotspotRisk(HotspotResult a, HotspotResult b)
		{
			if (ReferenceEquals(a, b))
			{
				return 0;
			}

			if (a == null)
			{
				return 1;
			}

			if (b == null)
			{
				return -1;
			}

			int byRisk = b.risk.CompareTo(a.risk);
			if (byRisk != 0)
			{
				return byRisk;
			}

			return b.totalSplDb.CompareTo(a.totalSplDb);
		}

		private static int CountValid<T>(List<T> list) where T : class
		{
			if (list == null)
			{
				return 0;
			}

			int count = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] != null)
				{
					count++;
				}
			}

			return count;
		}

		private static string FormatVector3(Vector3 value)
		{
			return "(" + value.x.ToString("F2") + ", " + value.y.ToString("F2") + ", " + value.z.ToString("F2") + ")";
		}

		private static string FormatBandArray(float[] values, string unit)
		{
			if (values == null)
			{
				return "n/a";
			}

			StringBuilder sb = new StringBuilder(256);
			for (int i = 0; i < Mathf.Min(values.Length, AcousticBands.Count); i++)
			{
				if (i > 0)
				{
					sb.Append(" | ");
				}

				sb.Append(AcousticBands.CenterFrequenciesHz[i].ToString("F0"));
				sb.Append("Hz=");
				sb.Append(values[i].ToString("F2"));

				if (!string.IsNullOrEmpty(unit))
				{
					sb.Append(unit);
				}
			}

			return sb.ToString();
		}

		private static AcousticMaterial GetMaterial(Collider collider)
		{
			if (collider == null)
			{
				return null;
			}

			AcousticMaterial material = collider.GetComponent<AcousticMaterial>();
			if (material != null)
			{
				return material;
			}

			return collider.GetComponentInParent<AcousticMaterial>();
		}

		private static Collider GetPrimaryCollider(Component component)
		{
			return component != null ? component.GetComponent<Collider>() : null;
		}

		private void OnDrawGizmosSelected()
		{
			if (roomBounds.size.sqrMagnitude > 0f)
			{
				Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
				Gizmos.DrawWireCube(roomBounds.center, roomBounds.size);
			}

			if (drawPropagationRays && debugRaySegments != null)
			{
				for (int i = 0; i < debugRaySegments.Count; i++)
				{
					DebugRaySegment segment = debugRaySegments[i];
					float bounceFade = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(segment.bounce / Mathf.Max(1f, gizmoRayBounceDepth + 1f)));
					float alpha = Mathf.Clamp01(segment.intensity * bounceFade);
					Gizmos.color = new Color(0.2f, 0.85f, 1f, alpha);
					Gizmos.DrawLine(segment.from, segment.to);

					Vector3 dir = segment.to - segment.from;
					if (dir.sqrMagnitude > 0.0001f)
					{
						Vector3 d = dir.normalized;
						Vector3 up = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
						Vector3 side = Vector3.Cross(d, up).normalized;
						float headLen = Mathf.Min(0.18f, dir.magnitude * 0.25f);
						Vector3 headBase = segment.to - (d * headLen);
						Gizmos.DrawLine(segment.to, headBase + (side * headLen * 0.35f));
						Gizmos.DrawLine(segment.to, headBase - (side * headLen * 0.35f));
					}
				}
			}

			if (drawHotspots && hotspots != null)
			{
				for (int i = 0; i < hotspots.Count; i++)
				{
					HotspotResult spot = hotspots[i];
					if (spot == null)
					{
						continue;
					}

					Color c = Color.Lerp(new Color(0f, 1f, 0.15f, 0.7f), new Color(1f, 0.1f, 0f, 0.85f), spot.risk);
					Gizmos.color = c;
					float scale = Mathf.Lerp(0.8f, 2.4f, spot.risk);
					Gizmos.DrawSphere(spot.position, gizmoSphereRadius * scale);
				}
			}

			if (drawSpeakerMicLinks && microphoneResults != null)
			{
				for (int i = 0; i < microphoneResults.Count; i++)
				{
					MicFeedbackResult result = microphoneResults[i];
					if (result == null || result.microphone == null || result.worstSpeaker == null)
					{
						continue;
					}

					float risk = Mathf.Clamp01(Mathf.InverseLerp(18f, -3f, result.feedbackMarginDb));
					Gizmos.color = Color.Lerp(Color.green, Color.red, risk);
					Gizmos.DrawLine(result.microphone.transform.position, result.worstSpeaker.transform.position);
					Gizmos.DrawSphere(result.microphone.transform.position, gizmoSphereRadius * 1.2f);
				}
			}
		}
	}
}