using UnityEngine;

namespace PhysicalAcousticsSim
{
	[DisallowMultipleComponent]
	public sealed class AcousticSignalWire : MonoBehaviour
	{
		[SerializeField] private string wireName = "Signal Wire";
		[SerializeField] private WireSignalType signalType = WireSignalType.MicrophoneBalanced;
		[SerializeField] private string commutation = "XLR";
		[SerializeField] private float lengthMeters = 5f;
		[SerializeField] private float velocityFactor = 0.66f;
		[SerializeField] private float conductorResistancePerMeterOhm = 0.08f;
		[SerializeField] private float capacitancePerMeterPf = 70f;
		[SerializeField] private float shieldingQualityDb = 80f;
		[SerializeField] private bool balanced = true;
		[SerializeField] private bool phaseInvert = false;

		public string WireName => wireName;
		public WireSignalType SignalType => signalType;
		public float LengthMeters => lengthMeters;
		public bool PhaseInvert => phaseInvert;

		private void OnValidate()
		{
			lengthMeters = Mathf.Max(0.01f, lengthMeters);
			velocityFactor = Mathf.Clamp(velocityFactor, 0.1f, 0.99f);
			conductorResistancePerMeterOhm = Mathf.Max(0.0001f, conductorResistancePerMeterOhm);
			capacitancePerMeterPf = Mathf.Max(0.1f, capacitancePerMeterPf);
			shieldingQualityDb = Mathf.Max(0f, shieldingQualityDb);
		}

		public float GetDelaySeconds()
		{
			return lengthMeters / (299792458f * velocityFactor);
		}

		public float GetPhaseRadians()
		{
			return phaseInvert ? Mathf.PI : 0f;
		}

		public float GetLossDb(int band, float sourceImpedanceOhm, float loadImpedanceOhm)
		{
			float amplitude = GetAmplitudeLoss(band, sourceImpedanceOhm, loadImpedanceOhm);
			return AcousticBands.AmplitudeToDb(amplitude);
		}

		public float GetAmplitudeLoss(int band, float sourceImpedanceOhm, float loadImpedanceOhm)
		{
			sourceImpedanceOhm = Mathf.Max(0.1f, sourceImpedanceOhm);
			loadImpedanceOhm = Mathf.Max(0.1f, loadImpedanceOhm);

			float seriesR = conductorResistancePerMeterOhm * lengthMeters * 2f;
			float divider = loadImpedanceOhm / (loadImpedanceOhm + sourceImpedanceOhm + seriesR);

			float cFarads = capacitancePerMeterPf * 1e-12f * lengthMeters;
			float fc = cFarads > 0f ? 1f / (2f * Mathf.PI * (sourceImpedanceOhm + seriesR) * cFarads) : 200000f;
			float freqHz = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			float hfWeight = AcousticBands.LPFWeight(fc, freqHz);

			float balanceBonus = balanced ? 1f : 0.985f;
			return Mathf.Clamp(divider * hfWeight * balanceBonus, 0.0001f, 1f);
		}

		public float GetInjectedNoisePenaltyDb()
		{
			float basePenalty = Mathf.Lerp(-90f, -55f, Mathf.InverseLerp(100f, 0f, shieldingQualityDb));
			if (!balanced)
			{
				basePenalty += 8f;
			}

			return basePenalty;
		}
	}
}