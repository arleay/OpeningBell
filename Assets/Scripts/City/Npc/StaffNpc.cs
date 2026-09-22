using System;
using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Someone who works in a building (receptionist, barista, clerk): at their station during their shift, away
    /// on breaks and after hours, greets the player once per visit and has a line when talked to (spec §5, §73).
    /// While at the station they cycle through small activities (typing, phone, coffee).
    /// </summary>
    public sealed class StaffNpc : Interactable
    {
        private enum Where
        {
            AtStation,
            Leaving,
            Away,
            Arriving,
        }

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Transform _player;
        private NpcBody _body;
        private Collider _collider;
        private string _speaker;
        private WorkSchedule _schedule;
        private List<Vector3> _route; // station → … → back door (local to the parent)
        private float _stationYaw;
        private NpcPose[] _activities;
        private Func<string> _greeting, _talk;

        private Where _where;
        private int _waypoint;
        private bool _greeted;
        private NpcPose _activity;
        private float _nextActivity;
        private System.Random _rng;

        public string Speaker => _speaker;
        public bool AtStation => _where == Where.AtStation;

        public static StaffNpc Create(Kit kit, Transform parent, string speaker, int seed, Color outfit, WorkSchedule schedule,
            IReadOnlyList<Vector3> routeToBackDoor, float stationYaw, NpcPose[] activities, Func<string> greeting, Func<string> talk,
            GameBootstrap game, InteractionHud hud, Transform player)
        {
            NpcBody body = NpcBody.Create(kit, parent, speaker, seed, outfit);
            var npc = body.gameObject.AddComponent<StaffNpc>();
            var capsule = body.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.28f;
            npc._collider = capsule;
            npc._body = body;
            npc._speaker = speaker;
            npc._schedule = schedule;
            npc._route = new List<Vector3>(routeToBackDoor);
            npc._stationYaw = stationYaw;
            npc._activities = activities;
            npc._greeting = greeting;
            npc._talk = talk;
            npc._game = game;
            npc._hud = hud;
            npc._player = player;
            npc._rng = new System.Random(seed);
            npc.Place(schedule.OnDuty(game.Clock.Now) ? Where.AtStation : Where.Away);
            return npc;
        }

        public override string Prompt => "Talk to " + _speaker.ToLowerInvariant();
        public override bool CanInteract => base.CanInteract && _where == Where.AtStation;

        public override void Interact() => Say(_talk());

        public void Say(string line)
        {
            if (!string.IsNullOrEmpty(line)) _hud.ShowSubtitle(_speaker, line);
        }

        private void Update()
        {
            bool duty = _schedule.OnDuty(_game.Clock.Now);
            float playerDistance = Vector3.Distance(_player.position, transform.position);

            // Nobody watching (or after a skip): jump straight to where they should be.
            if (playerDistance > 30f)
            {
                if (duty && _where != Where.AtStation) Place(Where.AtStation);
                else if (!duty && _where != Where.Away) Place(Where.Away);
            }
            else if (duty && (_where == Where.Away || _where == Where.Leaving)) Begin(Where.Arriving);
            else if (!duty && (_where == Where.AtStation || _where == Where.Arriving)) Begin(Where.Leaving);

            switch (_where)
            {
                case Where.AtStation:
                    Fidget();
                    if (!_greeted && playerDistance < 4.5f)
                    {
                        _greeted = true;
                        Say(_greeting());
                    }
                    if (playerDistance > 12f) _greeted = false;
                    break;
                case Where.Leaving:
                case Where.Arriving:
                    WalkRoute();
                    break;
            }
        }

        private void Fidget()
        {
            if (Time.time >= _nextActivity)
            {
                _activity = _activities[_rng.Next(_activities.Length)];
                _nextActivity = Time.time + 8f + (float)_rng.NextDouble() * 14f;
            }
            _body.Animate(_activity, Time.time);
        }

        private void Begin(Where where)
        {
            if (_where == Where.Away) SetVisible(true);
            _where = where;
            // Leaving walks the route forward from wherever they are; arriving walks it back.
            _waypoint = Mathf.Clamp(_waypoint, 0, _route.Count - 1);
        }

        private void WalkRoute()
        {
            bool leaving = _where == Where.Leaving;
            int next = leaving ? _waypoint + 1 : _waypoint - 1;
            if (next < 0 || next >= _route.Count)
            {
                Place(leaving ? Where.Away : Where.AtStation);
                return;
            }
            Vector3 target = _route[next];
            Vector3 p = transform.localPosition;
            Vector3 step = Vector3.MoveTowards(p, target, 1.3f * Time.deltaTime);
            Vector3 dir = target - p;
            if (dir.sqrMagnitude > 1e-4f) transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(dir.normalized), 0.2f);
            transform.localPosition = step;
            _body.Animate(NpcPose.Walk, Time.time);
            if ((step - target).sqrMagnitude < 0.0004f) _waypoint = next;
        }

        private void Place(Where where)
        {
            _where = where;
            bool atStation = where == Where.AtStation;
            _waypoint = atStation ? 0 : _route.Count - 1;
            transform.localPosition = _route[_waypoint];
            transform.localRotation = Quaternion.Euler(0f, _stationYaw, 0f);
            SetVisible(where != Where.Away);
            if (atStation) _body.Animate(NpcPose.Stand, Time.time);
        }

        private void SetVisible(bool visible)
        {
            _body.transform.GetChild(0).gameObject.SetActive(visible);
            _collider.enabled = visible;
        }
    }
}
