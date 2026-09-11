using System;
using System.Collections;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Guandan.Title
{
    // THESIS: Enter the tomb through a cinematic bronze-and-jade seal.
    // OWN-WORLD: Generated antique-gold lettering, carved Chinese tomb, quiet jade controls.
    // STORY: Recognize Tomb Duel, choose Enter Tomb, arrive at the existing card table.
    // FIRST VIEWPORT: Full landscape art, central monumental title, two live lower controls.
    // FORM: User-pinned central emblem composition; continuation approved 2026-09-08.
    // FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, and DESIGN.md
    public sealed class TombTitleBootstrap : MonoBehaviour
    {
        private const string GameplaySceneName = "GuandanTomb";
        private const float CoverLift = 0.32f;
        [SerializeField] private Vector3 designEyePosition = new(0f, 1.72f, -4.25f);
        [SerializeField] private Vector3 designFocusPosition = new(0f, 1.62f, 0f);
        [SerializeField] private float designEyeHeight = 1.60f;
        private Transform xrOrigin;
        private Camera headCamera;
        private Transform leftController, rightController;
        private LineRenderer leftRay, rightRay;
        private bool xrStartApplied, loading;
        private PointerState left = new(), right = new(), mouse = new(), leftHand = new(), rightHand = new();
        private readonly List<UnityEngine.XR.InputDevice> devices = new();
        private readonly List<TitleActionButton> buttons = new();
        private Canvas canvas;
        private Text instruction;
        private Image fade;
        private int keyboardFocus = -1;
        public Vector3 DesignEyePosition => designEyePosition;
        public Vector3 DesignFocusPosition => designFocusPosition;
        public bool IsLoading => loading;

        private sealed class PointerState
        {
            public bool held;
            public TitleActionButton down;
        }

        private void Awake()
        {
            BuildRig();
            BuildTitleStage();
            TrySetFloorTracking();
            RecenterToDesignStart();
            Debug.Log("[TombTitle] Ready: Tripo cover, world-space menu, Start -> GuandanTomb.");
        }

        private void Update()
        {
            foreach (var button in buttons) button.Hovered = false;
            if (loading) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) RecenterToDesignStart();
                if (keyboard.tabKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
                    keyboardFocus = (keyboardFocus + 1) % buttons.Count;
                if (keyboard.upArrowKey.wasPressedThisFrame)
                    keyboardFocus = keyboardFocus <= 0 ? buttons.Count - 1 : keyboardFocus - 1;
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                    buttons[Mathf.Max(0, keyboardFocus)].Activate();
                if (keyboard.escapeKey.wasPressedThisFrame) ExitGame();
            }
            if (keyboardFocus >= 0) buttons[keyboardFocus].Hovered = true;
            if (!XRSettings.enabled || !XRSettings.isDeviceActive)
            {
                ApplyDesktopCamera();
                if (Mouse.current != null)
                {
                    if (Mouse.current.delta.ReadValue().sqrMagnitude > 0.1f) keyboardFocus = -1;
                    var pointer = headCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                    // Preserve a complete quick click even when down and up arrive in one frame.
                    if (Mouse.current.leftButton.wasPressedThisFrame) ProcessPointer(pointer, true, mouse);
                    ProcessPointer(pointer, Mouse.current.leftButton.isPressed, mouse);
                }
                leftRay.enabled = rightRay.enabled = false;
            }
            else
            {
                if (!xrStartApplied) { PlaceXrHeadAtDesignStart(); xrStartApplied = true; }
                PollController(InputDeviceCharacteristics.Left, leftController, leftRay, left);
                PollController(InputDeviceCharacteristics.Right, rightController, rightRay, right);
                PollHand(HandType.HandLeft, leftHand);
                PollHand(HandType.HandRight, rightHand);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            foreach (var state in new[] { left, right, mouse, leftHand, rightHand }) CancelPointer(state);
        }

        public void StartGame() { if (!loading) StartCoroutine(LoadGameplay()); }
        public void ExitGame() { if (!loading) StartCoroutine(ExitAfterFeedback()); }
        private IEnumerator ExitAfterFeedback()
        {
            loading = true;
            instruction.text = "LEAVING THE TOMB";
            yield return new WaitForSecondsRealtime(0.18f);
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private IEnumerator LoadGameplay()
        {
            loading = true;
            instruction.text = "OPENING THE TOMB...";
            foreach (var button in buttons) button.Interactable = false;
            yield return new WaitForSecondsRealtime(0.20f);
            AsyncOperation operation = null;
            try { operation = SceneManager.LoadSceneAsync(GameplaySceneName, LoadSceneMode.Single); }
            catch (Exception exception) { Debug.LogException(exception); }
            if (operation == null)
            {
                loading = false;
                instruction.text = "COULD NOT OPEN THE TOMB. SELECT ENTER TOMB TO RETRY.";
                foreach (var button in buttons) button.Interactable = true;
                yield break;
            }
            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f) yield return null;
            for (float t = 0; t < 0.25f; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0.008f, 0.012f, 0.010f, t / 0.25f);
                yield return null;
            }
            Debug.Log("[TombTitle] Start confirmed; activating GuandanTomb.");
            operation.allowSceneActivation = true;
        }

        private void BuildTitleStage()
        {
            var go = new GameObject("TitleRoot · Tripo cinematic cover", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            // Move the complete cover and live controls, preserving the user's eye pose.
            go.transform.position = designFocusPosition + Vector3.up * CoverLift;
            go.transform.localScale = Vector3.one * 0.004f;
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = headCamera;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1920f, 1080f);
            var artRect = Rect("Tripo Nano Banana · Tomb Duel", rect, Vector2.zero, rect.sizeDelta);
            var art = artRect.gameObject.AddComponent<RawImage>();
            art.texture = Resources.Load<Texture2D>("GuandanUI/TitleCover");
            art.raycastTarget = false;
            if (art.texture == null) Debug.LogError("[TombTitle] Missing TitleCover texture.");
            CreateButton(rect, "Enter Tomb", "ENTER TOMB", new Vector2(0f, -328f), new Vector2(424f, 76f), TitleAction.Start);
            CreateButton(rect, "Exit", "EXIT", new Vector2(0f, -430f), new Vector2(300f, 64f), TitleAction.Exit);
            instruction = Label("Controls", rect, "POINT & TRIGGER TO SELECT  /  MOUSE OR ENTER", new Vector2(0f, -508f), new Vector2(1600f, 32f), 20, new Color(0.79f, 0.81f, 0.71f));
            var fadeRect = Rect("Transition", rect, Vector2.zero, rect.sizeDelta);
            fadeRect.localPosition += Vector3.back * 5f;
            fade = fadeRect.gameObject.AddComponent<Image>();
            fade.color = Color.clear;
            fade.raycastTarget = false;
        }

        private void CreateButton(RectTransform parent, string name, string label, Vector2 position, Vector2 size, TitleAction action)
        {
            var hit = Rect(name, parent, position, size);
            hit.localPosition += Vector3.back * 4f;
            var collider = hit.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 12f);
            var visual = Rect("Visual", hit, Vector2.zero, size);
            var fill = visual.gameObject.AddComponent<Image>();
            fill.raycastTarget = false;
            var borderColor = new Color(0.60f, 0.49f, 0.29f);
            var edges = new Image[4];
            var points = new[] { new Vector2(0f, size.y * .5f), new Vector2(0f, -size.y * .5f), new Vector2(-size.x * .5f, 0f), new Vector2(size.x * .5f, 0f) };
            for (int i = 0; i < 4; i++)
            {
                var edge = Rect("Bronze edge", visual, points[i], i < 2 ? new Vector2(size.x, 1.5f) : new Vector2(1.5f, size.y));
                edges[i] = edge.gameObject.AddComponent<Image>(); edges[i].color = borderColor; edges[i].raycastTarget = false;
            }
            var text = Label("Label", visual, label, Vector2.zero, size, action == TitleAction.Start ? 32 : 27, new Color(0.94f, 0.86f, 0.64f));
            var button = hit.gameObject.AddComponent<TitleActionButton>();
            button.Configure(this, action, visual, fill, edges, text);
            buttons.Add(button);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private static Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var rect = Rect(name, parent, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.text = value; text.alignment = TextAnchor.MiddleCenter;
            text.color = color; text.raycastTarget = false; text.fontStyle = FontStyle.Normal;
            return text;
        }

        private void BuildRig()
        {
            xrOrigin = new GameObject("Title XR Rig").transform;
            xrOrigin.SetParent(transform, false);
            if (GetComponent<PXR_Manager>() == null) gameObject.AddComponent<PXR_Manager>();
            var go = new GameObject("Title XR Head Camera", typeof(Camera), typeof(AudioListener));
            go.transform.SetParent(xrOrigin, false); go.transform.localPosition = Vector3.up * designEyeHeight; go.tag = "MainCamera";
            headCamera = go.GetComponent<Camera>();
            headCamera.clearFlags = CameraClearFlags.SolidColor;
            headCamera.backgroundColor = new Color(0.008f, 0.012f, 0.010f);
            headCamera.nearClipPlane = 0.05f; headCamera.farClipPlane = 50f;
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.positionInput = new InputActionProperty(new InputAction("Title head position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction("Title head rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(new InputAction("Title head tracking", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));
            leftController = new GameObject("Left Controller").transform; leftController.SetParent(xrOrigin, false);
            rightController = new GameObject("Right Controller").transform; rightController.SetParent(xrOrigin, false);
            leftRay = MakeRay("Left Ray", new Color(0.51f, 0.87f, 0.75f));
            rightRay = MakeRay("Right Ray", new Color(1f, 0.78f, 0.40f));
        }

        private LineRenderer MakeRay(string name, Color color)
        {
            var go = new GameObject(name); go.transform.SetParent(xrOrigin, false);
            var ray = go.AddComponent<LineRenderer>(); ray.positionCount = 2; ray.useWorldSpace = true;
            ray.startWidth = .006f; ray.endWidth = .002f;
            ray.material = new Material(Shader.Find("Sprites/Default")); ray.startColor = ray.endColor = color; ray.enabled = false;
            return ray;
        }

        private void PollController(InputDeviceCharacteristics side, Transform controller, LineRenderer line, PointerState state)
        {
            devices.Clear(); InputDevices.GetDevicesWithCharacteristics(side | InputDeviceCharacteristics.Controller, devices);
            var device = devices.Count > 0 ? devices[0] : default;
            bool tracked = device.isValid, pressed = false;
            if (device.isValid)
            {
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var valid)) tracked = valid;
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position)) controller.localPosition = position;
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation)) controller.localRotation = rotation;
                pressed = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var trigger) && trigger;
            }
            var input = side == InputDeviceCharacteristics.Left ? XRController.leftHand : XRController.rightHand;
            if (input != null && input.isTracked.isPressed)
            {
                tracked = true; controller.localPosition = input.devicePosition.ReadValue(); controller.localRotation = input.deviceRotation.ReadValue();
                pressed |= input.TryGetChildControl<ButtonControl>("triggerPressed")?.isPressed == true;
                pressed |= (input.TryGetChildControl<AxisControl>("trigger")?.ReadValue() ?? 0f) >= 0.65f;
            }
            line.enabled = tracked;
            if (!tracked) { CancelPointer(state); return; }
            // Match the card-table convention: visible and hit-test rays share this exact sample.
            var ray = new Ray(headCamera.transform.position, controller.forward);
            var target = ProcessPointer(ray, pressed, state);
            // The interaction still originates at the eye. A ribbon starting exactly on the
            // projection origin expands across the view; draw only its in-front segment.
            float forward = Vector3.Dot(ray.direction, headCamera.transform.forward);
            float distance = Physics.Raycast(ray, out var visibleHit, 20f) ? visibleHit.distance : 8f;
            float start = Mathf.Max(.35f, (headCamera.nearClipPlane + .08f) / Mathf.Max(.1f, forward));
            line.enabled = forward > .1f && distance > start;
            line.SetPosition(0, ray.GetPoint(start)); line.SetPosition(1, ray.GetPoint(distance));
            if (pressed && target != null && device.isValid && device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
            {
                if (target.PressStartedThisFrame) device.SendHapticImpulse(0, .22f, .045f);
            }
        }

        private void PollHand(HandType hand, PointerState state)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var aim = new HandAimState();
            const HandAimStatus required = HandAimStatus.AimComputed | HandAimStatus.AimRayValid;
            if (PXR_HandTracking.GetAimState(hand, ref aim) && (aim.aimStatus & required) == required)
            {
                var direction = xrOrigin.TransformDirection(aim.aimRayPose.Orientation.ToQuat() * Vector3.forward);
                bool pressed = (aim.aimStatus & (HandAimStatus.AimIndexPinching | HandAimStatus.AimRayTouched)) != 0;
                ProcessPointer(new Ray(headCamera.transform.position, direction), pressed, state);
            }
            else CancelPointer(state);
#endif
        }

        private TitleActionButton ProcessPointer(Ray ray, bool pressed, PointerState state)
        {
            TitleActionButton target = null;
            if (Physics.Raycast(ray, out var hit, 20f)) target = hit.collider.GetComponent<TitleActionButton>();
            if (target != null) target.Hovered = true;
            if (pressed && !state.held) { state.down = target; target?.SetPressed(true); }
            if (!pressed && state.held)
            {
                state.down?.SetPressed(false);
                if (target != null && target == state.down) target.Activate();
                state.down = null;
            }
            state.held = pressed;
            return target;
        }
        private static void CancelPointer(PointerState state) { state.down?.SetPressed(false); state.down = null; state.held = false; }

        public void RecenterToDesignStart()
        {
            if (XRSettings.enabled && XRSettings.isDeviceActive) { TrySetFloorTracking(); PlaceXrHeadAtDesignStart(); xrStartApplied = true; }
            else ApplyDesktopCamera();
        }
        private void ApplyDesktopCamera()
        {
            headCamera.transform.SetPositionAndRotation(designEyePosition, Quaternion.LookRotation(designFocusPosition - designEyePosition));
            // Keep the full authored 16:9 cover and menu visible in narrower desktop windows.
            headCamera.fieldOfView = Mathf.Max(58f, 2f * Mathf.Atan(3.98f / (4.25f * Mathf.Max(.6f, headCamera.aspect))) * Mathf.Rad2Deg);
        }
        private void PlaceXrHeadAtDesignStart()
        {
            var local = headCamera.transform.localPosition;
            if (local.sqrMagnitude < .0001f) local = Vector3.up * designEyeHeight;
            var forward = Vector3.ProjectOnPlane(headCamera.transform.localRotation * Vector3.forward, Vector3.up);
            var headYaw = forward.sqrMagnitude > .0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            var designYaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(designFocusPosition - designEyePosition, Vector3.up));
            var rotation = designYaw * Quaternion.Inverse(headYaw);
            xrOrigin.SetPositionAndRotation(designEyePosition - rotation * local, rotation);
        }
        private static void TrySetFloorTracking()
        {
            var systems = new List<XRInputSubsystem>(); SubsystemManager.GetSubsystems(systems);
            foreach (var system in systems) system.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }
    }

    public enum TitleAction { Start, Exit }
    public sealed class TitleActionButton : MonoBehaviour
    {
        private TombTitleBootstrap owner;
        private TitleAction action;
        private RectTransform visual;
        private Image fill;
        private Image[] edges;
        private Text label;
        private float hover, press;
        private bool pressed;
        private int pressFrame = -1;
        public bool Hovered { get; set; }
        public bool Interactable { get; set; } = true;
        public bool PressStartedThisFrame => pressFrame == Time.frameCount;
        public void Configure(TombTitleBootstrap title, TitleAction titleAction, RectTransform target, Image background, Image[] outline, Text text)
        { owner = title; action = titleAction; visual = target; fill = background; edges = outline; label = text; ApplyVisual(); }
        public void SetPressed(bool value) { pressed = value; if (value) pressFrame = Time.frameCount; }
        public void Activate()
        {
            if (!Interactable || owner == null || owner.IsLoading) return;
            press = 1f;
            Debug.Log($"[TombTitle] Activated {action}");
            if (action == TitleAction.Start) owner.StartGame(); else owner.ExitGame();
        }
        private void LateUpdate()
        {
            float t = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            hover = Mathf.Lerp(hover, Hovered && Interactable ? 1f : 0f, t);
            press = Mathf.Lerp(press, pressed ? 1f : 0f, t);
            ApplyVisual();
        }
        private void ApplyVisual()
        {
            if (visual == null) return;
            visual.localScale = Vector3.one * (1f + hover * .035f - press * .055f);
            fill.color = Color.Lerp(new Color(.035f, .064f, .054f, .94f), new Color(.14f, .26f, .21f, .98f), hover);
            var edge = Color.Lerp(new Color(.61f, .49f, .28f, .95f), new Color(1f, .84f, .48f), hover);
            foreach (var image in edges) image.color = edge;
            label.color = Color.Lerp(new Color(.94f, .86f, .65f), new Color(1f, .97f, .83f), hover);
            if (!Interactable) label.color *= new Color(.70f, .70f, .70f, 1f);
        }
    }
}
