using System.Collections.Generic;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell
{
    /// <summary>Rideable vehicles, parts and service prices that the city's shops sell (Phase 13).</summary>
    [CreateAssetMenu(menuName = "Opening Bell/Vehicle Library", fileName = "VehicleLibrary")]
    public sealed class VehicleLibrary : ScriptableObject
    {
        [SerializeField] private List<VehicleModel> models = new List<VehicleModel>();
        [SerializeField] private List<PartSpec> parts = new List<PartSpec>();
        [Tooltip("Used vehicles offered by private sellers in the computer's classifieds.")]
        [SerializeField] private List<UsedListing> listings = new List<UsedListing>();
        [Tooltip("3D models for cars (and traffic), looked up by name, e.g. \"sedan\".")]
        [SerializeField] private List<GameObject> carMeshes = new List<GameObject>();
        [Tooltip("Models the traffic system draws from (names in Car Meshes); repeats make a model more common.")]
        [SerializeField] private List<string> trafficMix = new List<string>();
        [Tooltip("Recorded engines, filled by Opening Bell/Rebuild Engine Sounds from Assets/Audio/Engines.")]
        [SerializeField] private List<EngineSoundSet> engineSounds = new List<EngineSoundSet>();
        [SerializeField] private double tuneUpPrice = 60;
        [SerializeField] private double newTiresPrice = 45;

        public IReadOnlyList<VehicleModel> Models => models;
        public IReadOnlyList<PartSpec> Parts => parts;
        public IReadOnlyList<string> TrafficMix => trafficMix;
        public IReadOnlyList<EngineSoundSet> EngineSounds => engineSounds;

        public GameObject CarMesh(string name)
        {
            foreach (GameObject m in carMeshes)
                if (m != null && m.name == name) return m;
            return null;
        }

        /// <summary>The recorded engine named <paramref name="name"/>, or null.</summary>
        public EngineSoundSet EngineSound(string name)
        {
            foreach (EngineSoundSet s in engineSounds)
                if (s != null && s.Name == name) return s;
            return null;
        }

#if UNITY_EDITOR
        public void EditorSetEngineSounds(List<EngineSoundSet> sets) => engineSounds = sets;
#endif

        public VehicleCatalog CreateCatalog() => new VehicleCatalog(models, parts, (decimal)tuneUpPrice, (decimal)newTiresPrice, listings);
    }

    /// <summary>
    /// One engine recorded at several steady rpms, as two layers (engine bay and exhaust), plus a startup. Each clip
    /// loops at the rpm it was recorded at; playback blends the two nearest and pitches them to the live rpm.
    /// </summary>
    [System.Serializable]
    public sealed class EngineSoundSet
    {
        public string Name = "";
        public AudioClip[] Engine = new AudioClip[0];
        public float[] EngineRpm = new float[0];
        public AudioClip[] Exhaust = new AudioClip[0];
        public float[] ExhaustRpm = new float[0];
        public AudioClip Startup;
    }
}
