using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The load floor of a pickup's bed or a trailer: things set down on it ride along (parented to the vehicle, held
    /// still), up to <see cref="Capacity"/> cubic metres. <see cref="Id"/> names the vehicle in the save.
    /// </summary>
    public sealed class CargoBed : MonoBehaviour
    {
        public string Id;
        public float Capacity;
        /// <summary>The vehicle's root: cargo is parented here, in its local frame.</summary>
        public Transform Vehicle;
        /// <summary>Closed tailgate or gate: loading needs it open.</summary>
        public bool Open = true;

        public void Configure(string id, float capacity, Transform vehicle)
        {
            Id = id;
            Capacity = capacity;
            Vehicle = vehicle;
        }
    }
}
