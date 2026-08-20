using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.InputSystem.XR;

namespace Guandan.XR
{
    public sealed class GuandanXRBootstrap : MonoBehaviour
    {
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private Camera headCamera;
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;
        [SerializeField] private LineRenderer leftRay;
        [SerializeField] private LineRenderer rightRay;
        [SerializeField] private float orbitSpeed = 70f;
        [SerializeField] private float zoomSpeed = 2.5f;
        [SerializeField] private float desktopWalkSpeed = 0.72f;
        [SerializeField] private float maxDesktopPanRadius = 0.75f;
        [Tooltip("0 means no artificial boundary. A tabletop game must allow a player to walk around the real table.")]
        [SerializeField] private float maxXrOffsetRadius;

        [Header("Authoritative design player start")]
        [Tooltip("Scene-owned source of truth. Desktop, PICO startup and recenter all resolve this component.")]
        [SerializeField] private DesignPlayerStart designPlayerStart;
        [Tooltip("The one authored eye position used by both the desktop Game View and the XR head pose at scene entry/reset.")]
        [SerializeField] private Vector3 designEyePosition = new(0f, 3.12f, -4.65f);
        [Tooltip("The first gameplay landmark. The start yaw is derived from eye position to this world-space table point.")]
        [SerializeField] private Vector3 designGameplayFocus = new(0f, 2.14f, 0f);
        [Tooltip("Floor-to-eye distance used while restoring the XR rig before tracking has supplied a valid head pose.")]
        [SerializeField] private float designEyeHeight = 1.60f;

        [Header("World-space treasure viewpoint")]
        [SerializeField] private Vector3 treasureEyePosition = new(0f, 3.00f, 18.25f);
        [SerializeField] private Vector3 treasureGameplayFocus = new(0f, 0.90f, 25.60f);

        private Vector3 desktopBasePosition;
        private Vector3 desktopPosition;
        private float desktopYaw;
        private float desktopPitch;
        private float desktopFieldOfView = 60f;
        private bool leftTriggerHeld;
        private bool rightTriggerHeld;
        private bool xrStartApplied;
        private bool recenterHeld;

        /// <summary>Single source of truth for the authored entry composition.</summary>
        public Vector3 DesignEyePosition => StartPose.EyePosition;
        public Vector3 DesignGameplayFocus => StartPose.GameplayFocus;
        public float DesignEyeHeight => StartPose.EyeHeight;
        public bool UsesSceneDesignPlayerStart => designPlayerStart != null;

        private DesignPlayerStart StartPose
        {
            get
            {
                if (designPlayerStart == null) designPlayerStart = GetComponent<DesignPlayerStart>();
                return designPlayerStart;
            }
        }

        private Vector3 EntryEyePosition => StartPose != null ? StartPose.EyePosition : designEyePosition;
        private Vector3 EntryGameplayFocus => StartPose != null ? StartPose.GameplayFocus : designGameplayFocus;
        private float EntryEyeHeight => StartPose != null ? StartPose.EyeHeight : designEyeHeight;
        private Vector3 TreasureEye => StartPose != null ? StartPose.TreasureEyePosition : treasureEyePosition;
        private Vector3 TreasureFocus => StartPose != null ? StartPose.TreasureGameplayFocus : treasureGameplayFocus;

        /// <summary>Editor setup entry point: leaves the rig and visible ray objects in the Scene.</summary>
        public void BuildSceneRigForEditor()
        {
            EnsureRig();
            EnsureDesignPlayerStart();
        }

        private void Awake()
        {
            EnsureRig();
            EnsureDesignPlayerStart();
            TrySetFloorTracking();
            RecenterDesktop();
        }

        private void Update()
        {
            if (headCamera == null) return;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RecenterToDesignStart();
                if (Keyboard.current.leftArrowKey.isPressed) desktopYaw -= orbitSpeed * Time.deltaTime;
                if (Keyboard.current.rightArrowKey.isPressed) desktopYaw += orbitSpeed * Time.deltaTime;
                if (Keyboard.current.upArrowKey.isPressed) desktopPitch = Mathf.Clamp(desktopPitch - orbitSpeed * 0.55f * Time.deltaTime, -24f, 58f);
                if (Keyboard.current.downArrowKey.isPressed) desktopPitch = Mathf.Clamp(desktopPitch + orbitSpeed * 0.55f * Time.deltaTime, -24f, 58f);
                var move = Vector2.zero;
                if (Keyboard.current.wKey.isPressed) move.y += 1f;
                if (Keyboard.current.sKey.isPressed) move.y -= 1f;
                if (Keyboard.current.dKey.isPressed) move.x += 1f;
                if (Keyboard.current.aKey.isPressed) move.x -= 1f;
                if (move.sqrMagnitude > 0.01f)
                {
                    move.Normalize();
                    var heading = Quaternion.Euler(0f, desktopYaw, 0f);
                    desktopPosition += (heading * Vector3.forward * move.y + heading * Vector3.right * move.x)
                        * (desktopWalkSpeed * Time.deltaTime);
                    ConstrainDesktopPosition();
                }
                if (Keyboard.current.f1Key.wasPressedThisFrame) SendAction(Guandan.Game.GameAction.Hint);
                if (Keyboard.current.enterKey.wasPressedThisFrame) SendAction(Guandan.Game.GameAction.Play);
                if (Keyboard.current.spaceKey.wasPressedThisFrame) SendAction(Guandan.Game.GameAction.Pass);
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                var look = Mouse.current.delta.ReadValue();
                desktopYaw += look.x * 0.13f;
                desktopPitch = Mathf.Clamp(desktopPitch - look.y * 0.10f, -24f, 58f);
            }
            if (Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Mouse.current != null)
            {
                desktopFieldOfView = Mathf.Clamp(
                    desktopFieldOfView - Mouse.current.scroll.y.ReadValue() * zoomSpeed * 0.008f,
                    45f,
                    70f);
            }

            if (!XRSettings.enabled || !XRSettings.isDeviceActive)
            {
                ApplyDesktopCamera();
            }
            else
            {
                ClampXrOrigin();
                if (!xrStartApplied)
                {
                    // PXR may activate after Awake. Apply the same design pose only once
                    // so subsequent real 6DoF room movement is never overwritten.
                    PlaceXrHeadAt(EntryEyePosition, EntryGameplayFocus);
                    xrStartApplied = true;
                }
            }

            UpdateRay(leftController, leftRay);
            UpdateRay(rightController, rightRay);
            PollController(UnityEngine.XR.InputDeviceCharacteristics.Left, leftController, ref leftTriggerHeld, Guandan.Game.PointerSource.LeftController);
            PollController(UnityEngine.XR.InputDeviceCharacteristics.Right, rightController, ref rightTriggerHeld, Guandan.Game.PointerSource.RightController);
        }

        public void RecenterDesktop()
        {
            desktopYaw = DesignYaw(EntryEyePosition, EntryGameplayFocus);
            desktopPitch = DesignPitch(EntryEyePosition, EntryGameplayFocus);
            desktopFieldOfView = 60f;
            desktopBasePosition = EntryEyePosition;
            desktopPosition = desktopBasePosition;
            ApplyDesktopCamera();
        }

        /// <summary>
        /// Restores the sole authored entry pose. Desktop and XR deliberately share the
        /// same eye position and first-table landmark; XR root compensation preserves
        /// the headset's local tracking offset instead of placing the player under the table.
        /// </summary>
        public void RecenterToDesignStart()
        {
            if (XRSettings.enabled && XRSettings.isDeviceActive)
            {
                TrySetFloorTracking();
                PlaceXrHeadAt(EntryEyePosition, EntryGameplayFocus);
                xrStartApplied = true;
                return;
            }
            RecenterDesktop();
        }

        public void SetGameplayView(bool treasure)
        {
            if (headCamera == null) return;
            if (XRSettings.enabled && XRSettings.isDeviceActive)
            {
                if (treasure) PlaceXrHeadAt(TreasureEye, TreasureFocus);
                else PlaceXrHeadAt(EntryEyePosition, EntryGameplayFocus);
                return;
            }
            var eye = treasure ? TreasureEye : EntryEyePosition;
            var focus = treasure ? TreasureFocus : EntryGameplayFocus;
            desktopBasePosition = eye;
            desktopPosition = desktopBasePosition;
            desktopYaw = DesignYaw(eye, focus);
            desktopPitch = DesignPitch(eye, focus);
            desktopFieldOfView = treasure ? 58f : 60f;
            ApplyDesktopCamera();
        }

        private void EnsureRig()
        {
            if (xrOrigin == null)
            {
                xrOrigin = transform;
                xrOrigin.name = "XR Rig · Floor Tracking";
                var cameraGo = new GameObject("XR Head Camera");
                cameraGo.transform.SetParent(xrOrigin, false);
                cameraGo.transform.localPosition = Vector3.up * designEyeHeight;
                cameraGo.tag = "MainCamera";
                headCamera = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();
            }

            if (headCamera == null)
            {
                headCamera = xrOrigin.GetComponentInChildren<Camera>(true);
            }

            leftController = EnsureController("Left Controller · PICO");
            rightController = EnsureController("Right Controller · PICO");
            leftRay = EnsureRay("Left Controller Ray", leftController, new Color(0.23f, 0.72f, 0.63f));
            rightRay = EnsureRay("Right Controller Ray", rightController, new Color(0.83f, 0.25f, 0.14f));
        }

        private void EnsureDesignPlayerStart()
        {
            if (designPlayerStart == null) designPlayerStart = GetComponent<DesignPlayerStart>();
            if (designPlayerStart != null) return;
            // Migration path for older scenes only. Once serialized, the component is
            // the authority and must not be overwritten from this legacy fallback.
            designPlayerStart = gameObject.AddComponent<DesignPlayerStart>();
            designPlayerStart.ConfigureFallback(
                designEyePosition,
                designGameplayFocus,
                designEyeHeight,
                treasureEyePosition,
                treasureGameplayFocus);
        }

        private Transform EnsureController(string name)
        {
            if (xrOrigin == null) return null;
            var existing = xrOrigin.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(xrOrigin, false);
            go.AddComponent<TrackedPoseDriver>();
            return go.transform;
        }

        private LineRenderer EnsureRay(string name, Transform parent, Color color)
        {
            var existing = parent.Find(name);
            var line = existing != null ? existing.GetComponent<LineRenderer>() : null;
            if (line != null) return line;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.startWidth = 0.008f;
            line.endWidth = 0.002f;
            line.material = new Material(Shader.Find("Sprites/Default")) { color = color };
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward * 5f);
            return line;
        }

        private void UpdateRay(Transform controller, LineRenderer ray)
        {
            if (controller == null || ray == null) return;
            ray.enabled = XRSettings.enabled;
            ray.SetPosition(0, Vector3.zero);
            ray.SetPosition(1, Vector3.forward * 5f);
        }

        private void PollController(InputDeviceCharacteristics side, Transform controller, ref bool held, Guandan.Game.PointerSource source)
        {
            if (controller == null || !XRSettings.enabled) return;
            var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(side | InputDeviceCharacteristics.Controller, devices);
            if (devices.Count == 0) return;
            var device = devices[0];
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position)) controller.localPosition = position;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation)) controller.localRotation = rotation;
            if (side == InputDeviceCharacteristics.Right)
            {
                var recenterPressed = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out var menu) && menu;
                if (recenterPressed && !recenterHeld) RecenterToDesignStart();
                recenterHeld = recenterPressed;
            }
            var pressed = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var trigger) && trigger;
            if (pressed && !held && Physics.Raycast(controller.position, controller.forward, out var hit, 8f))
            {
                var interactable = hit.collider.GetComponentInParent<Guandan.Game.IWorldInteractable>();
                if (interactable != null)
                {
                    interactable.Interact(source);
                    if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                    {
                        device.SendHapticImpulse(0u, 0.32f, 0.08f);
                    }
                }
            }
            if (pressed && Guandan.Game.SocialProp.Held != null)
            {
                Guandan.Game.SocialProp.Held.DragTo(controller.position + controller.forward * 2.2f);
            }
            if (!pressed && held && Guandan.Game.SocialProp.Held != null)
            {
                if (Guandan.Game.SocialProp.Held.ReadyToThrow)
                    FindAvatarAlongRay(new Ray(controller.position, controller.forward), 8f)?.Interact(source);
                Guandan.Game.SocialProp.Held?.ReturnHome();
            }
            held = pressed;
        }

        private void ApplyDesktopCamera()
        {
            if (headCamera == null) return;
            headCamera.transform.SetPositionAndRotation(
                desktopPosition,
                Quaternion.Euler(desktopPitch, desktopYaw, 0f));
            headCamera.fieldOfView = desktopFieldOfView;
        }

        private void ConstrainDesktopPosition()
        {
            var offset = Vector3.ProjectOnPlane(desktopPosition - desktopBasePosition, Vector3.up);
            if (offset.magnitude > maxDesktopPanRadius) offset = offset.normalized * maxDesktopPanRadius;
            desktopPosition = desktopBasePosition + offset;
        }

        private static Guandan.Game.AvatarTarget FindAvatarAlongRay(Ray ray, float distance)
        {
            var hits = Physics.RaycastAll(ray, distance);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var avatar = hit.collider.GetComponentInParent<Guandan.Game.AvatarTarget>();
                if (avatar != null) return avatar;
            }
            return null;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ClampXrOrigin()
        {
            // No default clamp: physical movement around the table is a core XR feature.
            // If a venue enables a comfort boundary, apply it around the authored eye
            // landmark, never around world zero or a stale rig-local coordinate.
            if (xrOrigin == null || headCamera == null || maxXrOffsetRadius <= 0f) return;
            var offset = Vector3.ProjectOnPlane(headCamera.transform.position - EntryEyePosition, Vector3.up);
            if (offset.magnitude <= maxXrOffsetRadius) return;
            xrOrigin.position -= offset.normalized * (offset.magnitude - maxXrOffsetRadius);
        }

        private void PlaceXrHeadAt(Vector3 eyePosition, Vector3 focusPoint)
        {
            if (xrOrigin == null || headCamera == null) return;

            // Head local pose comes from the active HMD. Rotate the rig by the inverse
            // local yaw, then translate it so the tracked eye (not the camera root) lands
            // exactly on the authored eye marker.
            var localHeadPosition = headCamera.transform.localPosition;
            if (localHeadPosition.sqrMagnitude < 0.0001f)
                localHeadPosition = Vector3.up * EntryEyeHeight;
            var localHeadYaw = ExtractYaw(headCamera.transform.localRotation);
            var worldYaw = Quaternion.Euler(0f, DesignYaw(eyePosition, focusPoint), 0f) * Quaternion.Inverse(localHeadYaw);
            xrOrigin.SetPositionAndRotation(eyePosition - worldYaw * localHeadPosition, worldYaw);
        }

        private static Quaternion ExtractYaw(Quaternion rotation)
        {
            var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            return forward.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static float DesignYaw(Vector3 eye, Vector3 focus)
        {
            var flat = Vector3.ProjectOnPlane(focus - eye, Vector3.up);
            return flat.sqrMagnitude < 0.0001f ? 0f : Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
        }

        private static float DesignPitch(Vector3 eye, Vector3 focus)
        {
            var delta = focus - eye;
            var horizontal = new Vector2(delta.x, delta.z).magnitude;
            return horizontal < 0.0001f ? 0f : -Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg;
        }

        private void TrySetFloorTracking()
        {
            if (!Application.isPlaying) return;
            var subsystem = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystem);
            foreach (var input in subsystem)
            {
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            }
        }

        private void SendAction(Guandan.Game.GameAction action, bool showMessage = true)
        {
            var director = FindFirstObjectByType<Guandan.Game.GameDirector>();
            if (director == null) return;
            director.HandleAction(action);
        }
    }
}
