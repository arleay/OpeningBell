using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// Makes purchases physical (spec §25): each apartment slot has a basic and an upgraded object, and the
    /// upgraded one appears once the matching store item is owned. Also re-targets the terminal image when the
    /// monitor changes.
    /// </summary>
    public sealed class EquipmentPresenter : MonoBehaviour
    {
        [Serializable]
        public sealed class SlotVisual
        {
            public string Slot = "";
            [Tooltip("Shown until upgraded. May be empty (e.g. a plant slot that starts bare).")]
            public GameObject Basic;
            public GameObject Upgraded;
        }

        [SerializeField] private GameBootstrap game;
        [SerializeField] private WorkstationController workstation;
        [SerializeField] private List<SlotVisual> slots = new List<SlotVisual>();

        private void Start()
        {
            game.Economy.ItemPurchased += _ => Apply();
            Apply();
        }

        private void Apply()
        {
            foreach (SlotVisual slot in slots)
            {
                bool upgraded = game.Economy.HasUpgrade(slot.Slot);
                if (slot.Basic != null) slot.Basic.SetActive(!upgraded);
                if (slot.Upgraded != null) slot.Upgraded.SetActive(upgraded);

                GameObject active = upgraded ? slot.Upgraded : slot.Basic;
                Transform screen = active != null ? active.transform.Find("Screen") : null;
                if (screen != null) workstation.SetScreen(screen.GetComponent<Renderer>());
            }
        }
    }
}
