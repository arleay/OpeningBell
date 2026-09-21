using System;
using System.Collections;
using System.IO;
using OpeningBell;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OpeningBell.Tests
{
    /// <summary>Helpers for tests that load Main and drive the real scene.</summary>
    public abstract class SceneTestBase
    {
        private PanelSettings _terminalPanel;
        private RenderTexture _terminalTarget;
        private string _saveDirectory;

        /// <summary>Every scene test starts a new game and never touches the player's real save slots.</summary>
        [SetUp]
        public void IsolateSaves()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "OpeningBellTests", Guid.NewGuid().ToString("N"));
            SaveSystem.DirectoryOverride = _saveDirectory;
        }

        [TearDown]
        public void RestoreSaveLocation()
        {
            // Disabled bootstraps skip their quit-time autosave, which would otherwise hit the real slot later.
            foreach (var game in Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None)) game.enabled = false;
            SaveSystem.DirectoryOverride = null;
            if (Directory.Exists(_saveDirectory)) Directory.Delete(_saveDirectory, true);
        }

        [TearDown]
        public void ReleaseTerminalTarget()
        {
            if (_terminalPanel != null && _terminalPanel.targetTexture == _terminalTarget) _terminalPanel.targetTexture = null;
            if (_terminalTarget != null) _terminalTarget.Release();
        }

        protected static IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            yield return null; // let Start run
        }

        protected static T Find<T>() where T : Object
        {
            var found = Object.FindAnyObjectByType<T>();
            Assert.NotNull(found, typeof(T).Name + " in scene");
            return found;
        }

        protected static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), "Timed out waiting for " + what);
        }

        protected static IEnumerator SitDown(WorkstationController workstation)
        {
            workstation.SitDown();
            yield return WaitUntil(() => workstation.State == WorkstationState.Seated, 5f, "seated");
        }

        /// <summary>
        /// Batchmode has no real screen, so lay the (on-screen) terminal out into a 1920×1080 texture. Needed for
        /// layout-dependent UI (ListView rows) and for screenshots. Cleared again in TearDown.
        /// </summary>
        protected void RenderTerminalOffscreen(TradingTerminal terminal)
        {
            _terminalPanel = terminal.GetComponent<UIDocument>().panelSettings;
            if (_terminalTarget == null) _terminalTarget = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            _terminalPanel.targetTexture = _terminalTarget;
        }

        protected void SaveTerminalScreenshot(string fileName) => SavePng(_terminalTarget, fileName);

        protected static IEnumerator CaptureCamera(Camera camera, string fileName)
        {
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            yield return null;
            yield return null;
            SavePng(target, fileName);
            camera.targetTexture = null;
            target.Release();
        }

        /// <summary>Activates a Button the way keyboard/gamepad submit does.</summary>
        protected static void Press(Button button)
        {
            Assert.NotNull(button, "button exists");
            Assert.IsTrue(button.enabledInHierarchy, $"{button.name} is enabled");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        protected static void Click(VisualElement element)
        {
            Assert.NotNull(element, "element exists");
            using (var e = ClickEvent.GetPooled())
            {
                e.target = element;
                element.SendEvent(e);
            }
        }

        private static void SavePng(RenderTexture source, string fileName)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, fileName), texture.EncodeToPNG());
            Object.Destroy(texture);
        }
    }
}
