using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The two-storey walk-up around the player's existing apartment room (which stays scene-authored at the
    /// origin): a hallway along its south side, the neighbour's door, the front door onto Maple Street and the
    /// upper floor as an outside-only volume.
    /// </summary>
    public static class ApartmentBuilding
    {
        public static void Build(CityContext c)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Apartment building");
            Rect f = CityPlan.ApartmentBuilding; // x -3.3..12.3, z -4.8..2.8
            Material brick = c.P.Lit(new Color(0.5f, 0.26f, 0.2f), 0.05f);
            Material plaster = c.P.Lit(new Color(0.8f, 0.77f, 0.7f), 0.05f);
            Material carpet = c.P.Lit(new Color(0.35f, 0.3f, 0.34f), 0.02f);
            Material ceiling = c.P.Lit(new Color(0.9f, 0.89f, 0.86f));
            Material wood = c.P.Lit(new Color(0.36f, 0.24f, 0.16f), 0.25f);
            const float wallTop = 3f, t = 0.2f;

            // Outer walls (the apartment's own west/north walls already enclose its side; these wrap the building).
            k.WallX(root, "Front wall", f.xMin, f.xMax, f.yMin + t / 2f, 0f, wallTop, t, brick,
                Opening.Door(6f, 1.1f, 0f, 2.25f), new Opening(0.5f, 1.3f, 1f, 2.2f), new Opening(10f, 1.3f, 1f, 2.2f));
            k.WallX(root, "Back wall", f.xMin, f.xMax, f.yMax - t / 2f, 0f, wallTop, t, brick);
            k.WallZ(root, "West wall", f.yMin, f.yMax, f.xMin + t / 2f, 0f, wallTop, t, brick);
            k.WallZ(root, "East wall", f.yMin, f.yMax, f.xMax - t / 2f, 0f, wallTop, t, brick);
            foreach (float x in new[] { 0.5f, 10f })
                k.Pane(root, "Hall window", new Vector3(x - 0.65f, 1f, f.yMin + 0.08f), new Vector3(x + 0.65f, 2.2f, f.yMin + 0.12f), new Color(0.6f, 0.72f, 0.8f, 0.3f));
            // The apartment's window, seen from outside.
            k.Span(root, "Apartment window", new Vector3(f.xMin - 0.02f, 0.95f, -1f), new Vector3(f.xMin, 2.05f, 0.6f), c.P.Lit(new Color(0.15f, 0.18f, 0.22f), 0.8f), collider: false);

            // Hallway between the units and the front wall.
            k.Span(root, "Hall floor", new Vector3(-3.1f, -0.1f, -4.6f), new Vector3(12.1f, 0f, -2.6f), carpet);
            k.Span(root, "Hall ceiling", new Vector3(-3.1f, 2.7f, -4.6f), new Vector3(12.1f, 2.8f, -2.6f), ceiling, collider: false);
            k.WallX(root, "1B wall", 3.1f, 12.1f, -2.55f, 0f, 2.8f, 0.1f, plaster, Opening.Door(8f, 0.95f));
            k.Span(root, "Floor 2 slab", new Vector3(f.xMin, 2.8f, f.yMin), new Vector3(f.xMax, 3f, f.yMax), plaster, collider: false);
            k.Span(root, "1B floor", new Vector3(3.1f, -0.1f, -2.5f), new Vector3(12.1f, 0f, 2.6f), carpet, collider: false);
            c.PointLight(root, new Vector3(4.5f, 2.5f, -3.6f), 9f, 0.9f, new Color(1f, 0.9f, 0.75f));
            k.Box(root, "Hall lamp", new Vector3(4.5f, 2.66f, -3.6f), new Vector3(0.4f, 0.06f, 0.4f), c.P.Unlit(new Color(1f, 0.95f, 0.85f)), collider: false);

            // Mailboxes by the front door.
            Transform mail = Kit.Group(root, "Mailboxes", new Vector3(11.9f, 0f, -3.6f), -90f);
            k.Box(mail, "Panel", new Vector3(0f, 1.3f, 0f), new Vector3(0.9f, 0.6f, 0.2f), c.P.Lit(new Color(0.55f, 0.52f, 0.45f), 0.6f), collider: false);
            k.Text(mail, "1A  1B  2A  2B", new Vector3(0f, 1.5f, -0.11f), 0f, 0.05f, new Color(0.15f, 0.15f, 0.15f));

            // Upper floor (outside only) and roof.
            c.Kit.Facade(root, "Upper floor", new Vector3(f.xMin, wallTop, f.yMin), new Vector3(f.xMax, 6.4f, f.yMax), c.P.Facade(FacadeStyle.Brick, false),
                c.P.Lit(new Color(0.24f, 0.24f, 0.25f)));

            // Entrance: stoop, lamp, number.
            k.Span(root, "Stoop", new Vector3(5f, -0.05f, f.yMin - 0.9f), new Vector3(7f, 0.02f, f.yMin), c.P.Lit(new Color(0.6f, 0.59f, 0.56f)));
            k.Box(root, "Canopy", new Vector3(6f, 2.55f, f.yMin - 0.45f), new Vector3(1.8f, 0.08f, 0.9f), c.P.Lit(new Color(0.2f, 0.22f, 0.25f)), collider: false);
            k.Box(root, "Entrance lamp", new Vector3(6f, 2.45f, f.yMin - 0.45f), new Vector3(0.25f, 0.08f, 0.25f),
                c.P.Lamp(new Color(0.6f, 0.6f, 0.55f), new Color(1f, 0.88f, 0.62f)), collider: false);
            k.Text(root, "118", new Vector3(7.1f, 2.4f, f.yMin - 0.01f), 0f, 0.22f, new Color(0.92f, 0.9f, 0.84f));

            Transform dyn = Kit.Group(c.Dynamic, "Apartment building doors");
            c.SwingDoor(dyn, "front door", new Vector3(5.45f, 0f, f.yMin + 0.1f), 1.1f, 2.22f, wood);
            Door neighbour = c.SwingDoor(dyn, "apartment 1B", new Vector3(7.525f, 0f, -2.55f), 0.95f, 2.1f, wood);
            neighbour.LockReason = () => "locked (your neighbour's)";
            k.Text(root, "1B", new Vector3(8f, 2.3f, -2.62f), 0f, 0.12f, new Color(0.85f, 0.75f, 0.5f));
            k.Text(root, "1A", new Vector3(-1.8f, 2.3f, -2.62f), 0f, 0.12f, new Color(0.85f, 0.75f, 0.5f));

            c.Place(new Vector3(6f, 0f, f.yMin - 1.1f), PlaceKind.Door, "118 Maple");
            c.Anchor("apartment_door_hall", new Vector3(-1.8f, 0f, -3.7f));
            c.Anchor("apartment_front_in", new Vector3(6f, 0f, -3.7f));
            c.Anchor("apartment_front_out", new Vector3(6f, 0f, -6.4f));
        }
    }
}
