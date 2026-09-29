using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum HomeKind { Apartment, Starter, Family, Mansion, Penthouse, Office }

    /// <summary>
    /// A home the player can own (or, for the apartment, rents from the start): where it is, what's indoors, its lot,
    /// and its price. The houses come from the town plan's walkable lots; the penthouse is the top of Harborview Tower.
    /// </summary>
    public sealed class HomeSpec
    {
        public const string ApartmentId = "apartment";

        public string Id, Name;
        public HomeKind Kind;
        public decimal Price;
        /// <summary>Indoors frame: origin at the middle of the footprint on the ground floor, front facing -z.</summary>
        public Transform Root;
        public Vector2 Size;
        public int Floors = 1;
        /// <summary>The lot (origin on the sidewalk edge, +z into the lot), or null (no garden).</summary>
        public Transform Plot;
        public float LotWidth, LotDepth;
        /// <summary>Local position of the front door in <see cref="Root"/>.</summary>
        public Vector3 DoorLocal;
        /// <summary>Ceiling height when it isn't whole storeys (the penthouse), else 0.</summary>
        public float Height;
        /// <summary>Furniture that comes with it: added to your belongings, where it stands, when you buy.</summary>
        public readonly List<(string Id, int Variant, Vector3 At, float Yaw)> Staging = new List<(string, int, Vector3, float)>();

        public static decimal PriceOf(HomeKind kind) => kind switch
        {
            HomeKind.Starter => 145000m,
            HomeKind.Family => 310000m,
            HomeKind.Mansion => 1450000m,
            HomeKind.Penthouse => 890000m,
            _ => 0m,
        };

        public bool Indoors(Vector3 world)
        {
            Vector3 p = Root.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < Size.x / 2f - 0.05f && Mathf.Abs(p.z) < Size.y / 2f - 0.05f && p.y > -0.5f && p.y < (Height > 0f ? Height + 0.1f : Floors * HouseBuilder.Storey + 0.5f);
        }

        public bool OnLot(Vector3 world)
        {
            if (Plot == null) return false;
            Vector3 p = Plot.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < LotWidth / 2f && p.z > 0f && p.z < LotDepth && p.y > -2f && p.y < 12f;
        }

        public bool Contains(Vector3 world) => Indoors(world) || OnLot(world);
    }
}
