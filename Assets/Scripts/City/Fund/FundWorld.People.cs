using System.Collections.Generic;
using OpeningBell.Fund;
using OpeningBell.Home;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.AI;

namespace OpeningBell.City
{
    /// <summary>
    /// The fund's people on Level 26 (FUND_SPEC §14, §18–19): one body per employee, following what the simulation
    /// says they're doing. They walk the floor on a navigation mesh baked at runtime from the office's own colliders
    /// (rebuilt when furniture moves), ride the residents' lift, sit at their workstations, queue for the coffee
    /// machine, wait in reception when there's no desk, and walk out when they leave. [E] on one opens their panel.
    /// </summary>
    public sealed partial class FundWorld
    {
        private readonly Dictionary<long, FundPerson> _people = new Dictionary<long, FundPerson>();
        private Transform _peopleRoot;
        private NavMeshData _nav;
        private NavMeshDataInstance _navInstance;
        private AsyncOperation _navBuild;
        private int _navBelongings = -1;
        private float _navDueAt = 0.01f;
        private float _nextSync;
        private readonly List<Door> _doors = new List<Door>();
        private EmployeeWindow _window;
        private CasinoControls _controls;
        private AudioSource _ambience;

        /// <summary>The navigation mesh has been baked at least once (people walk on it; before that they're placed).</summary>
        internal bool NavReady { get; private set; }
        internal HomeWorld Home => _home;
        internal IReadOnlyList<Door> Doors => _doors;
        internal int BubblesShown { get; set; }

        /// <summary>Is the player where they could see the office floor (on it, or in the lift lobby)?</summary>
        internal bool Watched
        {
            get
            {
                if (HarborviewOffice.Frame == null || _c.Player == null) return false;
                Vector3 p = _c.Player.position, o = HarborviewOffice.World(0f, 0f);
                return Mathf.Abs(p.y - o.y) < 3.5f && (new Vector2(p.x - o.x, p.z - o.z)).sqrMagnitude < 26f * 26f;
            }
        }

        private void ConfigurePeople()
        {
            _peopleRoot = Kit.Group(transform, "Office people");
            // The floor's own sound: a low murmur of voices and keyboards that grows with the number of people in.
            var sound = new GameObject("Office ambience");
            sound.transform.SetParent(transform, false);
            sound.transform.position = HarborviewOffice.World(0f, -2f) + Vector3.up * 2f;
            _ambience = sound.AddComponent<AudioSource>();
            _ambience.clip = CasinoArt.Murmur();
            _ambience.loop = true;
            _ambience.spatialBlend = 0.8f;
            _ambience.minDistance = 6f;
            _ambience.maxDistance = 24f;
            _ambience.rolloffMode = AudioRolloffMode.Linear;
            _ambience.volume = 0f;
            _controls = new CasinoControls(_c.Player);
            // The office's doors: automatic ones open for staff (Door.Walkers); swing doors (restrooms, meeting room)
            // are pushed open as they reach them. They're left out of the navigation mesh, closed or not.
            Vector3 o = HarborviewOffice.World(0f, 0f);
            foreach (Door d in FindObjectsByType<Door>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Vector3 p = d.transform.position;
                if (Mathf.Abs(p.y - o.y) < 3f && Mathf.Abs(p.x - o.x) < 16f && Mathf.Abs(p.z - o.z) < 14f) _doors.Add(d);
            }
        }

        private void OnDestroy()
        {
            if (_navInstance.valid) _navInstance.Remove();
            foreach (FundPerson p in _people.Values) if (p != null) Door.Walkers.Remove(p.transform);
        }

        private void UpdatePeople()
        {
            if (HarborviewOffice.Frame == null) return;
            UpdateParking();
            if (!Fund.Exists)
            {
                if (_people.Count > 0) Clear();
                return;
            }
            NavUpkeep();
            if (Time.time >= _nextSync)
            {
                _nextSync = Time.time + 0.5f;
                Sync();
                SeatsForPlayer();
                Ambience();
            }
            if (_window != null && _window.IsOpen)
            {
                _window.Tick();
                if (_controls.BackPressed) _window.Close();
            }
        }

        private void Clear()
        {
            foreach (FundPerson p in _people.Values)
                if (p != null)
                {
                    Door.Walkers.Remove(p.transform);
                    Destroy(p.gameObject);
                }
            _people.Clear();
        }

        /// <summary>A body for everyone on the payroll, and for the just-departed until they've walked out.</summary>
        private void Sync()
        {
            int index = 0;
            foreach (Employee e in Fund.Employees)
            {
                _people.TryGetValue(e.Id, out FundPerson p);
                if (e.Former && (p == null || p.Gone))
                {
                    if (p != null) Remove(e.Id);
                    continue;
                }
                if (p == null)
                {
                    p = FundPerson.Create(this, e, _peopleRoot);
                    _people[e.Id] = p;
                }
                p.Index = index++;
            }
            var stale = new List<long>();
            foreach (var kv in _people) if (Fund.Find(kv.Key) == null) stale.Add(kv.Key);
            foreach (long id in stale) Remove(id);
        }

        private void Remove(long id)
        {
            if (!_people.TryGetValue(id, out FundPerson p)) return;
            _people.Remove(id);
            if (p == null) return;
            Door.Walkers.Remove(p.transform);
            Destroy(p.gameObject);
        }

        /// <summary>
        /// A desk someone is working at isn't free for the player to sit at: its "Trade at this desk" seat goes away
        /// while they're in the chair.
        /// </summary>
        private void SeatsForPlayer()
        {
            foreach (Workstation w in Fund.Stations)
            {
                ItemView v = _home.View(w.Desk);
                Transform seat = v != null ? v.transform.Find("Trading seat") : null;
                if (seat == null) continue;
                bool taken = false;
                foreach (FundPerson p in _people.Values) if (p != null && p.SeatedAt == w.Desk) taken = true;
                if (seat.gameObject.activeSelf == taken) seat.gameObject.SetActive(!taken);
            }
        }

        private void Ambience()
        {
            int shown = 0;
            foreach (FundPerson p in _people.Values) if (p != null && p.Shown) shown++;
            float want = Watched && shown > 0 ? 0.05f + 0.1f * Mathf.Min(1f, shown / 8f) : 0f;
            _ambience.volume = want;
            if (want > 0f && !_ambience.isPlaying) _ambience.Play();
            else if (want == 0f && _ambience.isPlaying) _ambience.Stop();
        }

        // ------------------------------------------------------------------ the panel

        /// <summary>[E] on someone: their panel on the HUD. They carry on (seated, trading); game time keeps running.</summary>
        internal void OpenPanel(Employee e)
        {
            if (_window == null)
            {
                var terminal = FindAnyObjectByType<TradingTerminal>();
                _window = new EmployeeWindow(_c.Game, _c.Hud.Root, terminal != null ? terminal.Style : null);
                _window.Closed += () => _controls.Release();
            }
            if (!_window.IsOpen) _controls.Take();
            _window.Open(e);
        }

        // ------------------------------------------------------------------ navigation mesh

        /// <summary>Rebuilds the floor's navigation mesh a second after furniture changes (once the objects are in place).</summary>
        private void NavUpkeep()
        {
            if (_home.Belongings.Version != _navBelongings)
            {
                _navBelongings = _home.Belongings.Version;
                _navDueAt = Time.time + 1f;
            }
            if (_navBuild != null)
            {
                if (!_navBuild.isDone) return;
                _navBuild = null;
                NavReady = true;
            }
            if (_navDueAt > 0f && Time.time >= _navDueAt)
            {
                _navDueAt = 0f;
                BakeNav();
            }
        }

        private void BakeNav()
        {
            Vector3 o = HarborviewOffice.World(0f, 0f);
            // The floor from just below it to below the ceiling: the lift car at this stop is included, the penthouse isn't.
            var bounds = new Bounds(o + Vector3.up * 1.4f, new Vector3(34f, 3.6f, 30f));
            var found = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), found);
            var sources = new List<NavMeshBuildSource>(found.Count);
            foreach (NavMeshBuildSource s in found)
            {
                // Not the people (the player, staff), not triggers, not doors (they open).
                if (s.component is Collider col)
                {
                    if (col.isTrigger || col is CharacterController) continue;
                    if (col.gameObject.layer == CityLayers.Pedestrian || col.GetComponentInParent<Door>() != null || col.GetComponentInParent<FundPerson>() != null) continue;
                    if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                }
                sources.Add(s);
            }
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.26f;
            settings.agentHeight = 1.75f;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 30f;
            settings.minRegionArea = 0.5f;
            if (_nav == null)
            {
                _nav = new NavMeshData(settings.agentTypeID) { name = "Harborview Office navigation" };
                _navInstance = NavMesh.AddNavMeshData(_nav);
            }
            _navBuild = NavMeshBuilder.UpdateNavMeshDataAsync(_nav, settings, sources, bounds);
        }
    }
}
