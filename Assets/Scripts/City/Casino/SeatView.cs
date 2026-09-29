using System;
using System.Collections;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Sitting down at a casino game (CASINO_SPEC §79), shared by every table and machine: the body stands by the
    /// seat, the view glides to a pose over the game, movement is off and the cursor free; getting up glides back.
    /// Like the workstation, but for any pose the game asks for. Reduced motion (settings) shortens the glide.
    /// </summary>
    public sealed class SeatView
    {
        private const float GlideSeconds = 0.6f;
        private readonly CasinoControls _controls;
        private Camera _camera;
        private Vector3 _camLocalPos;
        private Quaternion _camLocalRot;

        public bool Seated { get; private set; }
        public bool Gliding { get; private set; }
        public bool BackPressed => _controls.BackPressed;
        public FirstPersonController Player => _controls.Player;

        public SeatView(Transform player) => _controls = new CasinoControls(player);

        private static float Glide => GameSettings.ReduceMotion ? 0.12f : GlideSeconds;

        /// <summary>Sits: body at <paramref name="stand"/> facing <paramref name="yaw"/>, view to <paramref name="eye"/> looking at <paramref name="focus"/>.</summary>
        public void Sit(MonoBehaviour host, Vector3 stand, float yaw, Vector3 eye, Vector3 focus)
        {
            if (Seated || Gliding) return;
            Seated = true;
            _camera = Camera.main;
            _controls.Take();
            _controls.Player?.PlaceAt(stand, yaw);
            if (_camera == null) return;
            _camLocalPos = _camera.transform.localPosition;
            _camLocalRot = _camera.transform.localRotation;
            host.StartCoroutine(In(eye, Quaternion.LookRotation(focus - eye)));
        }

        /// <summary>Moves the seated view (a different angle on the same game).</summary>
        public void Look(MonoBehaviour host, Vector3 eye, Vector3 focus)
        {
            if (!Seated || _camera == null || Gliding) return;
            host.StartCoroutine(In(eye, Quaternion.LookRotation(focus - eye)));
        }

        public void Stand(MonoBehaviour host, Action done = null)
        {
            if (!Seated || Gliding) return;
            host.StartCoroutine(Out(done));
        }

        private IEnumerator In(Vector3 pos, Quaternion rot)
        {
            Gliding = true;
            Transform cam = _camera.transform;
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            float seconds = Glide;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                cam.SetPositionAndRotation(Vector3.Lerp(fromPos, pos, k), Quaternion.Slerp(fromRot, rot, k));
                yield return null;
            }
            cam.SetPositionAndRotation(pos, rot);
            Gliding = false;
        }

        private IEnumerator Out(Action done)
        {
            Gliding = true;
            if (_camera != null)
            {
                Transform cam = _camera.transform;
                Vector3 fromPos = cam.localPosition;
                Quaternion fromRot = cam.localRotation;
                float seconds = Glide;
                for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                    cam.localPosition = Vector3.Lerp(fromPos, _camLocalPos, k);
                    cam.localRotation = Quaternion.Slerp(fromRot, _camLocalRot, k);
                    yield return null;
                }
                cam.localPosition = _camLocalPos;
                cam.localRotation = _camLocalRot;
            }
            Seated = false;
            Gliding = false;
            _controls.Release();
            done?.Invoke();
        }
    }
}
