using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Furnishes the scene-authored apartment (1A) with Kenney Furniture Kit models. The authored primitives keep
    /// their colliders, interactables and upgrade groups: only their renderers are hidden, and each model goes
    /// inside the object it dresses, so <c>EquipmentPresenter</c>'s basic/upgraded toggles carry the models too.
    /// Adds what a lived-in studio needs around them: loveseat, coffee table, nightstands, bookcase, kitchen
    /// cabinets, a ceiling lamp and a coat rack. Without art the room stays as authored.
    /// </summary>
    public static class ApartmentInterior
    {
        // Heights things rest on, shared by both variants of a slot so the objects on top never float:
        private const float DeskTop = 0.77f, CounterTop = 0.9f, FridgeTop = 0.86f, NightstandTop = 0.5f;

        /// <param name="room">The apartment root; boxes below are in its space (floor y 0, inner walls x ±3, z ±2.5).</param>
        /// <param name="workstation">The desk root (same origin as the room).</param>
        public static void Dress(Kit k, Transform room, Transform workstation)
        {
            if (k.Art == null || k.Art.Model("bedSingle") == null) return;

            Transform bedBasic = Find(room, "Bed/Bed_Basic"), bedQueen = Find(room, "Bed/Bed_Queen");
            Transform kitchen = Find(room, "Kitchenette"), coffee = Find(room, "Kitchenette/CoffeeMaker");
            Transform counter = Find(room, "Kitchenette/Counter"), fridge = Find(room, "Kitchenette/Fridge");
            Transform rug = Find(room, "Rug"), plant = Find(room, "Plant"), plantSpot = Find(room, "PlantSpot");
            Transform table = Find(workstation, "FoldingTable"), lampShade = Find(workstation, "FoldingTable/DeskLampShade");
            Transform folding = Find(workstation, "FoldingTable/Frame_Folding"), solid = Find(workstation, "FoldingTable/Frame_Solid");
            Transform keyboard = Find(workstation, "FoldingTable/Keyboard"), mouse = Find(workstation, "FoldingTable/Mouse");
            Transform chairBasic = Find(workstation, "Chair/Chair_Basic"), chairErgo = Find(workstation, "Chair/Chair_Ergo");
            if (!bedBasic || !bedQueen || !kitchen || !coffee || !counter || !fridge || !rug || !plant || !plantSpot || !table
                || !lampShade || !folding || !solid || !keyboard || !mouse || !chairBasic || !chairErgo) return;

            // The room's own materials keep its palette on the kit shapes.
            Material glow = MaterialOf(lampShade), rugRed = MaterialOf(rug);
            Material blanket = MaterialOf(Find(bedBasic, "Blanket")), mesh = MaterialOf(Find(chairErgo, "Seat"));
            Material leaf = MaterialOf(Find(plant, "Leaves")), grey = MaterialOf(Find(folding, "Top")), plastic = MaterialOf(keyboard);
            Material keys = k.P.Lit(new Color(0.24f, 0.25f, 0.27f), 0.3f), white = k.P.Lit(new Color(0.9f, 0.89f, 0.86f));

            // Beds: headboards against the north wall. Each variant brings its own nightstand and lamp, beside it.
            Hide(bedBasic);
            Kit.Recolor(Put(k, room, bedBasic, "bedSingle", new Vector3(-2.97f, 0f, 0.23f), new Vector3(-1.8f, 0f, 2.47f)), "carpet", blanket);
            Nightstand(k, room, bedBasic, -1.5f);
            Hide(bedQueen);
            Kit.Recolor(Put(k, room, bedQueen, "bedDouble", new Vector3(-2.97f, 0f, 0.23f), new Vector3(-1.33f, 0f, 2.47f), stretch: true), "carpet", blanket);
            Nightstand(k, room, bedQueen, -1.03f);

            // Desk: both tops at DeskTop, where the monitors, keyboard and lamp stand (the cheap one in the folding
            // table's grey). The chairs face the desk (+z).
            Hide(folding);
            Kit.Recolor(Put(k, room, folding, "table", new Vector3(0.1f, 0f, 1.77f), new Vector3(1.5f, DeskTop, 2.47f), stretch: true), "wood", grey);
            Hide(solid);
            Put(k, room, solid, "desk", new Vector3(0f, 0f, 1.745f), new Vector3(1.6f, DeskTop, 2.495f), stretch: true);
            Hide(keyboard);
            // The kit's peripherals are pale grey, which the monitor's light turns into a glowing slab; keep them dark.
            GameObject kb = Put(k, room, table, "computerKeyboard", new Vector3(0.575f, DeskTop, 1.86f), new Vector3(1.025f, DeskTop, 2.04f), stretch: true);
            Kit.Recolor(Kit.Recolor(kb, "metalDark", plastic), "metalMedium", keys);
            Hide(mouse);
            Kit.Recolor(Put(k, room, table, "computerMouse", new Vector3(1.0975f, DeskTop, 1.895f), new Vector3(1.1625f, DeskTop, 2.005f)), "metalDark", plastic);
            // The desk lamp used to be a floating shade; now it has a base. The DeskLamp light sits inside the shade.
            Hide(lampShade);
            Kit.Recolor(Put(k, room, table, "lampRoundTable", new Vector3(0.25f, DeskTop, 2.27f), new Vector3(0.25f, DeskTop + 0.38f, 2.27f)), "lamp", glow);
            Hide(chairBasic);
            Put(k, room, chairBasic, "chair", new Vector3(0.57f, 0f, 1.22f), new Vector3(1.03f, 0f, 1.68f), 180f);
            Hide(chairErgo);
            Kit.Recolor(Put(k, room, chairErgo, "chairDesk", new Vector3(0.5f, 0f, 1.15f), new Vector3(1.1f, 0f, 1.75f), 180f), "carpet", mesh);

            // Kitchenette along the east wall, fronts facing west (yaw 90): sink and drawers where the counter was,
            // an under-counter fridge with a microwave on it, wall cabinets above, a bin in the corner.
            Hide(counter);
            Put(k, room, kitchen, "kitchenSink", new Vector3(2.4f, 0f, -1.9f), new Vector3(3f, 0.98f, -1.2f), 90f, stretch: true);
            Put(k, room, kitchen, "kitchenCabinetDrawer", new Vector3(2.4f, 0f, -1.2f), new Vector3(3f, CounterTop, -0.5f), 90f, stretch: true);
            Hide(fridge);
            Put(k, room, kitchen, "kitchenFridgeSmall", new Vector3(2.4f, 0f, -0.4f), new Vector3(3f, FridgeTop, 0.2f), 90f, stretch: true);
            Put(k, room, kitchen, "kitchenMicrowave", new Vector3(2.56f, FridgeTop, -0.33f), new Vector3(2.98f, FridgeTop, 0.13f), 90f);
            for (float z = -1.9f; z < -0.6f; z += 0.7f)
                k.Solid(Put(k, room, kitchen, "kitchenCabinetUpper", new Vector3(2.66f, 1.45f, z), new Vector3(3f, 2.15f, z + 0.7f), 90f, stretch: true));
            k.Solid(Put(k, room, kitchen, "trashcan", new Vector3(2.56f, 0f, -2.4f), new Vector3(2.88f, 0f, -2.08f)));
            // The drip machine is a scaled primitive and is itself the slot object, so its model must be its child:
            // move the scale into its collider first so the child isn't squashed.
            var coffeeBox = coffee.GetComponent<BoxCollider>();
            coffeeBox.size = Vector3.Scale(coffeeBox.size, coffee.localScale);
            coffeeBox.center = Vector3.Scale(coffeeBox.center, coffee.localScale);
            coffee.localScale = Vector3.one;
            Hide(coffee);
            // A fruit plate by the coffee maker, a soda on the desk.
            k.Prop(kitchen, "Plate", kitchen.InverseTransformPoint(room.TransformPoint(new Vector3(2.72f, CounterTop, -0.6f))), 0f, 0f, 0.24f);
            k.Prop(kitchen, "Apple", kitchen.InverseTransformPoint(room.TransformPoint(new Vector3(2.68f, CounterTop + 0.01f, -0.63f))), 0.08f);
            k.Prop(kitchen, "Orange", kitchen.InverseTransformPoint(room.TransformPoint(new Vector3(2.77f, CounterTop + 0.01f, -0.57f))), 0.075f);
            k.Prop(kitchen, "Banana", kitchen.InverseTransformPoint(room.TransformPoint(new Vector3(2.74f, CounterTop + 0.01f, -0.64f))), 0f, 70f, 0.18f);
            k.Prop(table, "Soda", table.InverseTransformPoint(room.TransformPoint(new Vector3(1.47f, DeskTop, 1.9f))), 0.16f);
            Put(k, room, coffee, "kitchenCoffeeMachine", new Vector3(2.72f, CounterTop, -0.85f), new Vector3(2.72f, CounterTop + 0.3f, -0.85f), 90f);

            // Living corner under the west window: a loveseat facing into the room (yaw 270), a coffee table and a
            // round rug in front of it. The plant moves to the nook west of the door to make room.
            k.Solid(Kit.Recolor(Put(k, room, room, "loungeSofa", new Vector3(-2.98f, 0f, -1.45f), new Vector3(-2.35f, 0f, 0f), 270f),
                "carpet", k.P.Lit(new Color(0.2f, 0.4f, 0.42f), 0.05f)));
            k.Solid(Put(k, room, room, "tableCoffee", new Vector3(-2f, 0f, -1.15f), new Vector3(-1.48f, 0.4f, -0.3f), 90f, stretch: true));
            Hide(rug);
            GameObject round = Put(k, room, room, "rugRound", new Vector3(-2.45f, 0f, -1.52f), new Vector3(-0.85f, 0f, 0.08f));
            Kit.Recolor(round, "carpet", rugRed);
            if (rugRed != null) Kit.Recolor(round, "carpetDarker", k.P.Lit(Color.Lerp(Color.black, rugRed.color, 0.7f), 0.02f));
            plant.localPosition = plantSpot.localPosition = new Vector3(-2.72f, 0f, -2.18f);
            Hide(plant);
            Kit.Recolor(Put(k, room, plant, "pottedPlant", new Vector3(-2.72f, 0f, -2.18f), new Vector3(-2.72f, 1.05f, -2.18f)), "plant", leaf);

            // Window trim: the daylight pane is a bare quad on the west wall (z -1..0.6, y 0.95..2.05).
            k.Span(room, "Window sill", new Vector3(-3f, 0.91f, -1.1f), new Vector3(-2.87f, 0.95f, 0.7f), white, collider: false);
            k.Span(room, "Window head", new Vector3(-3f, 2.05f, -1.06f), new Vector3(-2.96f, 2.11f, 0.66f), white, collider: false);
            k.Span(room, "Window jamb", new Vector3(-3f, 0.95f, -1.06f), new Vector3(-2.96f, 2.05f, -1f), white, collider: false);
            k.Span(room, "Window jamb", new Vector3(-3f, 0.95f, 0.6f), new Vector3(-2.96f, 2.05f, 0.66f), white, collider: false);
            k.Span(room, "Window mullion", new Vector3(-3f, 0.95f, -0.22f), new Vector3(-2.975f, 2.05f, -0.18f), white, collider: false);

            // Bookcase in the north-east corner (shelves at 0.24, 0.68 and 1.12 m, top 1.56 m at this size), stocked.
            k.Solid(Put(k, room, room, "bookcaseOpen", new Vector3(1.95f, 0f, 2.03f), new Vector3(2.75f, 0f, 2.49f)));
            Put(k, room, room, "books", new Vector3(2.02f, 0.68f, 2.2f), new Vector3(2.32f, 0.68f, 2.4f));
            Put(k, room, room, "books", new Vector3(2.38f, 1.12f, 2.2f), new Vector3(2.68f, 1.12f, 2.4f));
            Put(k, room, room, "radio", new Vector3(2.1f, 0.24f, 2.15f), new Vector3(2.5f, 0.24f, 2.45f));
            Kit.Recolor(Put(k, room, room, "plantSmall2", new Vector3(2.45f, 1.56f, 2.12f), new Vector3(2.7f, 1.56f, 2.4f)), "plant", leaf);
            k.Solid(Put(k, room, room, "coatRackStanding", new Vector3(-1f, 0f, -2.2f), new Vector3(-1f, 1.65f, -2.2f)));
            // A fixture for the ceiling light (the CeilingLight sits inside the shade; it casts no shadows).
            Kit.Recolor(Kit.Recolor(Put(k, room, room, "lampSquareCeiling", new Vector3(0f, 2.2f, 0f), new Vector3(0f, 2.7f, 0f)), "lamp", glow), "metal", white);
        }

        private static void Nightstand(Kit k, Transform room, Transform bed, float x)
        {
            k.Solid(Put(k, room, bed, "cabinetBedDrawerTable", new Vector3(x - 0.24f, 0f, 2.05f), new Vector3(x + 0.24f, NightstandTop, 2.47f), stretch: true));
            Put(k, room, bed, "lampRoundTable", new Vector3(x, NightstandTop, 2.26f), new Vector3(x, NightstandTop + 0.34f, 2.26f));
        }

        /// <summary>A model fitted into a box given by its corners in the room's space (see <see cref="Kit.Fit"/>).</summary>
        private static GameObject Put(Kit k, Transform room, Transform parent, string model, Vector3 min, Vector3 max, float yaw = 0f, bool stretch = false)
        {
            Vector3 bottom = room.TransformPoint(new Vector3((min.x + max.x) / 2f, min.y, (min.z + max.z) / 2f));
            return k.Fit(parent, model, parent.InverseTransformPoint(bottom), max - min, yaw, stretch);
        }

        /// <summary>Hides an authored primitive (and its children), keeping its colliders. Inactive variants included.</summary>
        private static void Hide(Transform t)
        {
            foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
        }

        /// <summary>The authored object at <paramref name="path"/>, or null with an error (tests fail on it) if it was renamed.</summary>
        private static Transform Find(Transform root, string path)
        {
            Transform t = root != null ? root.Find(path) : null;
            if (t == null) Debug.LogError($"ApartmentInterior: {path} not found under {(root != null ? root.name : "null")}");
            return t;
        }

        private static Material MaterialOf(Transform t) => t != null && t.TryGetComponent(out Renderer r) ? r.sharedMaterial : null;
    }
}
