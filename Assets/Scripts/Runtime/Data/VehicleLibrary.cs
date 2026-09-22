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
        [SerializeField] private double tuneUpPrice = 60;
        [SerializeField] private double newTiresPrice = 45;

        public IReadOnlyList<VehicleModel> Models => models;
        public IReadOnlyList<PartSpec> Parts => parts;
        public IReadOnlyList<string> TrafficMix => trafficMix;

        public GameObject CarMesh(string name)
        {
            foreach (GameObject m in carMeshes)
                if (m != null && m.name == name) return m;
            return null;
        }

        public VehicleCatalog CreateCatalog() => new VehicleCatalog(models, parts, (decimal)tuneUpPrice, (decimal)newTiresPrice, listings);
    }
}
