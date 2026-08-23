using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR;
using UnityEngine.InputSystem.XR;
using ByteDance.PICO.XR;

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
        [Tooltip("0 means no artificial boundary.")]
        [SerializeField] private float maxXrOffsetRadius;

        // The authored table is tall enough that the previous eye point read as seated too
        // low in PICO Emulator.  Keep the player at a natural standing/sitting XR eye height.
        private static readonly Vector3 TableViewPosition = new(0f, 3.42f, -5.17f);
        private static readonly Vector3 TreasureViewPosition = new(0f, 2.70f, 18.25f);

        private Vector3 desktopBasePosition = TableViewPosition;
        private Vector3 desktopPosition = TableViewPosition;
        private float desktopYaw;
        private float desktopPitch = 16f;
        private float desktopFieldOfView = 60f;
        private bool leftTriggerHeld;
        private bool rightTriggerHeld;
        private bool leftHandPinchHeld;
        private bool rightHandPinchHeld;
        private bool loggedPicoHandAim;
        private bool loggedPicoHandDevice;
        private bool loggedPicoHandUnavailable;
        private bool picoHandRayAvailable;
        private float nextPointerDiagnosticTime;
        private TrackedPoseDriver headPoseDriver;
        private Vector3 xrEyePosition = TableViewPosition;

        public Camera HeadCamera => headCamera;
        public bool PicoHandRayAvailable => picoHandRayAvailable;

        /// <summary>Editor setup entry point: leaves the rig and visible ray objects in the Scene.</summary>
        public void BuildSceneRigForEditor()
        {
            EnsureRig();
        }

        private void Awake()
        {
            EnsureRig();
            TrySetFloorTracking();
        }

        private void Start()
        {
            // PXR becomes active after Awake on device/emulator.  Align once when its session
            // is ready; doing this per frame would fight normal head tracking.
            StartCoroutine(AlignStartViewWhenXrReady());
        }

        private IEnumerator AlignStartViewWhenXrReady()
        {
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!XRSettings.isDeviceActive && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!XRSettings.isDeviceActive) yield break;
            yield return new WaitForEndOfFrame();
            RecenterToDesignStart();
            Debug.Log("[Guandan] PXR 会话就绪：已校正地宫起始视点。");
        }

        private void Update()
        {
            if (headCamera == null) return;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RecenterDesktop();
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
                LockXrCameraPosition();
            }

            PollController(UnityEngine.XR.InputDeviceCharacteristics.Left, leftController, ref leftTriggerHeld, Guandan.Game.PointerSource.LeftController);
            PollController(UnityEngine.XR.InputDeviceCharacteristics.Right, rightController, ref rightTriggerHeld, Guandan.Game.PointerSource.RightController);
            // Draw after pose polling so the visible ray and the click ray use the exact same
            // controller sample instead of differing by one frame in the emulator.
            UpdateRay(leftController, leftRay);
            UpdateRay(rightController, rightRay);
            LogPointerDiagnostic();
            PollPicoHandPinch(HandType.HandLeft, ref leftHandPinchHeld, Guandan.Game.PointerSource.LeftController);
            PollPicoHandPinch(HandType.HandRight, ref rightHandPinchHeld, Guandan.Game.PointerSource.RightController);
            // PICO Emulator 0.13 can expose a valid hand aim ray without forwarding the
            // corresponding pinch state. Keep the generic pointer module alive so the
            // emulator's virtual touch press can complete the same hover target. UiHitTarget
            // already deduplicates the native-ray and generic-pointer paths on real hardware.
            SetPointerModuleEnabled(true);
        }

        public void RecenterDesktop()
        {
            desktopYaw = 0f;
            desktopPitch = 16f;
            desktopFieldOfView = 60f;
            desktopBasePosition = TableViewPosition;
            desktopPosition = desktopBasePosition;
            ApplyDesktopCamera();
        }

        /// <summary>
        /// Returns either runtime to the same authored south-seat view.  Keeping this
        /// public entry point lets the in-game "recenter" control work in a native
        /// PICO session without changing the restored screen-UI gameplay layout.
        /// </summary>
        public void RecenterToDesignStart()
        {
            if (headCamera == null || xrOrigin == null || !XRSettings.enabled || !XRSettings.isDeviceActive)
            {
                RecenterDesktop();
                return;
            }

            TrySetFloorTracking();
            var eye = TableViewPosition;
            var focus = new Vector3(0f, 1.25f, 0f);
            var planarDirection = Vector3.ProjectOnPlane(focus - eye, Vector3.up);
            if (planarDirection.sqrMagnitude < 0.0001f)
                planarDirection = Vector3.forward;
            var trackedLocalPosition = headCamera.transform.localPosition;
            if (trackedLocalPosition.sqrMagnitude < 0.0001f)
                trackedLocalPosition = Vector3.up * 1.6f;
            var trackedLocalYaw = Quaternion.Euler(0f, headCamera.transform.localEulerAngles.y, 0f);
            var desiredRootRotation = Quaternion.LookRotation(planarDirection, Vector3.up) * Quaternion.Inverse(trackedLocalYaw);
            xrOrigin.SetPositionAndRotation(eye - desiredRootRotation * trackedLocalPosition, desiredRootRotation);
            xrEyePosition = eye;
            LockXrCameraPosition();
        }

        public void SetGameplayView(bool treasure)
        {
            if (headCamera == null) return;
            var eye = treasure ? TreasureViewPosition : TableViewPosition;
            var focus = treasure ? new Vector3(0f, 0.72f, 26f) : new Vector3(0f, 1.25f, 0f);
            if (XRSettings.enabled && XRSettings.isDeviceActive)
            {
                PlaceXrHeadAt(eye, focus);
                return;
            }
            desktopBasePosition = eye;
            desktopPosition = desktopBasePosition;
            desktopYaw = 0f;
            desktopPitch = treasure ? 14f : 16f;
            desktopFieldOfView = treasure ? 58f : 60f;
            ApplyDesktopCamera();
        }

        private void PlaceXrHeadAt(Vector3 eye, Vector3 focus)
        {
            if (xrOrigin == null || headCamera == null) return;

            // Keep the current headset pose local to the rig, and move/rotate only the XR
            // origin to the authored world landmark. This lets the player keep looking
            // around naturally after a table/treasure view transition.
            var localHeadPosition = headCamera.transform.localPosition;
            var localForward = Vector3.ProjectOnPlane(
                headCamera.transform.localRotation * Vector3.forward, Vector3.up);
            var localYaw = localForward.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(localForward, Vector3.up);
            var desiredForward = Vector3.ProjectOnPlane(focus - eye, Vector3.up);
            var desiredYaw = desiredForward.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(desiredForward, Vector3.up);
            var rootRotation = desiredYaw * Quaternion.Inverse(localYaw);
            xrOrigin.SetPositionAndRotation(
                eye - rootRotation * localHeadPosition,
                rootRotation);
            xrEyePosition = eye;
            LockXrCameraPosition();
        }

        private void EnsureRig()
        {
            if (xrOrigin == null)
            {
                xrOrigin = transform;
                xrOrigin.name = "XR Rig · Floor Tracking";
                var cameraGo = new GameObject("XR Head Camera");
                cameraGo.transform.SetParent(xrOrigin, false);
                cameraGo.transform.localPosition = new Vector3(0f, 1.60f, -7.40f);
                cameraGo.tag = "MainCamera";
                headCamera = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();
            }

            if (headCamera == null)
            {
                headCamera = xrOrigin.GetComponentInChildren<Camera>(true);
            }

            EnsureHeadPoseDriver();

            leftController = EnsureController("Left Controller · PICO");
            rightController = EnsureController("Right Controller · PICO");
            leftRay = EnsureRay("Left Controller Ray", leftController, new Color(0.23f, 0.72f, 0.63f));
            rightRay = EnsureRay("Right Controller Ray", rightController, new Color(0.83f, 0.25f, 0.14f));
        }

        private void EnsureHeadPoseDriver()
        {
            if (headCamera == null) return;

            headPoseDriver = headCamera.GetComponent<TrackedPoseDriver>();
            if (headPoseDriver == null)
                headPoseDriver = headCamera.gameObject.AddComponent<TrackedPoseDriver>();

            // The gameplay camera is the actual XR eye. Bind it to the official PICO/XR
            // HMD controls so rotation and 6DoF movement follow the headset in both the
            // Emulator and a device. The authored world start is preserved by the XR root
            // recenter logic; this driver only supplies the live local head pose.
            // Keep the authored camera world position fixed.  The emulator can expose a
            // large synthetic head-position offset, while the requested interaction is
            // natural look-around; rotation-only tracking avoids moving the player under
            // the table and still gives true headset yaw/pitch/roll.
            headPoseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationOnly;
            headPoseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            headPoseDriver.ignoreTrackingState = false;
            headPoseDriver.positionInput = new InputActionProperty(new InputAction(
                "PICO Head Position",
                InputActionType.Value,
                "<XRHMD>/centerEyePosition",
                expectedControlType: "Vector3"));
            headPoseDriver.rotationInput = new InputActionProperty(new InputAction(
                "PICO Head Rotation",
                InputActionType.Value,
                "<XRHMD>/centerEyeRotation",
                expectedControlType: "Quaternion"));
            headPoseDriver.trackingStateInput = new InputActionProperty(new InputAction(
                "PICO Head Tracking State",
                InputActionType.Value,
                "<XRHMD>/trackingState",
                expectedControlType: "Integer"));
        }

        private Transform EnsureController(string name)
        {
            if (xrOrigin == null) return null;
            var existing = xrOrigin.Find(name);
            if (existing != null)
            {
                // Pose is written once below from the active PICO device. Leaving a second
                // TrackedPoseDriver enabled here makes the emulator race the manual pose
                // update and visibly moves the ray away from the head-relative UI.
                var trackedPose = existing.GetComponent<TrackedPoseDriver>();
                if (trackedPose != null) trackedPose.enabled = false;
                return existing;
            }
            var go = new GameObject(name);
            go.transform.SetParent(xrOrigin, false);
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
            line.useWorldSpace = true;
            line.startWidth = 0.008f;
            line.endWidth = 0.002f;
            line.material = new Material(Shader.Find("Sprites/Default")) { color = color };
            line.SetPosition(0, parent.position);
            line.SetPosition(1, parent.position + parent.forward * 5f);
            return line;
        }

        private void UpdateRay(Transform controller, LineRenderer ray)
        {
            if (controller == null || ray == null) return;
            ray.enabled = XRSettings.enabled;
            var pointerRay = BuildPointerRay(controller);
            var distance = 5f;
            if (Physics.Raycast(pointerRay, out var hit, 8f, ~0, QueryTriggerInteraction.Collide))
                distance = hit.distance;
            ray.useWorldSpace = true;
            ray.SetPosition(0, pointerRay.origin);
            ray.SetPosition(1, pointerRay.origin + pointerRay.direction * distance);
        }

        private void PollController(InputDeviceCharacteristics side, Transform controller, ref bool held, Guandan.Game.PointerSource source)
        {
            if (controller == null || !XRSettings.enabled) return;
            var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(side | InputDeviceCharacteristics.Controller, devices);
            var device = devices.Count > 0 ? devices[0] : default;
            var hasLegacyController = devices.Count > 0 && device.isValid;
            if (hasLegacyController)
            {
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position))
                    ApplyControllerPose(controller, position, null);
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation))
                    ApplyControllerPose(controller, null, rotation);
            }

            // PICO's current runtime exposes the controller through the Input System.  Some
            // Emulator releases do not populate the legacy InputDevices trigger feature, so
            // accept that signal as the authoritative fallback while retaining the legacy path
            // for older PICO firmware.
            var inputController = side.HasFlag(InputDeviceCharacteristics.Left)
                ? XRController.leftHand
                : XRController.rightHand;
            UpdateControllerPoseFromInputSystem(inputController, controller);
            var pressed = (hasLegacyController
                && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var legacyTrigger)
                && legacyTrigger)
                || IsInputSystemTriggerPressed(inputController);
            var pointerRay = BuildPointerRay(controller);
            if (pressed && !held && TryInteractAtRay(pointerRay, source))
            {
                if (hasLegacyController && device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                {
                    device.SendHapticImpulse(0u, 0.32f, 0.08f);
                }
            }
            if (pressed && Guandan.Game.SocialProp.Held != null)
            {
                Guandan.Game.SocialProp.Held.DragTo(pointerRay.GetPoint(2.2f));
            }
            if (!pressed && held && Guandan.Game.SocialProp.Held != null)
            {
                if (Guandan.Game.SocialProp.Held.ReadyToThrow)
                    FindAvatarAlongRay(pointerRay, 8f)?.Interact(source);
                Guandan.Game.SocialProp.Held?.ReturnHome();
            }
            held = pressed;
        }

        /// <summary>
        /// PICO Emulator's visible hand cursor uses a ray-based pinch.  It is neither a
        /// controller trigger nor a regular Android touchscreen click, so polling only those
        /// two devices leaves an apparently clickable UGUI card inert.  Read the PICO SDK's
        /// native aim pose and send the pinch ray through the same collider targets used by
        /// controllers.  This keeps the gameplay as flat cards on the head-relative canvas;
        /// it does not replace it with 3D cards or alter the authored tomb scene.
        /// </summary>
        private bool PollPicoHandPinch(HandType hand, ref bool held, Guandan.Game.PointerSource source)
        {
            if (!XRSettings.enabled || !XRSettings.isDeviceActive || xrOrigin == null)
            {
                held = false;
                return false;
            }

#if XR_HANDS
            // PICO 6 exposes the emulator's virtual hand cursor through the official
            // PicoHandInteraction Input System device.  The older PXR_HandTracking API can
            // report a valid extension while still returning a null tracking-service pose,
            // which is why it cannot be the primary input path here.
            var interactionHand = hand == HandType.HandLeft
                ? PicoHandInteraction.left
                : PicoHandInteraction.right;
            if (interactionHand != null)
            {
                if (!loggedPicoHandDevice)
                {
                    loggedPicoHandDevice = true;
                    Debug.Log($"[Guandan] PICO PicoHandInteraction 已注册：{hand}。");
                }

                var aimFlags = interactionHand.compatAimFlags;
                const HandAimStatus requiredPicoAim = HandAimStatus.AimComputed | HandAimStatus.AimRayValid;
                var hasPicoAimRay = (aimFlags & requiredPicoAim) == requiredPicoAim;
                var picoPinching = interactionHand.pinchTouched != null && interactionHand.pinchTouched.isPressed;
                var pointerActivated = interactionHand.pointerActivated != null && interactionHand.pointerActivated.isPressed;
                if (interactionHand.pinchValue != null)
                    picoPinching |= interactionHand.pinchValue.ReadValue() >= PicoHandInteraction.pressThreshold;
                picoPinching |= pointerActivated;

                if (!hasPicoAimRay)
                {
                    if (!loggedPicoHandUnavailable)
                    {
                        loggedPicoHandUnavailable = true;
                        Debug.Log("[Guandan] PICO PicoHandInteraction 已连接，但当前没有有效手部射线。");
                    }
                    held = picoPinching;
                    return false;
                }

                var localPosition = interactionHand.pointerPosition != null
                    ? interactionHand.pointerPosition.ReadValue()
                    : interactionHand.pointer.position.ReadValue();
                var localRotation = interactionHand.pointerRotation != null
                    ? interactionHand.pointerRotation.ReadValue()
                    : interactionHand.pointer.rotation.ReadValue();
                var origin = xrOrigin.TransformPoint(localPosition);
                var direction = xrOrigin.TransformDirection(localRotation * Vector3.forward);
                if (hasPicoAimRay && !loggedPicoHandAim)
                {
                    loggedPicoHandAim = true;
                    Debug.Log("[Guandan] 已收到 PICO PicoHandInteraction 射线；捏合可点击平面牌 UI。");
                }

                if (picoPinching && !held && direction.sqrMagnitude > 0.0001f)
                    TryInteractAtRay(BuildPointerRay(origin, direction), source);

                held = picoPinching;
                return true;
            }
#endif

            var aim = new HandAimState();
            if (!PXR_HandTracking.GetAimState(hand, ref aim))
            {
                held = false;
                return false;
            }

            const HandAimStatus requiredAim = HandAimStatus.AimComputed | HandAimStatus.AimRayValid;
            var hasAimRay = (aim.aimStatus & requiredAim) == requiredAim;
            var pinching = (aim.aimStatus & (HandAimStatus.AimIndexPinching | HandAimStatus.AimRayTouched)) != 0;
            if (hasAimRay && !loggedPicoHandAim)
            {
                loggedPicoHandAim = true;
                Debug.Log("[Guandan] 已收到 PICO 手部射线；捏合可点击平面牌 UI。");
            }

            if (pinching && !held && hasAimRay)
            {
                var localPosition = aim.aimRayPose.Position.ToVector3();
                var localRotation = aim.aimRayPose.Orientation.ToQuat();
                var origin = xrOrigin.TransformPoint(localPosition);
                var direction = xrOrigin.TransformDirection(localRotation * Vector3.forward);
                if (direction.sqrMagnitude > 0.0001f)
                    TryInteractAtRay(BuildPointerRay(origin, direction), source);
            }

            held = pinching;
            return hasAimRay;
        }

        private void SetPointerModuleEnabled(bool enabled)
        {
            if (picoHandRayAvailable == !enabled) return;
            picoHandRayAvailable = !enabled;
            var module = FindFirstObjectByType<InputSystemUIInputModule>();
            if (module != null) module.enabled = enabled;
            Debug.Log(picoHandRayAvailable
                ? "[Guandan] PICO 原生手部射线已接管平面牌点击。"
                : "[Guandan] PICO 手部射线未激活，已启用通用 UI 指针回退。");
        }

        private static bool TryInteractAtRay(Ray ray, Guandan.Game.PointerSource source)
        {
            var hits = Physics.RaycastAll(ray, 8f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            // UI is intentionally a close, flat interaction surface.  Always resolve it
            // before decorative table, avatar, or prop colliders further down the same ray.
            foreach (var hit in hits)
            {
                var uiTarget = hit.collider.GetComponentInParent<Guandan.UI.UiHitTarget>();
                if (uiTarget == null || !uiTarget.isActiveAndEnabled) continue;
                uiTarget.Interact(source);
                return true;
            }

            foreach (var hit in hits)
            {
                var interactable = hit.collider.GetComponentInParent<Guandan.Game.IWorldInteractable>();
                if (interactable == null) continue;
                interactable.Interact(source);
                return true;
            }
            return false;
        }

        private static bool IsInputSystemTriggerPressed(XRController inputController)
        {
            if (inputController == null) return false;
            var trigger = inputController.TryGetChildControl<ButtonControl>("triggerPressed")
                ?? inputController.TryGetChildControl<ButtonControl>("triggerButton");
            return trigger != null && trigger.isPressed;
        }

        private bool UpdateControllerPoseFromInputSystem(XRController inputController, Transform controller)
        {
            if (inputController == null || controller == null) return false;
            var position = inputController.TryGetChildControl<Vector3Control>("devicePosition")
                ?? inputController.TryGetChildControl<Vector3Control>("position");
            var rotation = inputController.TryGetChildControl<QuaternionControl>("deviceRotation")
                ?? inputController.TryGetChildControl<QuaternionControl>("rotation");
            var hasPose = position != null || rotation != null;
            if (hasPose)
            {
                ApplyControllerPose(controller, position != null ? position.ReadValue() : (Vector3?)null,
                    rotation != null ? rotation.ReadValue() : (Quaternion?)null);
            }
            return hasPose;
        }

        private Ray BuildPointerRay(Transform controller)
        {
            if (controller == null) return new Ray(Vector3.zero, Vector3.forward);
            return BuildPointerRay(controller.position, controller.forward);
        }

        private Ray BuildPointerRay(Vector3 origin, Vector3 direction)
        {
            // The PICO emulator reports the controller pose in tracking space while the
            // authored camera keeps a desktop world offset. Start both the visible and actual
            // hand rays exactly at the current XR camera, then use only the hand's direction.
            // This keeps the ray anchored to the player's view without moving the camera.
            var correctedOrigin = headCamera != null ? headCamera.transform.position : origin;
            var correctedDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            // Preserve the controller's actual direction. Do not retarget to the canvas
            // center when the ray misses: that makes a visible pointer and the clicked anchor
            // disagree, which is exactly the failure mode seen in the emulator. The only
            // correction here is the authored camera offset in tracking space.
            return new Ray(correctedOrigin, correctedDirection);
        }

        private void ApplyControllerPose(Transform controller, Vector3? position, Quaternion? rotation)
        {
            if (controller == null) return;
            // Unity XR pose features are tracking-space values. Keep the controller under the
            // same origin that owns the head camera so a recenter or floor-origin change moves
            // both together. The nullable arguments let legacy pose updates preserve the other
            // component without introducing a second writer.
            if (position.HasValue && IsFinite(position.Value)) controller.localPosition = position.Value;
            if (rotation.HasValue && IsFinite(rotation.Value)) controller.localRotation = rotation.Value;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        private static bool IsFinite(Quaternion value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsNaN(value.w) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z) && !float.IsInfinity(value.w);

        private void LogPointerDiagnostic()
        {
            if (Time.unscaledTime < nextPointerDiagnosticTime) return;
            nextPointerDiagnosticTime = Time.unscaledTime + 1f;
            var ui = FindFirstObjectByType<Guandan.UI.ScreenGameUi>();
            var canvasTransform = ui != null && ui.Canvas != null ? ui.Canvas.transform : null;
            LogPointer("左", leftController, canvasTransform);
            LogPointer("右", rightController, canvasTransform);
        }

        private void LogPointer(string label, Transform controller, Transform canvasTransform)
        {
            if (controller == null || headCamera == null) return;
            var ray = BuildPointerRay(controller);
            var hits = Physics.RaycastAll(ray, 8f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            var hitName = "none";
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<Guandan.UI.UiHitTarget>() != null)
                {
                    hitName = hit.collider.name;
                    break;
                }
            }
            Debug.Log($"[Guandan] 射线诊断 {label}: pointer={ray.origin:F2}/{ray.direction:F2}, camera={headCamera.transform.position:F2}/{headCamera.transform.forward:F2}, canvas={(canvasTransform != null ? canvasTransform.position.ToString("F2") : "none")}, uiHit={hitName}");
        }

        private void ApplyDesktopCamera()
        {
            if (headCamera == null) return;
            headCamera.transform.SetPositionAndRotation(
                desktopPosition,
                Quaternion.Euler(desktopPitch, desktopYaw, 0f));
            headCamera.fieldOfView = desktopFieldOfView;
        }

        private void LockXrCameraPosition()
        {
            if (headCamera == null) return;
            // PICO Emulator may apply a synthetic tracking-space translation to the rig.
            // Keep the authored eye point stable as requested; head rotation is still driven
            // by TrackedPoseDriver and therefore remains fully immersive.
            headCamera.transform.position = xrEyePosition;
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
            if (xrOrigin == null || maxXrOffsetRadius <= 0f) return;
            var offset = new Vector3(xrOrigin.localPosition.x, 0f, xrOrigin.localPosition.z);
            if (offset.magnitude <= maxXrOffsetRadius) return;
            offset = offset.normalized * maxXrOffsetRadius;
            xrOrigin.localPosition = new Vector3(offset.x, xrOrigin.localPosition.y, offset.z);
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
