using System.Collections;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>
    /// Phase 4 acceptance: walk to the desk, sit, trade, stand up, walk away. Driven by simulated keyboard
    /// and mouse devices through the real Input System actions.
    /// </summary>
    public class ApartmentTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Player_WalksToDesk_SitsTrades_StandsAndWalksAway()
        {
            UseSimulatedInput();
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            var player = Find<FirstPersonController>();
            var interactor = Find<PlayerInteractor>();
            var workstation = Find<WorkstationController>();
            var hud = Find<InteractionHud>();
            Camera camera = player.GetComponentInChildren<Camera>();

            Assert.AreEqual(WorkstationState.Standing, workstation.State);
            Assert.IsFalse(terminal.IsOnScreen, "terminal is on the in-world monitor while standing");
            Vector3 start = player.transform.position;
            Assert.Less(Vector3.Distance(start, new Vector3(0.8f, start.y, -1.6f)), 0.05f, "nothing shoves the player at load");

            // Look slightly down and walk toward the desk until the chair or computer is in focus.
            MoveMouse(new Vector2(0f, -160f));
            yield return null;
            HoldKeys(Key.W);
            // Generous: the first frames compile the city's shaders, and hitches cap deltaTime.
            yield return WaitUntil(() => interactor.Current is SeatInteractable, 20f, "workstation in focus");
            HoldKeys();
            yield return null;
            Assert.Greater(Vector3.Distance(start, player.transform.position), 0.5f, "player walked");
            StringAssert.StartsWith("[E]", hud.PromptText);
            yield return CaptureCamera(camera, "apartment-standing.png");

            // Sit down.
            yield return TapKey(Key.E);
            yield return WaitUntil(() => workstation.State == WorkstationState.Seated, 5f, "seated");
            Assert.IsTrue(terminal.IsOnScreen, "terminal full-screen while seated");
            Assert.IsTrue(game.IsAtWorkstation);
            Assert.IsFalse(player.ControlEnabled, "walking disabled while seated");

            // Trade: marketable limit buy (market orders are not accepted pre-market).
            RenderTerminalOffscreen(terminal);
            yield return null;
            VisualElement root = terminal.Root;
            Click(root.Q("watch-APEX"));
            Press(root.Q<Button>("type-limit"));
            root.Q<TextField>("qty").value = "50";
            game.Market.TryGetQuote("APEX", out Quote quote);
            root.Q<TextField>("limit-price").value = Fmt.Price(quote.Ask);
            Press(root.Q<Button>("submit-order"));
            Assert.AreEqual(50, game.Account.Portfolio.QuantityOf("APEX"), "bought through the terminal");
            terminal.ShowOnScreen(true); // undo the offscreen test target before standing up

            // Stand up with Esc, then walk away.
            yield return TapKey(Key.Escape);
            yield return WaitUntil(() => workstation.State == WorkstationState.Standing, 5f, "standing");
            Assert.IsFalse(terminal.IsOnScreen, "terminal back on the monitor");
            Assert.IsFalse(game.IsAtWorkstation);
            Assert.IsTrue(player.ControlEnabled);

            // Wait on distance, not wall time: a frame hitch (first-time shader compile) caps deltaTime.
            Vector3 stood = player.transform.position;
            HoldKeys(Key.S);
            yield return WaitUntil(() => Vector3.Distance(stood, player.transform.position) > 1f, 5f, "walked away from the desk");
            HoldKeys();
            Assert.AreEqual(50, game.Account.Portfolio.QuantityOf("APEX"), "position persists away from the desk");
            Assert.AreNotEqual(MarketSession.Closed, game.Market.Session);
        }
    }
}
