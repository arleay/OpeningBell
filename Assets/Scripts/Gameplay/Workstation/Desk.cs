using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// A place to trade from other than the home desk (e.g. the leased office). Its monitor shows the same
    /// terminal image; sitting here uses its own camera pose and stand-up point.
    /// </summary>
    public sealed class Desk : MonoBehaviour
    {
        [SerializeField] private WorkstationController workstation;
        [SerializeField] private Transform seatView;
        [SerializeField] private Transform standPoint;
        [SerializeField] private Renderer screen;

        public Transform SeatView => seatView;
        public Transform StandPoint => standPoint;

        /// <summary>For desks built in code (city generator).</summary>
        public void Configure(WorkstationController owner, Transform view, Transform stand, Renderer monitor)
        {
            workstation = owner;
            seatView = view;
            standPoint = stand;
            screen = monitor;
        }

        private void Start() => workstation.ShowTerminalOn(screen);
    }
}
