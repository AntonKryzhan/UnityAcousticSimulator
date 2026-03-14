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
		public float[] eqBandGainDb = new float[AcousticBands.Count];
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

		[Header("Simulation")]
		[SerializeField] private float simulationReferenceInputDbu = 0f;
		[SerializeField] private float internalLatencyMs = 0.65f;
		[SerializeField] private float masterFaderDb = 0f;
		[SerializeField] private float[] masterEqBandGainDb = new float[AcousticBands.Count];
		[SerializeField] private List<MixerInputChannel> channels = new List<MixerInputChannel>();
		[SerializeField] private List<MixerBusOutput> outputs = new List<MixerBusOutput>();

		public string MixerModel => mixerModel;
		public List<MixerInputChannel> Channels => channels;
		public List<MixerBusOutput> Outputs => outputs;

		private void Reset()
		{
			EnsureDefaultLayout();
		}

		private void OnValidate()
		{
			AcousticBands.EnsureArray(ref masterEqBandGainDb, 0f);
			internalLatencyMs = Mathf.Max(0f, internalLatencyMs);
			EnsureDefaultLayout();

			for (int i = 0; i < channels.Count; i++)
			{
				AcousticBands.EnsureArray(ref channels[i].eqBandGainDb, 0f);
				channels[i].hpfHz = Mathf.Max(10f, channels[i].hpfHz);
				channels[i].lpfHz = Mathf.Max(channels[i].hpfHz + 10f, channels[i].lpfHz);
				channels[i].faderDb = Mathf.Clamp(channels[i].faderDb, -60f, 10f);
				channels[i].pan = Mathf.Clamp(channels[i].pan, -1f, 1f);
			}
		}

		[ContextMenu("Ensure Default Layout")]
		public void EnsureDefaultLayout()
		{
			if (channels == null)
			{
				channels = new List<MixerInputChannel>();
			}

			if (outputs == null)
			{
				outputs = new List<MixerBusOutput>();
			}

			if (totalChannels < 1)
			{
				totalChannels = 1;
			}

			while (channels.Count < totalChannels)
			{
				MixerInputChannel ch = new MixerInputChannel();
				ch.channelName = GetDefaultChannelName(channels.Count);
				AcousticBands.EnsureArray(ref ch.eqBandGainDb, 0f);
				channels.Add(ch);
			}

			while (channels.Count > totalChannels)
			{
				channels.RemoveAt(channels.Count - 1);
			}

			for (int i = 0; i < channels.Count; i++)
			{
				if (string.IsNullOrWhiteSpace(channels[i].channelName))
				{
					channels[i].channelName = GetDefaultChannelName(i);
				}

				AcousticBands.EnsureArray(ref channels[i].eqBandGainDb, 0f);
			}

			for (int i = outputs.Count - 1; i >= 0; i--)
			{
				string bus = outputs[i].busName;
				if (string.Equals(bus, "FX", StringComparison.OrdinalIgnoreCase))
				{
					outputs.RemoveAt(i);
				}
			}

			EnsureOutputBus("MAIN L");
			EnsureOutputBus("MAIN R");
			EnsureOutputBus("MAIN");
			EnsureOutputBus("CONTROL ROOM");
			EnsureOutputBus("PHONES");
			EnsureOutputBus("MON1");
			EnsureOutputBus("MON2");
		}

		private static string GetDefaultChannelName(int index)
		{
			if (index >= 0 && index < DefaultChannelLayout.Length)
			{
				return DefaultChannelLayout[index];
			}

			return "CH" + (index + 1).ToString();
		}

		private void EnsureOutputBus(string busName)
		{
			for (int i = 0; i < outputs.Count; i++)
			{
				if (string.Equals(outputs[i].busName, busName, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}

			outputs.Add(new MixerBusOutput { busName = busName });
		}

		private bool AnySoloOnBus(string busName)
		{
			if (channels == null)
			{
				return false;
			}

			for (int i = 0; i < channels.Count; i++)
			{
				MixerInputChannel ch = channels[i];
				if (!ch.enabled || ch.muted || !ch.solo)
				{
					continue;
				}

				if (IsAssignedToBus(ch, busName))
				{
					return true;
				}
			}

			return false;
		}

		private static bool IsMainLeftBus(string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return false;
			}

			string upperBus = busName.Trim().ToUpperInvariant();
			return upperBus == "MAIN L" || upperBus.EndsWith("/L", StringComparison.Ordinal) || upperBus.EndsWith(" L", StringComparison.Ordinal);
		}

		private static bool IsMainRightBus(string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return false;
			}

			string upperBus = busName.Trim().ToUpperInvariant();
			return upperBus == "MAIN R" || upperBus.EndsWith("/R", StringComparison.Ordinal) || upperBus.EndsWith(" R", StringComparison.Ordinal);
		}

		private static bool IsMainBus(string busName)
		{
			if (string.IsNullOrWhiteSpace(busName))
			{
				return false;
			}

			string upperBus = busName.Trim().ToUpperInvariant();
			return upperBus == "MAIN" || IsMainLeftBus(upperBus) || IsMainRightBus(upperBus);
		}

		private static bool IsAssignedToBus(MixerInputChannel channel, string busName)
		{
			if (channel == null)
			{
				return false;
			}

			if (string.Equals(channel.assignedBus, busName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return IsMainBus(channel.assignedBus) && IsMainBus(busName);
		}

		private static bool IsStereoChannel(MixerInputChannel channel)
		{
			return channel != null && !string.IsNullOrWhiteSpace(channel.channelName) && channel.channelName.Contains("/");
		}

		private int GetChannelInputSlot(MixerInputChannel channel, bool rightSide)
		{
			if (channels == null || channel == null)
			{
				return -1;
			}

			int slot = 0;
			for (int i = 0; i < channels.Count; i++)
			{
				MixerInputChannel c = channels[i];
				bool stereo = IsStereoChannel(c);
				if (c == channel)
				{
					return slot + ((stereo && rightSide) ? 1 : 0);
				}

				slot += stereo ? 2 : 1;
			}

			return -1;
		}

		private bool IsChannelInputPhysicallyAvailable(MixerInputChannel channel, bool rightSide)
		{
			if (channel == null)
			{
				return false;
			}

			if (rightSide && !IsStereoChannel(channel))
			{
				return false;
			}

			int slot = GetChannelInputSlot(channel, rightSide);
			if (slot < 0 || slot >= jackInputs || slot >= microphoneInputs || slot >= xlrInputs)
			{
				return false;
			}

			return true;
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

		public MixerBusOutput GetOutputForSpeaker(AcousticSpeaker speaker)
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

		public float GetSpeakerBandEmissionOffsetDb(AcousticSpeaker speaker, int band, out float sourcePhaseRad)
		{
			sourcePhaseRad = 0f;
			if (speaker == null)
			{
				return -120f;
			}

			float gainDb = simulationReferenceInputDbu - speaker.NominalInputLevelDbuForMaxSpl + masterFaderDb + masterEqBandGainDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];

			MixerBusOutput output = GetOutputForSpeaker(speaker);
			if (output != null)
			{
				if (!output.enabled || output.muted)
				{
					return -120f;
				}

				gainDb += output.levelDb + output.outputTrimDb;

				if (output.polarityInvert)
				{
					sourcePhaseRad += Mathf.PI;
				}

				if (output.outputWire != null)
				{
					gainDb += output.outputWire.GetLossDb(band, 100f, speaker.InputImpedanceOhm);
					sourcePhaseRad += output.outputWire.GetPhaseRadians();
					sourcePhaseRad += 2f * Mathf.PI * f * output.outputWire.GetDelaySeconds();
				}
			}

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
			MixerBusOutput output = GetOutputForSpeaker(speaker);
			bool isStereoRightInput = false;
			if (channel == null || output == null)
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

			float f = AcousticBands.CenterFrequenciesHz[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
			float gainDb = channel.preampGainDb + channel.trimDb + channel.faderDb + output.levelDb + output.outputTrimDb;
			gainDb += masterFaderDb + masterEqBandGainDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)];
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
				gainDb += output.outputWire.GetLossDb(band, 100f, speaker.InputImpedanceOhm);
				chainPhaseRad += output.outputWire.GetPhaseRadians();
				chainPhaseRad += 2f * Mathf.PI * f * output.outputWire.GetDelaySeconds();
			}

			return AcousticBands.DbToAmplitude(gainDb);
		}
	}
}
