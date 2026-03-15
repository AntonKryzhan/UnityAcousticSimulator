using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAcousticsSim
{
	[Serializable]
	public sealed class MixerInputChannel
	{
		public string channelName = "CH";
		public bool enabled = true;
		public AcousticMicrophone microphoneInput;
		public AcousticSignalWire inputWire;
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
		public AcousticMicrophone stereoRightInput;
		public float eqHighGainDb = 0f;
		public float eqMidGainDb = 0f;
		public float eqMidFrequencyHz = 2500f;
		public float eqLowGainDb = 0f;
		[HideInInspector] public float[] eqBandGainDb = new float[AcousticBands.Count];
	}

	[Serializable]
	public sealed class MixerBusOutput
	{
		public string busName = "MAIN";
		public bool enabled = true;
		public AcousticSpeaker targetSpeaker;
		public AcousticSignalWire outputWire;
		public bool muted = false;
		public bool polarityInvert = false;
		public float levelDb = 0f;
		public float outputTrimDb = 0f;
	}

	[DisallowMultipleComponent]
	public sealed class Onyx24Mixer : MonoBehaviour
	{
		public const float PhysicalFaderOffDb = -80f;
		public const float PhysicalFaderMinDb = -60f;
		public const float PhysicalFaderUnityDb = 0f;
		public const float PhysicalFaderMaxDb = 10f;
		public const float PhysicalPreampMinDb = -20f;
		public const float PhysicalPreampMaxDb = 40f;
		public const float ChannelEqGainMinDb = -15f;
		public const float ChannelEqGainMaxDb = 15f;
		public const float ChannelEqMidFreqMinHz = 100f;
		public const float ChannelEqMidFreqMaxHz = 8000f;

		private const float SimulatedLowShelfHz = 80f;
		private const float SimulatedHighShelfHz = 12000f;
		private const float SimulatedHighShelfReferenceHz = 6000f;
		private const float MidBellWidthOctaves = 0.85f;

		private static readonly string[] DefaultChannelLayout =
		{
			"1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14",
			"15/16", "17/18", "19/20", "21/22", "23/24"
		};

		[Header("Model")]
		[SerializeField] private string mixerModel = "Mackie ONYX24";
		[SerializeField] private string mixingType = "Analog";
		[SerializeField] private int totalChannels = 19;
		[SerializeField] private int microphoneInputs = 22;
		[SerializeField] private int stereoInputs = 5;
		[SerializeField] private int xlrInputs = 22;
		[SerializeField] private int jackInputs = 24;
		[SerializeField] private string xlrOutputs = "2 (MAIN L/R)";
		[SerializeField] private int monitorOutputs = 2;
		[SerializeField] private int jackOutputs = 7;
		[SerializeField] private bool usbInterface = true;
		[SerializeField] private bool phantomPowerAvailable = true;

		[Header("Presets")]
		[SerializeField] private Onyx24MixerPreset presetAsset;
		[SerializeField] private string presetName = "New Preset";

		[Header("Simulation")]
		[SerializeField] private float simulationReferenceInputDbu = 0f;
		[SerializeField] private float internalLatencyMs = 0.65f;
		[SerializeField] private float masterFaderDb = 0f;
		[SerializeField] private float[] masterEqBandGainDb = new float[AcousticBands.Count];
		[SerializeField] private List<MixerInputChannel> channels = new List<MixerInputChannel>();
		[SerializeField] private List<MixerBusOutput> outputs = new List<MixerBusOutput>();

		public string MixerModel => mixerModel;
		public Onyx24MixerPreset PresetAsset => presetAsset;
		public string PresetName => presetName;
		public List<MixerInputChannel> Channels => channels;
		public List<MixerBusOutput> Outputs => outputs;

		private void Reset()
		{
			EnsureDefaultLayout();
		}

		private void OnValidate()
		{
			presetName = NormalizePresetName(presetName);
			AcousticBands.EnsureArray(ref masterEqBandGainDb, 0f);
			internalLatencyMs = Mathf.Max(0f, internalLatencyMs);
			masterFaderDb = ClampPhysicalFaderDb(masterFaderDb);
			EnsureDefaultLayout();

			for (int i = 0; i < channels.Count; i++)
			{
				AcousticBands.EnsureArray(ref channels[i].eqBandGainDb, 0f);
				channels[i].hpfHz = Mathf.Max(10f, channels[i].hpfHz);
				channels[i].lpfHz = Mathf.Max(channels[i].hpfHz + 10f, channels[i].lpfHz);
				channels[i].preampGainDb = ClampPhysicalPreampGainDb(channels[i].preampGainDb);
				channels[i].faderDb = ClampPhysicalFaderDb(channels[i].faderDb);
				channels[i].pan = Mathf.Clamp(channels[i].pan, -1f, 1f);
				UpdateChannelEqBands(channels[i]);
			}

			for (int i = 0; i < outputs.Count; i++)
			{
				outputs[i].levelDb = ClampPhysicalFaderDb(outputs[i].levelDb);
			}
		}

		public static float ClampPhysicalFaderDb(float valueDb)
		{
			return Mathf.Clamp(valueDb, PhysicalFaderOffDb, PhysicalFaderMaxDb);
		}

		public static bool IsPhysicalFaderOff(float valueDb)
		{
			return ClampPhysicalFaderDb(valueDb) <= PhysicalFaderOffDb + 0.0001f;
		}

		public static float GetPhysicalFaderGainDb(float valueDb)
		{
			float clamped = ClampPhysicalFaderDb(valueDb);
			return IsPhysicalFaderOff(clamped) ? -120f : clamped;
		}

		public static float ClampPhysicalPreampGainDb(float valueDb)
		{
			return Mathf.Clamp(valueDb, PhysicalPreampMinDb, PhysicalPreampMaxDb);
		}

		public static float ClampChannelEqGainDb(float valueDb)
		{
			return Mathf.Clamp(valueDb, ChannelEqGainMinDb, ChannelEqGainMaxDb);
		}

		public static float ClampChannelEqMidFrequencyHz(float valueHz)
		{
			return Mathf.Clamp(valueHz, ChannelEqMidFreqMinHz, ChannelEqMidFreqMaxHz);
		}

		public void SaveToPreset(Onyx24MixerPreset targetPreset)
		{
			if (targetPreset == null)
			{
				return;
			}

			targetPreset.EnsureData();
			targetPreset.PresetName = NormalizePresetName(presetName);
			targetPreset.MixerModel = mixerModel;
			targetPreset.MasterFaderDb = ClampPhysicalFaderDb(masterFaderDb);
			AcousticBands.EnsureArray(ref masterEqBandGainDb, 0f);
			AcousticBands.Copy(masterEqBandGainDb, targetPreset.MasterEqBandGainDb);

			targetPreset.Channels.Clear();
			for (int i = 0; i < channels.Count; i++)
			{
				MixerInputChannel channel = channels[i];
				UpdateChannelEqBands(channel);

				targetPreset.Channels.Add(new MixerInputChannelPresetData
				{
					channelName = channel.channelName,
					enabled = channel.enabled,
					phantomPower = channel.phantomPower,
					hiZ = channel.hiZ,
					muted = channel.muted,
					solo = channel.solo,
					polarityInvert = channel.polarityInvert,
					preampGainDb = ClampPhysicalPreampGainDb(channel.preampGainDb),
					trimDb = channel.trimDb,
					lowCutEnabled = channel.lowCutEnabled,
					hpfHz = Mathf.Max(10f, channel.hpfHz),
					lpfHz = Mathf.Max(channel.hpfHz + 10f, channel.lpfHz),
					eqEnabled = channel.eqEnabled,
					pan = Mathf.Clamp(channel.pan, -1f, 1f),
					lr = channel.lr,
					faderDb = ClampPhysicalFaderDb(channel.faderDb),
					assignedBus = channel.assignedBus,
					eqHighGainDb = ClampChannelEqGainDb(channel.eqHighGainDb),
					eqMidGainDb = ClampChannelEqGainDb(channel.eqMidGainDb),
					eqMidFrequencyHz = ClampChannelEqMidFrequencyHz(channel.eqMidFrequencyHz),
					eqLowGainDb = ClampChannelEqGainDb(channel.eqLowGainDb)
				});
			}

			targetPreset.Outputs.Clear();
			for (int i = 0; i < outputs.Count; i++)
			{
				MixerBusOutput output = outputs[i];
				targetPreset.Outputs.Add(new MixerBusOutputPresetData
				{
					busName = output.busName,
					enabled = output.enabled,
					muted = output.muted,
					polarityInvert = output.polarityInvert,
					levelDb = ClampPhysicalFaderDb(output.levelDb),
					outputTrimDb = output.outputTrimDb
				});
			}
		}

		public bool LoadFromPreset(Onyx24MixerPreset sourcePreset)
		{
			if (sourcePreset == null)
			{
				return false;
			}

			sourcePreset.EnsureData();
			presetAsset = sourcePreset;
			presetName = NormalizePresetName(sourcePreset.PresetName);
			mixerModel = string.IsNullOrWhiteSpace(sourcePreset.MixerModel) ? mixerModel : sourcePreset.MixerModel;
			masterFaderDb = ClampPhysicalFaderDb(sourcePreset.MasterFaderDb);
			AcousticBands.EnsureArray(ref masterEqBandGainDb, 0f);
			AcousticBands.Copy(sourcePreset.MasterEqBandGainDb, masterEqBandGainDb);

			EnsureDefaultLayout();

			int channelCount = Mathf.Min(channels.Count, sourcePreset.Channels.Count);
			for (int i = 0; i < channelCount; i++)
			{
				MixerInputChannel channel = channels[i];
				MixerInputChannelPresetData data = sourcePreset.Channels[i];
				channel.channelName = string.IsNullOrWhiteSpace(data.channelName) ? channel.channelName : data.channelName;
				channel.enabled = data.enabled;
				channel.phantomPower = data.phantomPower;
				channel.hiZ = data.hiZ;
				channel.muted = data.muted;
				channel.solo = data.solo;
				channel.polarityInvert = data.polarityInvert;
				channel.preampGainDb = ClampPhysicalPreampGainDb(data.preampGainDb);
				channel.trimDb = data.trimDb;
				channel.lowCutEnabled = data.lowCutEnabled;
				channel.hpfHz = Mathf.Max(10f, data.hpfHz);
				channel.lpfHz = Mathf.Max(channel.hpfHz + 10f, data.lpfHz);
				channel.eqEnabled = data.eqEnabled;
				channel.pan = Mathf.Clamp(data.pan, -1f, 1f);
				channel.lr = data.lr;
				channel.faderDb = ClampPhysicalFaderDb(data.faderDb);
				channel.assignedBus = string.IsNullOrWhiteSpace(data.assignedBus) ? "MAIN" : data.assignedBus;
				channel.eqHighGainDb = ClampChannelEqGainDb(data.eqHighGainDb);
				channel.eqMidGainDb = ClampChannelEqGainDb(data.eqMidGainDb);
				channel.eqMidFrequencyHz = ClampChannelEqMidFrequencyHz(data.eqMidFrequencyHz);
				channel.eqLowGainDb = ClampChannelEqGainDb(data.eqLowGainDb);
				UpdateChannelEqBands(channel);
			}

			int outputCount = Mathf.Min(outputs.Count, sourcePreset.Outputs.Count);
			for (int i = 0; i < outputCount; i++)
			{
				MixerBusOutput output = outputs[i];
				MixerBusOutputPresetData data = sourcePreset.Outputs[i];
				output.busName = string.IsNullOrWhiteSpace(data.busName) ? output.busName : data.busName;
				output.enabled = data.enabled;
				output.muted = data.muted;
				output.polarityInvert = data.polarityInvert;
				output.levelDb = ClampPhysicalFaderDb(data.levelDb);
				output.outputTrimDb = data.outputTrimDb;
			}

			return true;
		}

		public void CapturePresetFromCurrent()
		{
			if (presetAsset == null)
			{
				return;
			}

			SaveToPreset(presetAsset);
		}

		public bool ApplyPresetAsset()
		{
			return LoadFromPreset(presetAsset);
		}

		private static string NormalizePresetName(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "New Preset";
			}

			return value.Trim();
		}

		private void EnsureDefaultLayout()
		{
			if (channels == null)
			{
				channels = new List<MixerInputChannel>();
			}

			if (outputs == null)
			{
				outputs = new List<MixerBusOutput>();
			}

			totalChannels = Mathf.Clamp(totalChannels, 1, DefaultChannelLayout.Length);
			microphoneInputs = Mathf.Clamp(microphoneInputs, 0, totalChannels + stereoInputs);
			stereoInputs = Mathf.Clamp(stereoInputs, 0, 8);
			xlrInputs = Mathf.Clamp(xlrInputs, 0, totalChannels + stereoInputs);
			jackInputs = Mathf.Clamp(jackInputs, 0, totalChannels + stereoInputs + stereoInputs);
			monitorOutputs = Mathf.Clamp(monitorOutputs, 0, 8);
			jackOutputs = Mathf.Clamp(jackOutputs, 0, 32);

			while (channels.Count < totalChannels)
			{
				channels.Add(new MixerInputChannel());
			}

			while (channels.Count > totalChannels)
			{
				channels.RemoveAt(channels.Count - 1);
			}

			for (int i = 0; i < channels.Count; i++)
			{
				MixerInputChannel channel = channels[i];
				channel.channelName = DefaultChannelLayout[i];
				if (string.IsNullOrWhiteSpace(channel.assignedBus))
				{
					channel.assignedBus = "MAIN";
				}

				AcousticBands.EnsureArray(ref channel.eqBandGainDb, 0f);
				UpdateChannelEqBands(channel);
				channel.preampGainDb = ClampPhysicalPreampGainDb(channel.preampGainDb);
				channel.faderDb = ClampPhysicalFaderDb(channel.faderDb);
				channel.hpfHz = Mathf.Max(10f, channel.hpfHz);
				channel.lpfHz = Mathf.Max(channel.hpfHz + 10f, channel.lpfHz);
				channel.pan = Mathf.Clamp(channel.pan, -1f, 1f);
			}

			int desiredOutputs = Mathf.Max(2, monitorOutputs + 2);
			while (outputs.Count < desiredOutputs)
			{
				outputs.Add(new MixerBusOutput());
			}

			while (outputs.Count > desiredOutputs)
			{
				outputs.RemoveAt(outputs.Count - 1);
			}

			for (int i = 0; i < outputs.Count; i++)
			{
				MixerBusOutput output = outputs[i];
				if (string.IsNullOrWhiteSpace(output.busName))
				{
					output.busName = i switch
					{
						0 => "MAIN L",
						1 => "MAIN R",
						_ => "AUX " + (i - 1)
					};
				}

				output.levelDb = ClampPhysicalFaderDb(output.levelDb);
			}
		}

		private static bool IsStereoChannel(MixerInputChannel channel)
		{
			return channel != null && !string.IsNullOrWhiteSpace(channel.channelName) && channel.channelName.Contains("/");
		}

		private void UpdateChannelEqBands(MixerInputChannel channel)
		{
			if (channel == null)
			{
				return;
			}

			AcousticBands.EnsureArray(ref channel.eqBandGainDb, 0f);
			channel.eqHighGainDb = ClampChannelEqGainDb(channel.eqHighGainDb);
			channel.eqMidGainDb = ClampChannelEqGainDb(channel.eqMidGainDb);
			channel.eqMidFrequencyHz = ClampChannelEqMidFrequencyHz(channel.eqMidFrequencyHz);
			channel.eqLowGainDb = ClampChannelEqGainDb(channel.eqLowGainDb);

			float midCenter = channel.eqMidFrequencyHz;
			float halfWidth = MidBellWidthOctaves * 0.5f;
			float midLow = midCenter / Mathf.Pow(2f, halfWidth);
			float midHigh = midCenter * Mathf.Pow(2f, halfWidth);

			for (int i = 0; i < AcousticBands.Count; i++)
			{
				float f = AcousticBands.CenterFrequenciesHz[i];
				float lowWeight = AcousticBands.LPFWeight(SimulatedLowShelfHz, f);
				float highWeight = 1f - AcousticBands.LPFWeight(SimulatedHighShelfReferenceHz, f);
				float midWeight = AcousticBands.BandPassWeight(midLow, midHigh, f);
				channel.eqBandGainDb[i] = (channel.eqLowGainDb * lowWeight) + (channel.eqMidGainDb * midWeight) + (channel.eqHighGainDb * highWeight);
			}
		}

		private bool IsAssignedToBus(MixerInputChannel channel, string busName)
		{
			if (channel == null)
			{
				return false;
			}

			string assigned = string.IsNullOrWhiteSpace(channel.assignedBus) ? "MAIN" : channel.assignedBus.Trim();
			string target = string.IsNullOrWhiteSpace(busName) ? "MAIN" : busName.Trim();

			if (string.Equals(assigned, "MAIN", StringComparison.OrdinalIgnoreCase))
			{
				return IsMainBus(target);
			}

			return string.Equals(assigned, target, StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsMainBus(string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return true;
			}

			string normalized = busName.Trim();
			return string.Equals(normalized, "MAIN", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "MAIN L", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "MAIN R", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "L", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "R", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsMainLeftBus(string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return false;
			}

			string normalized = busName.Trim();
			return string.Equals(normalized, "MAIN L", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "L", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsMainRightBus(string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return false;
			}

			string normalized = busName.Trim();
			return string.Equals(normalized, "MAIN R", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "R", StringComparison.OrdinalIgnoreCase);
		}

		private bool AnySoloOnBus(string busName)
		{
			if (channels == null)
			{
				return false;
			}

			for (int i = 0; i < channels.Count; i++)
			{
				MixerInputChannel channel = channels[i];
				if (channel != null && channel.enabled && channel.solo && IsAssignedToBus(channel, busName))
				{
					return true;
				}
			}

			return false;
		}

		private bool IsChannelInputPhysicallyAvailable(MixerInputChannel channel, bool isStereoRightSide)
		{
			if (channel == null || channels == null)
			{
				return false;
			}

			int index = channels.IndexOf(channel);
			if (index < 0)
			{
				return false;
			}

			if (IsStereoChannel(channel))
			{
				int stereoStart = Mathf.Max(0, totalChannels - stereoInputs);
				if (index < stereoStart)
				{
					return !isStereoRightSide && index < microphoneInputs;
				}

				int stereoIndex = index - stereoStart;
				int physicalLeft = microphoneInputs + (stereoIndex * 2);
				int physicalRight = physicalLeft + 1;
				return isStereoRightSide ? physicalRight < jackInputs : physicalLeft < jackInputs;
			}

			return index < microphoneInputs;
		}

		private int GetDeclaredXlrOutputCount()
		{
			if (string.IsNullOrWhiteSpace(xlrOutputs))
			{
				return 0;
			}

			int i = 0;
			while (i < xlrOutputs.Length && char.IsDigit(xlrOutputs[i]))
			{
				i++;
			}

			if (i == 0)
			{
				return 0;
			}

			if (int.TryParse(xlrOutputs.Substring(0, i), out int count))
			{
				return Mathf.Max(0, count);
			}

			return 0;
		}

		private bool IsOutputPhysicallyAvailable(MixerBusOutput output)
		{
			if (output == null || outputs == null)
			{
				return false;
			}

			int index = outputs.IndexOf(output);
			if (index < 0 || index >= jackOutputs)
			{
				return false;
			}

			if ((IsMainLeftBus(output.busName) || IsMainRightBus(output.busName)) && GetDeclaredXlrOutputCount() < 2)
			{
				return false;
			}

			return true;
		}

		private static bool IsMicRoutedToChannel(AcousticMicrophone microphone, MixerInputChannel channel, out bool isRightSide)
		{
			isRightSide = false;
			if (channel == null || microphone == null)
			{
				return false;
			}

			if (channel.microphoneInput == microphone)
			{
				return true;
			}

			if (IsStereoChannel(channel) && channel.stereoRightInput == microphone)
			{
				isRightSide = true;
				return true;
			}

			return false;
		}

		private static float GetInputLoadDb(MixerInputChannel channel, AcousticMicrophone microphone)
		{
			if (channel == null || microphone == null || microphone.OutputImpedanceOhm <= 0f)
			{
				return 0f;
			}

			float loadOhm = channel.hiZ ? 1000000f : 2400f;
			float vin = loadOhm / (loadOhm + microphone.OutputImpedanceOhm);
			float vref = 2400f / (2400f + microphone.OutputImpedanceOhm);
			return AcousticBands.AmplitudeToDb(Mathf.Max(0.0001f, vin / Mathf.Max(0.0001f, vref)));
		}

		private static float GetPanGainDb(float pan, string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return 0f;
			}

			bool isLeft = IsMainLeftBus(busName);
			bool isRight = IsMainRightBus(busName);

			if (!isLeft && !isRight)
			{
				return 0f;
			}

			float t = Mathf.Clamp01((Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.5f);
			float amp = isLeft ? Mathf.Cos(t * Mathf.PI * 0.5f) : Mathf.Sin(t * Mathf.PI * 0.5f);
			return AcousticBands.AmplitudeToDb(Mathf.Max(0.0001f, amp));
		}

		public MixerInputChannel GetChannelForMic(AcousticMicrophone microphone)
		{
			if (microphone == null || channels == null)
			{
				return null;
			}

			for (int i = 0; i < channels.Count; i++)
			{
				if (IsMicRoutedToChannel(microphone, channels[i], out _))
				{
					return channels[i];
				}
			}

			return null;
		}

		private MixerBusOutput GetDirectOutputForSpeaker(AcousticSpeaker speaker)
		{
			if (speaker == null || outputs == null)
			{
				return null;
			}

			for (int i = 0; i < outputs.Count; i++)
			{
				if (outputs[i].targetSpeaker == speaker)
				{
					return outputs[i];
				}
			}

			return null;
		}

		private bool TryResolveSpeakerSignalPath(AcousticSpeaker speaker, int band, HashSet<AcousticSpeaker> visited, out MixerBusOutput output, out AcousticSpeaker mixerTargetSpeaker, out float additionalGainDb, out float additionalPhaseRad)
		{
			output = null;
			mixerTargetSpeaker = null;
			additionalGainDb = 0f;
			additionalPhaseRad = 0f;

			if (speaker == null)
			{
				return false;
			}

			if (visited == null)
			{
				visited = new HashSet<AcousticSpeaker>();
			}

			if (!visited.Add(speaker))
			{
				return false;
			}

			output = GetDirectOutputForSpeaker(speaker);
			if (output != null)
			{
				mixerTargetSpeaker = speaker;
				return true;
			}

			if (!speaker.FeedFromUpstreamSpeaker || speaker.UpstreamSpeaker == null)
			{
				return false;
			}

			if (!TryResolveSpeakerSignalPath(speaker.UpstreamSpeaker, band, visited, out output, out mixerTargetSpeaker, out additionalGainDb, out additionalPhaseRad))
			{
				return false;
			}

			additionalGainDb += speaker.UpstreamSignalTrimDb;
			if (speaker.UpstreamPolarityInvert)
			{
				additionalPhaseRad += Mathf.PI;
			}

			float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			additionalPhaseRad += 2f * Mathf.PI * f * speaker.UpstreamLatencySeconds;

			AcousticSignalWire upstreamWire = speaker.UpstreamInputWire;
			if (upstreamWire != null)
			{
				additionalGainDb += upstreamWire.GetLossDb(band, speaker.UpstreamSourceImpedanceOhm, speaker.InputImpedanceOhm);
				additionalPhaseRad += upstreamWire.GetPhaseRadians();
				additionalPhaseRad += 2f * Mathf.PI * f * upstreamWire.GetDelaySeconds();
			}

			return true;
		}

		public MixerBusOutput GetOutputForSpeaker(AcousticSpeaker speaker)
		{
			TryResolveSpeakerSignalPath(speaker, 0, new HashSet<AcousticSpeaker>(), out MixerBusOutput output, out _, out _, out _);
			return output;
		}

		public float GetSpeakerBandEmissionOffsetDb(AcousticSpeaker speaker, int band, out float sourcePhaseRad)
		{
			sourcePhaseRad = 0f;
			if (speaker == null || IsPhysicalFaderOff(masterFaderDb))
			{
				return -120f;
			}

			float gainDb = simulationReferenceInputDbu - speaker.NominalInputLevelDbuForMaxSpl + GetPhysicalFaderGainDb(masterFaderDb) + masterEqBandGainDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];

			if (!TryResolveSpeakerSignalPath(speaker, band, new HashSet<AcousticSpeaker>(), out MixerBusOutput output, out AcousticSpeaker mixerTargetSpeaker, out float additionalGainDb, out float additionalPhaseRad) || output == null || mixerTargetSpeaker == null)
			{
				return -120f;
			}

			if (!output.enabled || output.muted || IsPhysicalFaderOff(output.levelDb))
			{
				return -120f;
			}

			gainDb += GetPhysicalFaderGainDb(output.levelDb) + output.outputTrimDb;

			if (output.polarityInvert)
			{
				sourcePhaseRad += Mathf.PI;
			}

			if (output.outputWire != null)
			{
				gainDb += output.outputWire.GetLossDb(band, 100f, mixerTargetSpeaker.InputImpedanceOhm);
				sourcePhaseRad += output.outputWire.GetPhaseRadians();
				sourcePhaseRad += 2f * Mathf.PI * f * output.outputWire.GetDelaySeconds();
			}

			gainDb += additionalGainDb;
			sourcePhaseRad += additionalPhaseRad;
			sourcePhaseRad += 2f * Mathf.PI * f * (internalLatencyMs * 0.001f);
			return gainDb;
		}

		public float GetMicToSpeakerVoltageGainAmplitude(AcousticMicrophone microphone, AcousticSpeaker speaker, int band, out float chainPhaseRad)
		{
			chainPhaseRad = 0f;
			if (microphone == null || speaker == null)
			{
				return 0f;
			}

			MixerInputChannel channel = GetChannelForMic(microphone);
			bool isStereoRightInput = false;
			if (channel == null)
			{
				return 0f;
			}

			if (!TryResolveSpeakerSignalPath(speaker, band, new HashSet<AcousticSpeaker>(), out MixerBusOutput output, out AcousticSpeaker mixerTargetSpeaker, out float additionalGainDb, out float additionalPhaseRad) || output == null || mixerTargetSpeaker == null)
			{
				return 0f;
			}

			if (!IsMicRoutedToChannel(microphone, channel, out isStereoRightInput))
			{
				return 0f;
			}

			if (!IsChannelInputPhysicallyAvailable(channel, isStereoRightInput) || !IsOutputPhysicallyAvailable(output))
			{
				return 0f;
			}

			if (!channel.enabled || channel.muted || !output.enabled || output.muted)
			{
				return 0f;
			}

			if (!IsAssignedToBus(channel, output.busName))
			{
				return 0f;
			}

			if (microphone.RequiresPhantomPower && (!phantomPowerAvailable || !channel.phantomPower))
			{
				return 0f;
			}

			if (AnySoloOnBus(output.busName) && !channel.solo)
			{
				return 0f;
			}

			if (!channel.lr && IsMainBus(output.busName))
			{
				return 0f;
			}

			if (IsPhysicalFaderOff(channel.faderDb) || IsPhysicalFaderOff(output.levelDb) || IsPhysicalFaderOff(masterFaderDb))
			{
				return 0f;
			}

			float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			float gainDb = ClampPhysicalPreampGainDb(channel.preampGainDb) + channel.trimDb + GetPhysicalFaderGainDb(channel.faderDb) + GetPhysicalFaderGainDb(output.levelDb) + output.outputTrimDb;
			gainDb += GetPhysicalFaderGainDb(masterFaderDb) + masterEqBandGainDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			gainDb += GetPanGainDb(channel.pan, output.busName);
			if (IsStereoChannel(channel) && IsMainBus(output.busName))
			{
				float balance = Mathf.Clamp(channel.pan, -1f, 1f);
				if (isStereoRightInput)
				{
					gainDb += AcousticBands.AmplitudeToDb(Mathf.Max(0.0001f, Mathf.Clamp01(1f - Mathf.Max(0f, -balance))));
				}
				else
				{
					gainDb += AcousticBands.AmplitudeToDb(Mathf.Max(0.0001f, Mathf.Clamp01(1f - Mathf.Max(0f, balance))));
				}
			}
			if (channel.eqEnabled)
			{
				gainDb += channel.eqBandGainDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			}

			if (channel.lowCutEnabled)
			{
				gainDb += AcousticBands.AmplitudeToDb(AcousticBands.HPFWeight(Mathf.Max(10f, channel.hpfHz), f));
			}

			gainDb += AcousticBands.AmplitudeToDb(AcousticBands.LPFWeight(Mathf.Max(channel.hpfHz + 10f, channel.lpfHz), f));

			if (channel.polarityInvert)
			{
				chainPhaseRad += Mathf.PI;
			}

			if (output.polarityInvert)
			{
				chainPhaseRad += Mathf.PI;
			}

			chainPhaseRad += 2f * Mathf.PI * f * (internalLatencyMs * 0.001f);

			gainDb += GetInputLoadDb(channel, microphone);

			if (channel.inputWire != null)
			{
				gainDb += channel.inputWire.GetLossDb(band, microphone.OutputImpedanceOhm, channel.hiZ ? 1000000f : 2400f);
				chainPhaseRad += channel.inputWire.GetPhaseRadians();
				chainPhaseRad += 2f * Mathf.PI * f * channel.inputWire.GetDelaySeconds();
			}

			if (output.outputWire != null)
			{
				gainDb += output.outputWire.GetLossDb(band, 100f, mixerTargetSpeaker.InputImpedanceOhm);
				chainPhaseRad += output.outputWire.GetPhaseRadians();
				chainPhaseRad += 2f * Mathf.PI * f * output.outputWire.GetDelaySeconds();
			}

			gainDb += additionalGainDb;
			chainPhaseRad += additionalPhaseRad;

			return AcousticBands.DbToAmplitude(gainDb);
		}
	}
}
