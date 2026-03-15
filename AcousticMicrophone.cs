using UnityEngine;

namespace PhysicalAcousticsSim
{
	[DisallowMultipleComponent]
	public sealed class AcousticMicrophone : MonoBehaviour
	{
		[SerializeField] private string microphoneType = "Suspended condenser cardioid microphone";
		[SerializeField] private string elementType = "Condenser";
		[SerializeField] private PolarPattern polarPattern = PolarPattern.Cardioid;
		[SerializeField] private MicrophoneAxis pickupAxis = MicrophoneAxis.Down;

		[Header("Specification")]
		[SerializeField] private Vector2 frequencyRangeHz = new Vector2(70f, 16000f);
		[SerializeField] private float sensitivityMilliVoltsPerPa = 14.1f;
		[SerializeField] private float outputImpedanceOhm = 100f;
		[SerializeField] private float maxInputSplDb = 134f;
		[SerializeField] private float signalToNoiseDb = 66f;
		[SerializeField] private float dynamicRangeDb = 106f;
		[SerializeField] private Vector2 phantomVoltageRange = new Vector2(9f, 52f);

		[Header("Simulation Tuning")]
		[SerializeField] private bool requiresPhantomPower = true;
		[SerializeField] private float additionalGainDb = 0f;
		[SerializeField] private float[] bandTrimDb = new float[AcousticBands.Count];

		public string MicrophoneType => microphoneType;
		public string ElementType => elementType;
		public PolarPattern Pattern => polarPattern;
		public float OutputImpedanceOhm => outputImpedanceOhm;
		public bool RequiresPhantomPower => requiresPhantomPower;
		public float SensitivityVoltsPerPascal => Mathf.Max(0.0000001f, sensitivityMilliVoltsPerPa * 0.001f);

		private void Reset()
		{
			AcousticBands.EnsureArray(ref bandTrimDb, 0f);
		}

		private void OnValidate()
		{
			AcousticBands.EnsureArray(ref bandTrimDb, 0f);

			frequencyRangeHz.x = Mathf.Max(15f, frequencyRangeHz.x);
			frequencyRangeHz.y = Mathf.Max(frequencyRangeHz.x + 10f, frequencyRangeHz.y);
			sensitivityMilliVoltsPerPa = Mathf.Max(0.001f, sensitivityMilliVoltsPerPa);
			outputImpedanceOhm = Mathf.Max(1f, outputImpedanceOhm);
			maxInputSplDb = Mathf.Max(20f, maxInputSplDb);
			signalToNoiseDb = Mathf.Max(0f, signalToNoiseDb);
			dynamicRangeDb = Mathf.Max(1f, dynamicRangeDb);
			phantomVoltageRange.x = Mathf.Max(0f, phantomVoltageRange.x);
			phantomVoltageRange.y = Mathf.Max(phantomVoltageRange.x, phantomVoltageRange.y);
		}

		public Vector3 GetPickupAxisWorld()
		{
			switch (pickupAxis)
			{
				case MicrophoneAxis.Up:
					return transform.up;
				case MicrophoneAxis.Down:
					return -transform.up;
				default:
					return transform.forward;
			}
		}

		public float GetPolarAmplitudeTo(Vector3 sourcePosition)
		{
			Vector3 axis = GetPickupAxisWorld().normalized;
			Vector3 toSource = (sourcePosition - transform.position).normalized;
			float c = Mathf.Clamp(Vector3.Dot(axis, toSource), -1f, 1f);

			float response;
			switch (polarPattern)
			{
				case PolarPattern.Omni:
					response = 1f;
					break;
				case PolarPattern.Supercardioid:
					response = Mathf.Clamp01(0.37f + 0.63f * c);
					break;
				case PolarPattern.Hypercardioid:
					response = Mathf.Clamp01(0.25f + 0.75f * c);
					break;
				case PolarPattern.Figure8:
					response = Mathf.Abs(c);
					break;
				case PolarPattern.Shotgun:
					response = Mathf.Pow(Mathf.Clamp01(0.5f + 0.5f * c), 3.5f);
					break;
				default:
					response = Mathf.Clamp01(0.5f + 0.5f * c);
					break;
			}

			return Mathf.Max(0.02f, response);
		}

		public float GetFrequencyResponseAmplitude(int band)
		{
			float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			float amp = AcousticBands.BandPassWeight(frequencyRangeHz.x, frequencyRangeHz.y, f);
			amp *= AcousticBands.DbToAmplitude(additionalGainDb + bandTrimDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)]);
			return Mathf.Max(0.0001f, amp);
		}

		public float GetEquivalentSelfNoiseDb()
		{
			float fromSnr = 94f - signalToNoiseDb;
			float fromRange = maxInputSplDb - dynamicRangeDb;
			return Mathf.Max(fromSnr, fromRange);
		}

		public float GetOpenCircuitOutputDbu(float pressurePa)
		{
			float voltage = pressurePa * SensitivityVoltsPerPascal;
			float voltsRms = Mathf.Max(1e-8f, voltage);
			return 20f * Mathf.Log10(voltsRms / 0.775f);
		}
	}
}
