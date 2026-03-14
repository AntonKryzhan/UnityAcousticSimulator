using UnityEditor;
using UnityEngine;

namespace PhysicalAcousticsSim.EditorTools
{
	[CustomEditor(typeof(Onyx24Mixer))]
	public sealed class Onyx24MixerEditor : UnityEditor.Editor
	{
		private static readonly string[] EqBandLabels = BuildEqBandLabels();

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			DrawModelSection();
			EditorGUILayout.Space();
			DrawPresetSection();
			EditorGUILayout.Space();
			DrawSimulationSection();

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawModelSection()
		{
			EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
			DrawProperty("mixerModel");
			DrawProperty("mixingType");
			DrawProperty("totalChannels");
			DrawProperty("microphoneInputs");
			DrawProperty("stereoInputs");
			DrawProperty("xlrInputs");
			DrawProperty("jackInputs");
			DrawProperty("xlrOutputs");
			DrawProperty("monitorOutputs");
			DrawProperty("jackOutputs");
			DrawProperty("usbInterface");
			DrawProperty("phantomPowerAvailable");
		}

		private void DrawPresetSection()
		{
			EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);

			SerializedProperty presetAssetProperty = serializedObject.FindProperty("presetAsset");
			SerializedProperty presetNameProperty = serializedObject.FindProperty("presetName");

			EditorGUILayout.PropertyField(presetAssetProperty, new GUIContent("Preset Asset"));
			EditorGUILayout.PropertyField(presetNameProperty, new GUIContent("Preset Name"));

			EditorGUILayout.BeginHorizontal();

			if (GUILayout.Button("Create Preset Asset"))
			{
				CreatePresetAsset(presetAssetProperty);
			}

			GUI.enabled = presetAssetProperty.objectReferenceValue != null;
			if (GUILayout.Button("Save To Preset"))
			{
				SaveToPreset((Onyx24MixerPreset)presetAssetProperty.objectReferenceValue);
			}

			if (GUILayout.Button("Load From Preset"))
			{
				LoadFromPreset((Onyx24MixerPreset)presetAssetProperty.objectReferenceValue);
			}
			GUI.enabled = true;

			EditorGUILayout.EndHorizontal();
		}

		private void DrawSimulationSection()
		{
			EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);
			DrawProperty("simulationReferenceInputDbu");
			DrawProperty("internalLatencyMs");
			DrawProperty("masterFaderDb");
			DrawEqBandArray(serializedObject.FindProperty("masterEqBandGainDb"), new GUIContent("Master Eq Band Gain Db"));
			DrawProperty("channels", true);
			DrawProperty("outputs", true);
		}

		private void DrawProperty(string propertyName, bool includeChildren = false)
		{
			SerializedProperty property = serializedObject.FindProperty(propertyName);
			if (property != null)
			{
				EditorGUILayout.PropertyField(property, includeChildren);
			}
		}

		private void CreatePresetAsset(SerializedProperty presetAssetProperty)
		{
			serializedObject.ApplyModifiedProperties();

			Onyx24Mixer mixer = (Onyx24Mixer)target;
			Onyx24MixerPreset preset = ScriptableObject.CreateInstance<Onyx24MixerPreset>();
			mixer.SaveToPreset(preset);

			string safeName = MakeSafeAssetName(preset.PresetName);
			string path = EditorUtility.SaveFilePanelInProject("Create Mixer Preset", safeName, "asset", "Select a location for the mixer preset asset.");
			if (string.IsNullOrWhiteSpace(path))
			{
				DestroyImmediate(preset);
				serializedObject.Update();
				return;
			}

			AssetDatabase.CreateAsset(preset, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			serializedObject.Update();
			presetAssetProperty.objectReferenceValue = preset;
			serializedObject.ApplyModifiedProperties();

			EditorUtility.SetDirty(mixer);
			EditorGUIUtility.PingObject(preset);
		}

		private void SaveToPreset(Onyx24MixerPreset preset)
		{
			if (preset == null)
			{
				return;
			}

			serializedObject.ApplyModifiedProperties();

			Onyx24Mixer mixer = (Onyx24Mixer)target;
			mixer.SaveToPreset(preset);

			EditorUtility.SetDirty(preset);
			EditorUtility.SetDirty(mixer);
			AssetDatabase.SaveAssets();

			serializedObject.Update();
		}

		private void LoadFromPreset(Onyx24MixerPreset preset)
		{
			if (preset == null)
			{
				return;
			}

			serializedObject.ApplyModifiedProperties();

			Onyx24Mixer mixer = (Onyx24Mixer)target;
			Undo.RecordObject(mixer, "Load Mixer Preset");
			mixer.LoadFromPreset(preset);

			EditorUtility.SetDirty(mixer);
			serializedObject.Update();
		}

		internal static void DrawEqBandArray(SerializedProperty arrayProperty, GUIContent label)
		{
			if (arrayProperty == null)
			{
				return;
			}

			arrayProperty.isExpanded = EditorGUILayout.Foldout(arrayProperty.isExpanded, label, true);
			if (!arrayProperty.isExpanded)
			{
				return;
			}

			EditorGUI.indentLevel++;

			int count = Mathf.Min(arrayProperty.arraySize, EqBandLabels.Length);
			for (int i = 0; i < count; i++)
			{
				EditorGUILayout.PropertyField(arrayProperty.GetArrayElementAtIndex(i), new GUIContent(EqBandLabels[i]));
			}

			for (int i = count; i < arrayProperty.arraySize; i++)
			{
				EditorGUILayout.PropertyField(arrayProperty.GetArrayElementAtIndex(i), new GUIContent("Band " + i));
			}

			EditorGUI.indentLevel--;
		}

		private static string[] BuildEqBandLabels()
		{
			string[] labels = new string[AcousticBands.Count];
			for (int i = 0; i < labels.Length; i++)
			{
				labels[i] = FormatFrequency(AcousticBands.CenterFrequenciesHz[i]);
			}

			return labels;
		}

		private static string FormatFrequency(float frequencyHz)
		{
			if (frequencyHz >= 1000f)
			{
				float khz = frequencyHz / 1000f;
				return khz % 1f == 0f ? khz.ToString("0") + " kHz" : khz.ToString("0.##") + " kHz";
			}

			return frequencyHz.ToString("0") + " Hz";
		}

		private static string MakeSafeAssetName(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "NewMixerPreset";
			}

			char[] invalid = System.IO.Path.GetInvalidFileNameChars();
			string safe = value.Trim();
			for (int i = 0; i < invalid.Length; i++)
			{
				safe = safe.Replace(invalid[i], '_');
			}

			return string.IsNullOrWhiteSpace(safe) ? "NewMixerPreset" : safe;
		}
	}

	[CustomPropertyDrawer(typeof(MixerInputChannel))]
	public sealed class MixerInputChannelDrawer : PropertyDrawer
	{
		private const float VerticalSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
			property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);

			if (!property.isExpanded)
			{
				return;
			}

			EditorGUI.indentLevel++;

			DrawProperty(ref line, property, "channelName");
			DrawProperty(ref line, property, "enabled");
			DrawProperty(ref line, property, "microphoneInput");
			DrawProperty(ref line, property, "inputWire");
			DrawProperty(ref line, property, "phantomPower");
			DrawProperty(ref line, property, "hiZ");
			DrawProperty(ref line, property, "muted");
			DrawProperty(ref line, property, "solo");
			DrawProperty(ref line, property, "polarityInvert");
			DrawSlider(ref line, property.FindPropertyRelative("preampGainDb"), new GUIContent("GAIN"), Onyx24Mixer.PhysicalPreampMinDb, Onyx24Mixer.PhysicalPreampMaxDb);
			DrawProperty(ref line, property, "trimDb");
			DrawProperty(ref line, property, "lowCutEnabled");
			DrawProperty(ref line, property, "hpfHz");
			DrawProperty(ref line, property, "lpfHz");
			DrawProperty(ref line, property, "eqEnabled");
			DrawSlider(ref line, property.FindPropertyRelative("eqHighGainDb"), new GUIContent("HI 12 kHz"), Onyx24Mixer.ChannelEqGainMinDb, Onyx24Mixer.ChannelEqGainMaxDb);
			DrawSlider(ref line, property.FindPropertyRelative("eqMidGainDb"), new GUIContent("MID"), Onyx24Mixer.ChannelEqGainMinDb, Onyx24Mixer.ChannelEqGainMaxDb);
			DrawSlider(ref line, property.FindPropertyRelative("eqMidFrequencyHz"), new GUIContent("FREQ"), Onyx24Mixer.ChannelEqMidFreqMinHz, Onyx24Mixer.ChannelEqMidFreqMaxHz);
			DrawSlider(ref line, property.FindPropertyRelative("eqLowGainDb"), new GUIContent("LOW 80 Hz"), Onyx24Mixer.ChannelEqGainMinDb, Onyx24Mixer.ChannelEqGainMaxDb);
			DrawProperty(ref line, property, "pan");
			DrawProperty(ref line, property, "lr");
			DrawSlider(ref line, property.FindPropertyRelative("faderDb"), new GUIContent("Fader Db"), Onyx24Mixer.PhysicalFaderOffDb, Onyx24Mixer.PhysicalFaderMaxDb);
			DrawProperty(ref line, property, "assignedBus");
			DrawProperty(ref line, property, "stereoRightInput");

			EditorGUI.indentLevel--;
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float height = EditorGUIUtility.singleLineHeight;

			if (!property.isExpanded)
			{
				return height;
			}

			height += GetPropertyHeight(property, "channelName");
			height += GetPropertyHeight(property, "enabled");
			height += GetPropertyHeight(property, "microphoneInput");
			height += GetPropertyHeight(property, "inputWire");
			height += GetPropertyHeight(property, "phantomPower");
			height += GetPropertyHeight(property, "hiZ");
			height += GetPropertyHeight(property, "muted");
			height += GetPropertyHeight(property, "solo");
			height += GetPropertyHeight(property, "polarityInvert");
			height += GetSliderHeight();
			height += GetPropertyHeight(property, "trimDb");
			height += GetPropertyHeight(property, "lowCutEnabled");
			height += GetPropertyHeight(property, "hpfHz");
			height += GetPropertyHeight(property, "lpfHz");
			height += GetPropertyHeight(property, "eqEnabled");
			height += GetSliderHeight();
			height += GetSliderHeight();
			height += GetSliderHeight();
			height += GetSliderHeight();
			height += GetPropertyHeight(property, "pan");
			height += GetPropertyHeight(property, "lr");
			height += GetSliderHeight();
			height += GetPropertyHeight(property, "assignedBus");
			height += GetPropertyHeight(property, "stereoRightInput");

			return height;
		}

		private void DrawProperty(ref Rect line, SerializedProperty parent, string relativeName)
		{
			SerializedProperty property = parent.FindPropertyRelative(relativeName);
			if (property == null)
			{
				return;
			}

			float height = EditorGUI.GetPropertyHeight(property, true);
			line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
			line.height = height;
			EditorGUI.PropertyField(line, property, true);
		}

		private void DrawSlider(ref Rect line, SerializedProperty property, GUIContent label, float min, float max)
		{
			if (property == null)
			{
				return;
			}

			line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
			line.height = EditorGUIUtility.singleLineHeight;
			property.floatValue = EditorGUI.Slider(line, label, property.floatValue, min, max);
		}

		private static float GetPropertyHeight(SerializedProperty parent, string relativeName)
		{
			SerializedProperty property = parent.FindPropertyRelative(relativeName);
			if (property == null)
			{
				return 0f;
			}

			return EditorGUI.GetPropertyHeight(property, true) + VerticalSpacing;
		}

		private static float GetSliderHeight()
		{
			return EditorGUIUtility.singleLineHeight + VerticalSpacing;
		}
	}
}