using System;
using System.Collections;
using OpeningBell.UI;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    public enum WorkstationState
    {
        Standing,
        SittingDown,
        Seated,
        StandingUp,
    }

    /// <summary>
    /// Sit → camera glides to the monitor → terminal goes full-screen with a free cursor.
    /// Leave (Esc) → terminal returns to the in-world monitor → camera glides back → player stands behind the chair.
    /// </summary>
    public sealed class WorkstationController : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private TradingTerminal terminal;
        [SerializeField] private GameInput input;
        [SerializeField] private FirstPersonController player;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private Camera viewCamera;
        [Tooltip("Camera pose while seated, framing the monitor.")]
        [SerializeField] private Transform seatView;
        [Tooltip("Where the player stands after getting up (position + yaw).")]
        [SerializeField] private Transform standPoint;
        [SerializeField] private Renderer screen;
        [SerializeField] private float transitionSeconds = 0.6f;

        public WorkstationState State { get; private set; } = WorkstationState.Standing;
        public event Action<WorkstationState> StateChanged;

        /// <summary>Where the player is (or last was) seated; null = the home desk.</summary>
        public Desk CurrentDesk { get; private set; }

        private Transform SeatView => CurrentDesk != null ? CurrentDesk.SeatView : seatView;
        private Transform StandPoint => CurrentDesk != null ? CurrentDesk.StandPoint : standPoint;

        private void Start()
        {
            SetScreen(screen);
            terminal.ShowOnScreen(false);
        }

        /// <summary>Points the home desk's terminal image at a monitor mesh (called again when the monitor is upgraded).</summary>
        public void SetScreen(Renderer target)
        {
            screen = target;
            ShowTerminalOn(target);
        }

        /// <summary>Any monitor can show the terminal: they all sample the same render texture.</summary>
        public void ShowTerminalOn(Renderer target)
        {
            // The screen material is black while "off"; Unlit multiplies the texture by its colour.
            Material screenMaterial = target.material;
            screenMaterial.mainTexture = terminal.WorldTexture;
            screenMaterial.color = Color.white;
        }

        private void Update()
        {
            if (State == WorkstationState.Seated && input.Leave.WasPressedThisFrame()) StandUp();
        }

        /// <summary>Sits at <paramref name="desk"/>, or at the home desk when null.</summary>
        public void SitDown(Desk desk = null)
        {
            if (State != WorkstationState.Standing) return;
            CurrentDesk = desk;
            StartCoroutine(SitRoutine());
        }

        public void StandUp()
        {
            if (State == WorkstationState.Seated) StartCoroutine(StandRoutine());
        }

        private IEnumerator SitRoutine()
        {
            SetState(WorkstationState.SittingDown);
            player.ControlEnabled = false;
            interactor.enabled = false;
            input.UseWorkstationControls();

            Transform cam = viewCamera.transform;
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            Transform view = SeatView;
            yield return Glide(k => cam.SetPositionAndRotation(
                Vector3.Lerp(fromPos, view.position, k), Quaternion.Slerp(fromRot, view.rotation, k)));

            terminal.ShowOnScreen(true);
            game.IsAtWorkstation = true;
            SetState(WorkstationState.Seated);
        }

        private IEnumerator StandRoutine()
        {
            SetState(WorkstationState.StandingUp);
            terminal.ShowOnScreen(false);
            game.IsAtWorkstation = false;

            // Move the body first, then pin the camera back at the seat view so the move is invisible,
            // and glide it home to the head.
            Transform cam = viewCamera.transform;
            player.PlaceAt(StandPoint.position, StandPoint.eulerAngles.y);
            cam.SetPositionAndRotation(SeatView.position, SeatView.rotation);
            Vector3 fromPos = cam.localPosition;
            Quaternion fromRot = cam.localRotation;
            yield return Glide(k =>
            {
                cam.localPosition = Vector3.Lerp(fromPos, Vector3.zero, k);
                cam.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, k);
            });

            input.UsePlayerControls();
            interactor.enabled = true;
            player.ControlEnabled = true;
            SetState(WorkstationState.Standing);
        }

        private IEnumerator Glide(Action<float> apply)
        {
            for (float t = 0f; t < transitionSeconds; t += Time.unscaledDeltaTime)
            {
                apply(Mathf.SmoothStep(0f, 1f, t / transitionSeconds));
                yield return null;
            }
            apply(1f);
        }

        private void SetState(WorkstationState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
