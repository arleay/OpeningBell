using System;
using System.Collections.Generic;
using OpeningBell.Home;

namespace OpeningBell.Fund
{
    /// <summary>A desk in the office and what it takes to trade at it: found from the placed furniture, never assumed.</summary>
    public sealed class Workstation
    {
        public int Desk;
        public int Chair, Computer;
        public int Monitors, MonitorsOn;
        public bool Keyboard, Mouse, Laptop;
        public readonly List<string> Problems = new List<string>();
        /// <summary>0–1: chair, screens and computer quality (comfort, workflow). Never a win-rate bonus by itself.</summary>
        public double Quality;
        public long AssignedTo;
        public float X, Z, Yaw;

        public bool Valid => Problems.Count == 0;
        /// <summary>
        /// One short line for panels: the "X missing." problems fold into "Missing: x, y." so a bare desk reads as one
        /// clause instead of four sentences; the other problems follow as they are.
        /// </summary>
        public string Summary
        {
            get
            {
                if (Valid) return "Ready";
                var missing = new List<string>();
                var rest = new List<string>();
                foreach (string p in Problems)
                {
                    if (p.EndsWith(" missing.", StringComparison.Ordinal)) missing.Add(p.Substring(0, p.Length - " missing.".Length).ToLowerInvariant());
                    else rest.Add(p);
                }
                if (missing.Count > 0) rest.Insert(0, "Missing: " + string.Join(", ", missing) + ".");
                return string.Join(" ", rest);
            }
        }
    }

    /// <summary>
    /// Which desks in a property are complete workstations (FUND_SPEC §8): desk, a compatible chair in front of it, a
    /// computer (a tower beside it or on it, or a laptop on it), at least one powered screen on it (a monitor or the laptop's own), keyboard and mouse (unless a
    /// laptop), the office's power and network, and room to reach and sit. Distances are in the desk's own frame (its
    /// front faces -z, where the seat is).
    /// </summary>
    public static class WorkstationRules
    {
        public static bool IsWorkChair(HomeItem i) => i != null && (i.Id == "chair_office" || i.Id == "chair_ergo");

        /// <param name="access">Physical check from the world: why nobody can reach or sit at this seat, or null (fine).
        /// Null delegate = no world loaded (tests): assumed reachable.</param>
        public static List<Workstation> Find(Belongings belongings, string property, bool power, bool network,
            Func<Workstation, string> access = null)
        {
            var desks = new List<OwnedItem>();
            var chairs = new List<OwnedItem>();
            var towers = new List<OwnedItem>();
            foreach (OwnedItem i in belongings.Items)
            {
                if (i.State != ItemState.Placed || i.Property != property || i.Boxed) continue;
                HomeItem s = i.Item;
                if (s == null) continue;
                if (s.IsDesk) desks.Add(i);
                else if (IsWorkChair(s)) chairs.Add(i);
                else if (s.Id == "pc_tower") towers.Add(i);
            }
            desks.Sort((a, b) => a.Uid.CompareTo(b.Uid));

            var usedChairs = new HashSet<int>();
            var usedTowers = new HashSet<int>();
            var result = new List<Workstation>();
            foreach (OwnedItem d in desks)
            {
                HomeItem ds = d.Item;
                var w = new Workstation { Desk = d.Uid, X = d.X, Z = d.Z, Yaw = d.Yaw };
                // Things on the desk.
                bool pad = false;
                foreach (OwnedItem m in belongings.MountedOn(d.Uid))
                {
                    HomeItem ms = m.Item;
                    if (ms == null) continue;
                    if (ms.Id == "keyboard" || ms.Id == "keyboard_mech") w.Keyboard = true;
                    if (ms.Id == "mouse") w.Mouse = true;
                    if (ms.Id == "laptop") { w.Laptop = true; pad = true; }
                }
                w.Monitors = belongings.MonitorsOn(d);
                w.MonitorsOn = CountPowered(belongings, d);

                // The chair: the nearest free work chair in the seat zone in front of the desk.
                OwnedItem chair = null;
                float best = float.MaxValue;
                foreach (OwnedItem c in chairs)
                {
                    if (usedChairs.Contains(c.Uid)) continue;
                    Local(d, c.X, c.Z, out float lx, out float lz);
                    bool inZone = Math.Abs(lx) <= ds.Width / 2f + 0.25f && lz <= -ds.Depth / 2f + 0.25f && lz >= -ds.Depth / 2f - 1.2f;
                    float dist = lx * lx + (lz + ds.Depth / 2f + 0.5f) * (lz + ds.Depth / 2f + 0.5f);
                    if (inZone && dist < best) { best = dist; chair = c; }
                }
                if (chair != null) { usedChairs.Add(chair.Uid); w.Chair = chair.Uid; }

                // A tower near the desk (on the floor or on the top), unless a laptop's on it.
                if (!pad)
                {
                    OwnedItem tower = null;
                    best = float.MaxValue;
                    foreach (OwnedItem t in towers)
                    {
                        if (usedTowers.Contains(t.Uid)) continue;
                        float dx = t.X - d.X, dz = t.Z - d.Z;
                        float dist = dx * dx + dz * dz;
                        float reach = Math.Max(ds.Width, ds.Depth) / 2f + 0.9f;
                        if (dist <= reach * reach && dist < best) { best = dist; tower = t; }
                    }
                    if (tower != null) { usedTowers.Add(tower.Uid); w.Computer = tower.Uid; }
                }

                if (!pad && w.Computer == 0) w.Problems.Add("Computer missing.");
                if (w.Monitors == 0) w.Problems.Add("Monitor missing.");
                else if (w.MonitorsOn == 0 || !power) w.Problems.Add("Monitor not connected.");
                if (!pad && !w.Keyboard) w.Problems.Add("Keyboard missing.");
                if (!pad && !w.Mouse) w.Problems.Add("Mouse missing.");
                if (w.Chair == 0) w.Problems.Add("No chair assigned.");
                if (!power) w.Problems.Add("No power.");
                if (!network) w.Problems.Add("No network: connectivity bill unpaid.");
                if (w.Chair != 0 && access != null)
                {
                    string blocked = access(w);
                    if (blocked != null) w.Problems.Add(blocked);
                }
                w.Quality = QualityOf(belongings, w, chair);
                result.Add(w);
            }
            return result;
        }

        private static int CountPowered(Belongings b, OwnedItem desk)
        {
            int n = 0;
            foreach (OwnedItem i in b.MountedOn(desk.Uid))
            {
                if (i.Item?.HasScreen == true && i.Power) n++;
                else if (i.Item?.IsArm == true) foreach (OwnedItem m in b.MountedOn(i.Uid)) if (m.Item?.IsMonitor == true && m.Power) n++;
            }
            return n;
        }

        /// <summary>Comfort and workflow: a better chair, more and bigger screens, a real computer.</summary>
        private static double QualityOf(Belongings b, Workstation w, OwnedItem chair)
        {
            double q = 0;
            if (chair != null) q += chair.ItemId == "chair_ergo" ? 0.35 : 0.18;
            q += Math.Min(4, w.MonitorsOn) * 0.07;
            if (w.Computer != 0) q += 0.15;
            else if (w.Laptop) q += 0.06;
            OwnedItem desk = b.Get(w.Desk);
            if (desk?.ItemId == "desk_trading" || desk?.ItemId == "desk_corner") q += 0.1;
            foreach (OwnedItem m in b.MountedOn(w.Desk)) if (m.ItemId == "keyboard_mech") q += 0.05;
            return Math.Min(1.0, q);
        }

        /// <summary>A point into a desk's frame (x right, z forward; the seat is on -z).</summary>
        private static void Local(OwnedItem desk, float x, float z, out float lx, out float lz)
        {
            double yaw = desk.Yaw * Math.PI / 180.0;
            float dx = x - desk.X, dz = z - desk.Z;
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            // Unity yaw rotates +z toward +x: local = R(-yaw) · world.
            lx = dx * c - dz * s;
            lz = dx * s + dz * c;
        }

        /// <summary>Where someone sits at a desk (world x/z and facing), for the world and the checks.</summary>
        public static (float X, float Z, float Yaw) SeatPose(OwnedItem desk)
        {
            HomeItem s = desk.Item;
            double yaw = desk.Yaw * Math.PI / 180.0;
            float back = -(s?.Depth ?? 0.75f) / 2f - 0.5f;
            return (desk.X + (float)Math.Sin(yaw) * back, desk.Z + (float)Math.Cos(yaw) * back, desk.Yaw);
        }
    }
}
