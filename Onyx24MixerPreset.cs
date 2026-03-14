using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAcousticsSim
{
	[Serializable]
	public sealed class MixerInputChannelPresetData
	{
		public string channelName = "CH";
		public bool enabled = true;
		public bool phantomPower = true;
		public bool hiZ = false;
		public bool muted = false;
		public bool solo = false;
		public bool polarityInvert = false;
		public float preampGainDb = 0f;
		public float trimDb = 0f;
		public bool lowCutEnabled = true;
		public float hpfHz = 100f;
		public float lpfHz = 18000f;
		public bool eqEnabled = true;
		public float pan = 0f;
		public bool lr = true;
		public float faderDb = 0f;
		public string assignedBus = "MAIN";
		public float eqHighGainDb = 0f;
		public float eqMidGainDb = 0f;
		public float eqMidFrequencyHz = 2500f;
		public float eqLowGainDb = 0f;
	}

	[Serializable]
	public sealed class MixerBusOutputPresetData
	{
		public string busName = "MAIN";
		public bool enabled = true;
		public bool muted = false;
		public bool polarityInvert = false;
		public float levelDb = 0f;
		public float outputTrimDb = 0f;
	}

	[CreateAssetMenu(fileName = "Onyx24MixerPreset", menuName = "Physical Acoustics Sim/Onyx24 Mixer Preset")]
	public sealed class Onyx24MixerPreset : ScriptableObject
	{
		[SerializeField] private string presetName = "New Preset";
		[SerializeField] private string mixerModel = "Mackie ONYX24";
		[SerializeField] private float masterFaderDb = 0f;
		[SerializeField] private float[] masterEqBandGainDb = new float[AcousticBands.Count];
		[SerializeField] private List<MixerInputChannelPresetData> channels = new List<MixerInputChannelPresetData>();
		[SerializeField] private List<MixerBusOutputPresetData> outputs = new List<MixerBusOutputPresetData>();

		public string PresetName
		{
			get => presetName;
			set => presetName = string.IsNullOrWhiteSpace(value) ? "New Preset" : value.Trim();
		}

		public string MixerModel
		{
			get => mixerModel;
			set => mixerModel = value ?? string.Empty;
		}

		public float MasterFaderDb
		{
			get => masterFaderDb;
			set => masterFaderDb = value;
		}

		public float[] MasterEqBandGainDb => masterEqBandGainDb;
		public List<MixerInputChannelPresetData> Channels => channels;
		public List<MixerBusOutputPresetData> Outputs => outputs;

		private void OnValidate()
		{
			EnsureData();
		}

		public void EnsureData()
		{
			PresetName = presetName;
			if (mixerModel == null)
			{
				mixerModel = string.Empty;
			}

			AcousticBands.EnsureArray(ref masterEqBandGainDb, 0f);

			if (channels == null)
			{
				channels = new List<MixerInputChannelPresetData>();
			}

			if (outputs == null)
			{
				outputs = new List<MixerBusOutputPresetData>();
			}
		}
	}
}