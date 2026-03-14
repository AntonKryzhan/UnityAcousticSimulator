#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PhysicalAcousticsSim
{
	[CustomEditor(typeof(AcousticSimulator))]
	public sealed class AcousticSimulatorEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			EditorGUILayout.Space(10f);

			AcousticSimulator simulator = (AcousticSimulator)target;

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Auto Collect", GUILayout.Height(28f)))
				{
					Undo.RecordObject(simulator, "Auto Collect Acoustics");
					simulator.AutoCollect();
					EditorUtility.SetDirty(simulator);
				}

				if (GUILayout.Button("Simulate", GUILayout.Height(28f)))
				{
					Undo.RecordObject(simulator, "Simulate Acoustics");
					simulator.Simulate();
					EditorUtility.SetDirty(simulator);
				}

				if (GUILayout.Button("Clear Results", GUILayout.Height(28f)))
				{
					Undo.RecordObject(simulator, "Clear Acoustic Results");
					simulator.ClearResults();
					EditorUtility.SetDirty(simulator);
				}
			}

			EditorGUILayout.Space(8f);

			EditorGUILayout.HelpBox(
				"Simulation is offline / analytical. It computes speaker directivity, multi-band attenuation, reflections, phase, signal-chain delay, feedback loop risk and structure-borne low-frequency transfer through touching materials.",
				MessageType.Info
			);

			if (simulator.MicrophoneResults != null && simulator.Hotspots != null)
			{
				EditorGUILayout.LabelField("Stored results", EditorStyles.boldLabel);
				EditorGUILayout.LabelField("Microphones", simulator.MicrophoneResults.Count.ToString());
				EditorGUILayout.LabelField("Hotspots", simulator.Hotspots.Count.ToString());
			}
		}
	}
}
#endif
