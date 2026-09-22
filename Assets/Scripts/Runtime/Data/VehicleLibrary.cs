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
        [SerializeField] private double tuneUpPrice = 60;
        [SerializeField] private double newTiresPrice = 45;

        public IReadOnlyList<VehicleModel> Models => models;
        public IReadOnlyList<PartSpec> Parts => parts;

        public VehicleCatalog CreateCatalog() => new VehicleCatalog(models, parts, (decimal)tuneUpPrice, (decimal)newTiresPrice);
    }
}
