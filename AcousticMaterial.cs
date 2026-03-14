using UnityEngine;

namespace PhysicalAcousticsSim
{
	[DisallowMultipleComponent]
	public sealed class AcousticMaterial : MonoBehaviour
	{
		[SerializeField] private MaterialPreset preset = MaterialPreset.Concrete;
		[SerializeField] private string materialLabel = "Concrete";
		[SerializeField] private float thicknessMeters = 0.15f;
		[SerializeField] private float densityKgPerM3 = 2400f;
		[SerializeField] private float damping = 0.05f;
		[SerializeField] private float youngModulusGPa = 24f;

		[SerializeField] private float[] absorption = new float[AcousticBands.Count];
		[SerializeField] private float[] scattering = new float[AcousticBands.Count];
		[SerializeField] private float[] transmissionLossDb = new float[AcousticBands.Count];
		[SerializeField] private float[] structureTransmission = new float[AcousticBands.Count];

		public MaterialPreset Preset => preset;
		public string MaterialLabel => materialLabel;
		public float ThicknessMeters => thicknessMeters;
		public float DensityKgPerM3 => densityKgPerM3;
		public float Damping => damping;
		public float YoungModulusGPa => youngModulusGPa;

		private void Reset()
		{
			ApplyPreset();
		}

		private void OnValidate()
		{
			AcousticBands.EnsureArray(ref absorption, 0.05f);
			AcousticBands.EnsureArray(ref scattering, 0.10f);
			AcousticBands.EnsureArray(ref transmissionLossDb, 20f);
			AcousticBands.EnsureArray(ref structureTransmission, 0.5f);

			thicknessMeters = Mathf.Max(0.001f, thicknessMeters);
			densityKgPerM3 = Mathf.Max(1f, densityKgPerM3);
			damping = Mathf.Clamp01(damping);
			youngModulusGPa = Mathf.Max(0.1f, youngModulusGPa);
		}

		[ContextMenu("Apply Preset")]
		public void ApplyPreset()
		{
			AcousticBands.EnsureArray(ref absorption, 0.05f);
			AcousticBands.EnsureArray(ref scattering, 0.10f);
			AcousticBands.EnsureArray(ref transmissionLossDb, 20f);
			AcousticBands.EnsureArray(ref structureTransmission, 0.5f);

			switch (preset)
			{
				case MaterialPreset.Concrete:
					materialLabel = "Concrete";
					thicknessMeters = 0.18f;
					densityKgPerM3 = 2400f;
					damping = 0.05f;
					youngModulusGPa = 24f;
					Assign(absorption, 0.01f, 0.01f, 0.015f, 0.02f, 0.02f, 0.025f, 0.03f, 0.04f);
					Assign(scattering, 0.05f, 0.05f, 0.06f, 0.06f, 0.07f, 0.08f, 0.10f, 0.12f);
					Assign(transmissionLossDb, 38f, 42f, 48f, 54f, 58f, 62f, 66f, 70f);
					Assign(structureTransmission, 0.95f, 0.92f, 0.84f, 0.68f, 0.46f, 0.28f, 0.15f, 0.08f);
					break;

				case MaterialPreset.Wood:
					materialLabel = "Wood";
					thicknessMeters = 0.04f;
					densityKgPerM3 = 650f;
					damping = 0.14f;
					youngModulusGPa = 12f;
					Assign(absorption, 0.10f, 0.08f, 0.07f, 0.07f, 0.08f, 0.09f, 0.10f, 0.11f);
					Assign(scattering, 0.12f, 0.12f, 0.14f, 0.16f, 0.18f, 0.20f, 0.22f, 0.24f);
					Assign(transmissionLossDb, 16f, 18f, 21f, 24f, 27f, 30f, 33f, 36f);
					Assign(structureTransmission, 0.88f, 0.84f, 0.72f, 0.55f, 0.40f, 0.24f, 0.14f, 0.08f);
					break;

				case MaterialPreset.Metal:
					materialLabel = "Metal";
					thicknessMeters = 0.004f;
					densityKgPerM3 = 7800f;
					damping = 0.03f;
					youngModulusGPa = 200f;
					Assign(absorption, 0.01f, 0.01f, 0.015f, 0.02f, 0.02f, 0.02f, 0.025f, 0.03f);
					Assign(scattering, 0.06f, 0.06f, 0.07f, 0.08f, 0.10f, 0.12f, 0.14f, 0.16f);
					Assign(transmissionLossDb, 10f, 12f, 15f, 18f, 21f, 24f, 27f, 30f);
					Assign(structureTransmission, 0.97f, 0.95f, 0.88f, 0.72f, 0.52f, 0.32f, 0.18f, 0.10f);
					break;

				case MaterialPreset.Glass:
					materialLabel = "Glass";
					thicknessMeters = 0.008f;
					densityKgPerM3 = 2500f;
					damping = 0.04f;
					youngModulusGPa = 70f;
					Assign(absorption, 0.04f, 0.03f, 0.02f, 0.02f, 0.02f, 0.02f, 0.03f, 0.04f);
					Assign(scattering, 0.03f, 0.03f, 0.04f, 0.04f, 0.05f, 0.05f, 0.06f, 0.08f);
					Assign(transmissionLossDb, 14f, 16f, 20f, 24f, 28f, 31f, 34f, 36f);
					Assign(structureTransmission, 0.92f, 0.90f, 0.76f, 0.55f, 0.36f, 0.22f, 0.12f, 0.08f);
					break;

				case MaterialPreset.Fabric:
					materialLabel = "Fabric";
					thicknessMeters = 0.008f;
					densityKgPerM3 = 140f;
					damping = 0.28f;
					youngModulusGPa = 1.4f;
					Assign(absorption, 0.05f, 0.08f, 0.14f, 0.28f, 0.46f, 0.62f, 0.72f, 0.78f);
					Assign(scattering, 0.06f, 0.07f, 0.08f, 0.10f, 0.12f, 0.14f, 0.16f, 0.18f);
					Assign(transmissionLossDb, 4f, 5f, 7f, 9f, 11f, 13f, 15f, 17f);
					Assign(structureTransmission, 0.16f, 0.12f, 0.08f, 0.05f, 0.03f, 0.02f, 0.01f, 0.01f);
					break;

				case MaterialPreset.Rubber:
					materialLabel = "Rubber";
					thicknessMeters = 0.012f;
					densityKgPerM3 = 1200f;
					damping = 0.42f;
					youngModulusGPa = 0.08f;
					Assign(absorption, 0.08f, 0.10f, 0.14f, 0.18f, 0.24f, 0.32f, 0.40f, 0.48f);
					Assign(scattering, 0.06f, 0.06f, 0.08f, 0.10f, 0.12f, 0.14f, 0.16f, 0.18f);
					Assign(transmissionLossDb, 8f, 10f, 13f, 16f, 19f, 22f, 25f, 28f);
					Assign(structureTransmission, 0.28f, 0.24f, 0.18f, 0.12f, 0.08f, 0.05f, 0.03f, 0.02f);
					break;

				case MaterialPreset.Foam:
					materialLabel = "Foam";
					thicknessMeters = 0.05f;
					densityKgPerM3 = 35f;
					damping = 0.55f;
					youngModulusGPa = 0.01f;
					Assign(absorption, 0.08f, 0.14f, 0.28f, 0.50f, 0.74f, 0.86f, 0.92f, 0.94f);
					Assign(scattering, 0.08f, 0.10f, 0.12f, 0.14f, 0.16f, 0.18f, 0.18f, 0.18f);
					Assign(transmissionLossDb, 5f, 7f, 9f, 11f, 13f, 15f, 17f, 18f);
					Assign(structureTransmission, 0.05f, 0.04f, 0.03f, 0.02f, 0.015f, 0.01f, 0.01f, 0.01f);
					break;

				case MaterialPreset.Carpet:
					materialLabel = "Carpet";
					thicknessMeters = 0.018f;
					densityKgPerM3 = 300f;
					damping = 0.32f;
					youngModulusGPa = 0.2f;
					Assign(absorption, 0.02f, 0.05f, 0.10f, 0.24f, 0.46f, 0.62f, 0.70f, 0.72f);
					Assign(scattering, 0.10f, 0.12f, 0.14f, 0.18f, 0.20f, 0.22f, 0.24f, 0.24f);
					Assign(transmissionLossDb, 7f, 9f, 11f, 14f, 17f, 19f, 21f, 23f);
					Assign(structureTransmission, 0.12f, 0.10f, 0.08f, 0.05f, 0.03f, 0.02f, 0.01f, 0.01f);
					break;

				case MaterialPreset.Plaster:
					materialLabel = "Plaster";
					thicknessMeters = 0.02f;
					densityKgPerM3 = 900f;
					damping = 0.11f;
					youngModulusGPa = 4f;
					Assign(absorption, 0.03f, 0.03f, 0.04f, 0.05f, 0.06f, 0.06f, 0.08f, 0.10f);
					Assign(scattering, 0.08f, 0.08f, 0.10f, 0.12f, 0.14f, 0.16f, 0.18f, 0.20f);
					Assign(transmissionLossDb, 18f, 20f, 24f, 28f, 31f, 34f, 37f, 40f);
					Assign(structureTransmission, 0.72f, 0.68f, 0.54f, 0.38f, 0.26f, 0.16f, 0.10f, 0.06f);
					break;

				case MaterialPreset.Curtain:
					materialLabel = "Curtain";
					thicknessMeters = 0.01f;
					densityKgPerM3 = 180f;
					damping = 0.30f;
					youngModulusGPa = 0.5f;
					Assign(absorption, 0.04f, 0.08f, 0.16f, 0.30f, 0.48f, 0.62f, 0.72f, 0.78f);
					Assign(scattering, 0.05f, 0.06f, 0.08f, 0.10f, 0.12f, 0.14f, 0.16f, 0.18f);
					Assign(transmissionLossDb, 5f, 6f, 8f, 10f, 12f, 14f, 16f, 18f);
					Assign(structureTransmission, 0.18f, 0.14f, 0.10f, 0.06f, 0.04f, 0.02f, 0.01f, 0.01f);
					break;

				default:
					break;
			}
		}

		public float GetAbsorption(int band)
		{
			return Mathf.Clamp01(absorption[Mathf.Clamp(band, 0, AcousticBands.Count - 1)]);
		}

		public float GetScattering(int band)
		{
			return Mathf.Clamp01(scattering[Mathf.Clamp(band, 0, AcousticBands.Count - 1)]);
		}

		public float GetTransmissionAmplitude(int band)
		{
			float tl = Mathf.Max(0f, transmissionLossDb[Mathf.Clamp(band, 0, AcousticBands.Count - 1)]);
			return Mathf.Pow(10f, -tl / 20f);
		}

		public float GetReflectionAmplitude(int band)
		{
			float absorptionEnergy = GetAbsorption(band);
			float transmissionEnergy = Mathf.Pow(GetTransmissionAmplitude(band), 2f);
			float reflectionEnergy = Mathf.Clamp01(1f - absorptionEnergy - transmissionEnergy);
			return Mathf.Sqrt(reflectionEnergy);
		}

		public float GetStructureAmplitude(int band)
		{
			return Mathf.Clamp01(structureTransmission[Mathf.Clamp(band, 0, AcousticBands.Count - 1)]);
		}

		public Bounds GetBounds()
		{
			Collider c = GetComponent<Collider>();
			if (c != null)
			{
				return c.bounds;
			}

			Renderer r = GetComponent<Renderer>();
			if (r != null)
			{
				return r.bounds;
			}

			return new Bounds(transform.position, Vector3.one * 0.1f);
		}

		private static void Assign(float[] target, params float[] values)
		{
			if (target == null)
			{
				return;
			}

			for (int i = 0; i < Mathf.Min(target.Length, values.Length); i++)
			{
				target[i] = values[i];
			}
		}
	}
}