using System;
using UnityEngine;

namespace PhysicalAcousticsSim
{
	public enum SpeakerRole
	{
		FullRange,
		Satellite,
		Subwoofer,
		Monitor
	}

	public enum PolarPattern
	{
		Omni,
		Cardioid,
		Supercardioid,
		Hypercardioid,
		Figure8,
		Shotgun
	}

	public enum MicrophoneAxis
	{
		Forward,
		Up,
		Down
	}

	public enum MaterialPreset
	{
		Custom,
		Concrete,
		Wood,
		Metal,
		Glass,
		Fabric,
		Rubber,
		Foam,
		Carpet,
		Plaster,
		Curtain,
		Latex,
		Mylar,
		PvcFabric,
		ReinforcedPvc,
		OxfordPu
	}

	public enum WireSignalType
	{
		MicrophoneBalanced,
		LineBalanced,
		SpeakerLevel,
		USBDigital
	}

	public static class AcousticBands
	{
		public const int Count = 8;
		public static readonly float[] CenterFrequenciesHz = { 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f };
		public const float ReferencePressurePa = 0.00002f;

		public static void EnsureArray(ref float[] array, float defaultValue)
		{
			if (array != null && array.Length == Count)
			{
				return;
			}

			float[] old = array;
			array = new float[Count];
			for (int i = 0; i < Count; i++)
			{
				array[i] = defaultValue;
			}

			if (old == null)
			{
				return;
			}

			for (int i = 0; i < Mathf.Min(old.Length, Count); i++)
			{
				array[i] = old[i];
			}
		}

		public static void SetAll(float[] array, float value)
		{
			if (array == null)
			{
				return;
			}

			for (int i = 0; i < array.Length; i++)
			{
				array[i] = value;
			}
		}

		public static void Copy(float[] source, float[] destination)
		{
			if (source == null || destination == null)
			{
				return;
			}

			for (int i = 0; i < Mathf.Min(source.Length, destination.Length); i++)
			{
				destination[i] = source[i];
			}
		}

		public static float SpeedOfSound(float temperatureCelsius)
		{
			return 331.3f + 0.606f * temperatureCelsius;
		}

		public static float DbToAmplitude(float db)
		{
			return Mathf.Pow(10f, db / 20f);
		}

		public static float AmplitudeToDb(float amplitude)
		{
			return 20f * Mathf.Log10(Mathf.Max(1e-8f, amplitude));
		}

		public static float DbToPressure(float splDb)
		{
			return ReferencePressurePa * Mathf.Pow(10f, splDb / 20f);
		}

		public static float PressureToDb(float pressurePa)
		{
			return 20f * Mathf.Log10(Mathf.Max(pressurePa, 1e-9f) / ReferencePressurePa);
		}

		public static float BandPassWeight(float lowHz, float highHz, float centerHz)
		{
			lowHz = Mathf.Max(1f, lowHz);
			highHz = Mathf.Max(lowHz + 1f, highHz);

			float low = HPFWeight(lowHz, centerHz);
			float high = LPFWeight(highHz, centerHz);
			return Mathf.Clamp01(low * high);
		}

		public static float HPFWeight(float cutoffHz, float centerHz)
		{
			cutoffHz = Mathf.Max(1f, cutoffHz);
			centerHz = Mathf.Max(1f, centerHz);
			float ratio = cutoffHz / centerHz;
			return 1f / Mathf.Sqrt(1f + Mathf.Pow(ratio, 4f));
		}

		public static float LPFWeight(float cutoffHz, float centerHz)
		{
			cutoffHz = Mathf.Max(1f, cutoffHz);
			centerHz = Mathf.Max(1f, centerHz);
			float ratio = centerHz / cutoffHz;
			return 1f / Mathf.Sqrt(1f + Mathf.Pow(ratio, 4f));
		}

		public static float AirAbsorptionDbPerMeter(float centerHz, float temperatureCelsius, float humidityPercent)
		{
			float f = Mathf.Max(0.02f, centerHz / 1000f);
			float humidityNorm = Mathf.Clamp01(humidityPercent / 100f);
			float temperatureNorm = Mathf.InverseLerp(-10f, 40f, temperatureCelsius);

			float baseLoss = 0.00015f * Mathf.Pow(f, 1.7f);
			float humidityFactor = Mathf.Lerp(1.22f, 0.78f, humidityNorm);
			float temperatureFactor = Mathf.Lerp(1.12f, 0.90f, temperatureNorm);

			return baseLoss * humidityFactor * temperatureFactor;
		}

		public static float SumPressureBandsToDb(float[] bandPressuresPa)
		{
			float sumSq = 0f;
			if (bandPressuresPa != null)
			{
				for (int i = 0; i < bandPressuresPa.Length; i++)
				{
					sumSq += bandPressuresPa[i] * bandPressuresPa[i];
				}
			}

			return PressureToDb(Mathf.Sqrt(sumSq));
		}
	}

	public static class AcousticsMath
	{
		public static uint Hash(uint x)
		{
			x ^= x >> 16;
			x *= 0x7feb352dU;
			x ^= x >> 15;
			x *= 0x846ca68bU;
			x ^= x >> 16;
			return x;
		}

		public static uint Hash(Vector3 value)
		{
			unchecked
			{
				uint hx = (uint)value.x.GetHashCode();
				uint hy = (uint)value.y.GetHashCode();
				uint hz = (uint)value.z.GetHashCode();
				return Hash(hx ^ (hy * 397u) ^ (hz * 7919u));
			}
		}

		public static float Next01(ref uint state)
		{
			state = Hash(state + 0x9e3779b9U);
			return (state & 0x00ffffffU) / 16777215f;
		}

		public static Vector3 SampleHemisphere(Vector3 normal, ref uint state)
		{
			normal = normal.normalized;
			Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up).normalized;
			Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

			float u1 = Next01(ref state);
			float u2 = Next01(ref state);

			float phi = 2f * Mathf.PI * u1;
			float cosTheta = u2;
			float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));

			Vector3 local =
				tangent * (Mathf.Cos(phi) * sinTheta) +
				bitangent * (Mathf.Sin(phi) * sinTheta) +
				normal * cosTheta;

			return local.normalized;
		}

		public static Vector3 SampleAsymmetricCone(Vector3 forward, Vector3 up, float leftCoverageDeg, float rightCoverageDeg, float verticalCoverageDeg, ref uint state)
		{
			forward = forward.normalized;
			up = up.normalized;
			Vector3 right = Vector3.Cross(up, forward).normalized;

			float yaw = Mathf.Lerp(-0.5f * Mathf.Max(1f, leftCoverageDeg), 0.5f * Mathf.Max(1f, rightCoverageDeg), Next01(ref state));
			float pitch = Mathf.Lerp(-0.5f * Mathf.Max(1f, verticalCoverageDeg), 0.5f * Mathf.Max(1f, verticalCoverageDeg), Next01(ref state));

			Quaternion qYaw = Quaternion.AngleAxis(yaw, up);
			Quaternion qPitch = Quaternion.AngleAxis(pitch, right);
			return (qYaw * qPitch * forward).normalized;
		}

		public static float DistanceToBounds(Bounds bounds, Vector3 point)
		{
			Vector3 closest = bounds.ClosestPoint(point);
			return Vector3.Distance(closest, point);
		}

		public static Bounds Expand(Bounds bounds, float amount)
		{
			bounds.Expand(amount * 2f);
			return bounds;
		}

		public static bool BoundsTouchOrIntersect(Bounds a, Bounds b, float gap)
		{
			return Expand(a, gap).Intersects(Expand(b, gap));
		}
	}

	[Serializable]
	public sealed class MicFeedbackResult
	{
		public AcousticMicrophone microphone;
		public AcousticSpeaker worstSpeaker;
		public float totalSplDb;
		public float feedbackMarginDb;
		public float worstBandFrequencyHz;
		public float structureVibrationDb;
		public Vector3 dominantDirection;
		public float[] bandSplDb = new float[AcousticBands.Count];
		public float[] loopGainDb = new float[AcousticBands.Count];
	}

	[Serializable]
	public sealed class ListenerResult
	{
		public float totalSplDb;
		public float structureVibrationDb;
		public float peakFrequencyHz;
		public float[] bandSplDb = new float[AcousticBands.Count];
	}

	[Serializable]
	public sealed class HotspotResult
	{
		public Vector3 position;
		public float totalSplDb;
		public float risk;
		public float peakFrequencyHz;
		public float structureVibrationDb;
		public float[] bandSplDb = new float[AcousticBands.Count];
	}
}
