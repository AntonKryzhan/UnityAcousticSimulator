using UnityEngine;

namespace PhysicalAcousticsSim
{
	[DisallowMultipleComponent]
	public sealed class AcousticListenerProbe : MonoBehaviour
	{
		[SerializeField] private string probeName = "Listener";
		[SerializeField] private ListenerResult lastResult = new ListenerResult();

		public string ProbeName => probeName;
		public ListenerResult LastResult => lastResult;

		public void StoreResult(float totalSplDb, float peakFrequencyHz, float structureVibrationDb, float[] bandSplDb)
		{
			lastResult.totalSplDb = totalSplDb;
			lastResult.peakFrequencyHz = peakFrequencyHz;
			lastResult.structureVibrationDb = structureVibrationDb;

			if (bandSplDb != null)
			{
				for (int i = 0; i < Mathf.Min(lastResult.bandSplDb.Length, bandSplDb.Length); i++)
				{
					lastResult.bandSplDb[i] = bandSplDb[i];
				}
			}
		}
	}
}
