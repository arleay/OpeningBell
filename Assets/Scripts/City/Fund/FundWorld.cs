using OpeningBell.Fund;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The fund in the world (FUND_SPEC §7–9, §14, §18–19, §24): the physical checks the simulation asks for (can
    /// someone reach and sit at a workstation's chair), and the people on Level 26. The simulation never waits for
    /// anything here; this follows it.
    /// </summary>
    public sealed partial class FundWorld : MonoBehaviour
    {
        private CityContext _c;
        private HomeWorld _home;
        private int _seenBelongings = -1;
        private int _pendingBump;

        public HedgeFund Fund => _c.Game.Fund;
        public CityContext City => _c;

        public static FundWorld Build(CityContext c, HomeWorld home)
        {
            var go = new GameObject("Fund world");
            go.transform.SetParent(c.Dynamic, false);
            var w = go.AddComponent<FundWorld>();
            w._c = c;
            w._home = home;
            c.Game.Fund.Access = w.Access;
            w.ConfigurePeople();

            return w;
        }

        private void Update()
        {
            // Furniture moved: once the objects have caught up (next frame), ask the simulation to re-check access.
            if (_home.Belongings.Version != _seenBelongings)
            {
                _seenBelongings = _home.Belongings.Version;
                _pendingBump = 2;
            }
            if (_pendingBump > 0 && --_pendingBump == 0) Fund.AccessVersion++;
            UpdatePeople();

        }

        /// <summary>
        /// Can a person reach this workstation's chair and sit? Somewhere to stand behind or beside the chair that's
        /// clear of walls and furniture, with nothing solid between there and the seat. Null when fine.
        /// </summary>
        public string Access(Workstation w)
        {
            OwnedItem chair = _home.Belongings.Get(w.Chair), desk = _home.Belongings.Get(w.Desk);
            if (chair == null || desk == null) return null;
            ItemView chairView = _home.View(chair.Uid), deskView = _home.View(desk.Uid);
            if (chairView == null || deskView == null) return null; // not in the world yet: judged once it is
            Physics.SyncTransforms();
            Vector3 seat = new Vector3(chair.X, chair.Y, chair.Z);
            Vector3 fromDesk = seat - new Vector3(desk.X, desk.Y, desk.Z);
            fromDesk.y = 0f;
            if (fromDesk.sqrMagnitude < 1e-4f) fromDesk = Quaternion.Euler(0f, desk.Yaw, 0f) * Vector3.back;
            fromDesk.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fromDesk);
            // Behind the chair first, then either side of it.
            foreach (Vector3 offset in new[] { fromDesk * 0.6f, side * 0.65f, -side * 0.65f })
            {
                Vector3 stand = seat + offset;
                if (Blocked(stand, chairView, deskView)) continue;
                if (Physics.Linecast(stand + Vector3.up, seat + Vector3.up, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !Ignorable(hit.collider, chairView, deskView))
                    continue;
                return null;
            }
            return "Chair access blocked.";
        }

        private static bool Blocked(Vector3 stand, ItemView chair, ItemView desk)
        {
            foreach (Collider c in Physics.OverlapCapsule(stand + Vector3.up * 0.35f, stand + Vector3.up * 1.5f, 0.24f, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignorable(c, chair, desk)) return true;
            // Something to stand on (not off the edge of the floor).
            return !Physics.Raycast(stand + Vector3.up * 0.5f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static bool Ignorable(Collider c, ItemView chair, ItemView desk) =>
            c is CharacterController || c is TerrainCollider || c.transform.IsChildOf(chair.transform) || c.transform.IsChildOf(desk.transform)
            || c.gameObject.layer == CityLayers.Pedestrian || c.GetComponentInParent<FundPerson>() != null;
    }
}
