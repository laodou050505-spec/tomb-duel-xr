using System.Collections;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Guandan.Title
{
    /// <summary>
    /// A deliberately separate, opaque title stage. No gameplay scene object is loaded
    /// here: START unloads this scene by loading GuandanTomb in Single mode.
    /// </summary>
    public sealed class TombTitleBootstrap : MonoBehaviour
    {
        private const string GameplaySceneName = "GuandanTomb";

        [Header("Authoritative title-stage player start")]
        [SerializeField] private Vector3 designEyePosition = new(0f, 1.72f, -4.25f);
        [SerializeField] private Vector3 designFocusPosition = new(0f, 1.62f, 0f);
        [SerializeField] private float designEyeHeight = 1.60f;

        private Transform xrOrigin;
        private Camera headCamera;
        private Transform leftController;
        private Transform rightController;
        private LineRenderer leftRay;
        private LineRenderer rightRay;
        private bool xrStartApplied;
        private bool leftTriggerHeld;
        private bool rightTriggerHeld;
        private bool loading;

        public Vector3 DesignEyePosition => designEyePosition;
        public Vector3 DesignFocusPosition => designFocusPosition;

        private void Awake()
        {
            BuildTitleStage();
            BuildRig();
            TrySetFloorTracking();
            RecenterToDesignStart();
        }

        private void Update()
        {
            if (loading) return;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RecenterToDesignStart();
                if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame) StartGame();
                if (Keyboard.current.escapeKey.wasPressedThisFrame) ExitGame();
            }

            if (!XRSettings.enabled || !XRSettings.isDeviceActive)
            {
                ApplyDesktopCamera();
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && headCamera != null)
                {
                    var ray = headCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                    if (Physics.Raycast(ray, out var hit, 30f)) hit.collider.GetComponentInParent<TitleActionButton>()?.Activate();
                }
            }
            else if (!xrStartApplied)
            {
                PlaceXrHeadAtDesignStart();
                xrStartApplied = true;
            }

            UpdateRay(leftController, leftRay);
            UpdateRay(rightController, rightRay);
            PollController(InputDeviceCharacteristics.Left, leftController, ref leftTriggerHeld);
            PollController(InputDeviceCharacteristics.Right, rightController, ref rightTriggerHeld);
        }

        public void StartGame()
        {
            if (!loading) StartCoroutine(LoadGameplay());
        }

        public void ExitGame()
        {
            Application.Quit();
        }

        public void RecenterToDesignStart()
        {
            if (XRSettings.enabled && XRSettings.isDeviceActive)
            {
                TrySetFloorTracking();
                PlaceXrHeadAtDesignStart();
                xrStartApplied = true;
            }
            else
            {
                ApplyDesktopCamera();
            }
        }

        private IEnumerator LoadGameplay()
        {
            loading = true;
            var operation = SceneManager.LoadSceneAsync(GameplaySceneName, LoadSceneMode.Single);
            while (operation != null && !operation.isDone) yield return null;
        }

        private void BuildTitleStage()
        {
            if (transform.Find("TitleRoot · 不透明启动阶段") != null) return;
            var root = new GameObject("TitleRoot · 不透明启动阶段").transform;
            root.SetParent(transform, false);

            var backdrop = CreateBlock("Opaque Tomb Cover", root, new Vector3(0f, 1.62f, 0f), new Vector3(6.6f, 4.3f, 0.22f), new Color(0.025f, 0.045f, 0.045f));
            ApplyTexture(backdrop, "GuandanUI/TombPanel", new Color(0.08f, 0.11f, 0.10f));
            var inner = CreateBlock("Bronze Inner Frame", root, new Vector3(0f, 1.62f, -0.13f), new Vector3(5.8f, 3.5f, 0.04f), new Color(0.12f, 0.075f, 0.035f));
            ApplyTexture(inner, "GuandanUI/TombPanel", new Color(0.15f, 0.12f, 0.08f));

            CreateText("Title", root, "地宫争锋", new Vector3(0f, 2.52f, -0.20f), 0.11f, new Color(0.95f, 0.78f, 0.36f));
            CreateText("Subtitle", root, "掼蛋夺宝 · 进入地宫牌桌", new Vector3(0f, 2.10f, -0.20f), 0.038f, new Color(0.70f, 0.88f, 0.79f));
            CreateText("Instruction", root, "走近启动台，以控制器射线或鼠标选择", new Vector3(0f, 0.86f, -0.20f), 0.028f, new Color(0.80f, 0.72f, 0.54f));

            CreateActionButton(root, "Start", "进入地宫", new Vector3(0f, 1.45f, -0.24f), TitleAction.Start);
            CreateActionButton(root, "Exit", "退出", new Vector3(0f, 1.10f, -0.24f), TitleAction.Exit, new Vector3(1.55f, 0.26f, 0.10f));
        }

        private void BuildRig()
        {
            xrOrigin = transform.Find("Title XR Rig · Floor Tracking");
            if (xrOrigin == null)
            {
                xrOrigin = new GameObject("Title XR Rig · Floor Tracking").transform;
                xrOrigin.SetParent(transform, false);
            }
            if (GetComponent<PXR_Manager>() == null) gameObject.AddComponent<PXR_Manager>();

            headCamera = xrOrigin.GetComponentInChildren<Camera>(true);
            if (headCamera == null)
            {
                var cameraObject = new GameObject("Title XR Head Camera");
                cameraObject.transform.SetParent(xrOrigin, false);
                cameraObject.transform.localPosition = Vector3.up * designEyeHeight;
                cameraObject.tag = "MainCamera";
                headCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            leftController = CreateController("Left Controller · PICO");
            rightController = CreateController("Right Controller · PICO");
            leftRay = CreateRay(leftController, "Left Title Ray", new Color(0.23f, 0.72f, 0.63f));
            rightRay = CreateRay(rightController, "Right Title Ray", new Color(0.83f, 0.25f, 0.14f));
        }

        private Transform CreateController(string name)
        {
            var controller = xrOrigin.Find(name);
            if (controller != null) return controller;
            var go = new GameObject(name);
            go.transform.SetParent(xrOrigin, false);
            go.AddComponent<TrackedPoseDriver>();
            return go.transform;
        }

        private static LineRenderer CreateRay(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ray = go.AddComponent<LineRenderer>();
            ray.positionCount = 2;
            ray.useWorldSpace = false;
            ray.startWidth = 0.012f;
            ray.endWidth = 0.003f;
            ray.material = new Material(Shader.Find("Sprites/Default")) { color = color };
            ray.SetPosition(0, Vector3.zero);
            ray.SetPosition(1, Vector3.forward * 8f);
            return ray;
        }

        private void PollController(InputDeviceCharacteristics side, Transform controller, ref bool held)
        {
            if (!XRSettings.enabled || controller == null) return;
            var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(side | InputDeviceCharacteristics.Controller, devices);
            if (devices.Count == 0) return;
            var device = devices[0];
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position)) controller.localPosition = position;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation)) controller.localRotation = rotation;
            var pressed = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var trigger) && trigger;
            if (pressed && !held && Physics.Raycast(controller.position, controller.forward, out var hit, 10f))
            {
                var button = hit.collider.GetComponentInParent<TitleActionButton>();
                button?.Activate();
                if (button != null && device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                    device.SendHapticImpulse(0u, 0.28f, 0.06f);
            }
            held = pressed;
        }

        private static void UpdateRay(Transform controller, LineRenderer ray)
        {
            if (controller == null || ray == null) return;
            ray.enabled = XRSettings.enabled;
        }

        private void ApplyDesktopCamera()
        {
            if (headCamera == null) return;
            headCamera.transform.SetPositionAndRotation(designEyePosition, Quaternion.LookRotation(designFocusPosition - designEyePosition, Vector3.up));
            headCamera.fieldOfView = 58f;
        }

        private void PlaceXrHeadAtDesignStart()
        {
            if (xrOrigin == null || headCamera == null) return;
            var localHeadPosition = headCamera.transform.localPosition;
            if (localHeadPosition.sqrMagnitude < 0.0001f) localHeadPosition = Vector3.up * designEyeHeight;
            var localForward = Vector3.ProjectOnPlane(headCamera.transform.localRotation * Vector3.forward, Vector3.up);
            var localYaw = localForward.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(localForward);
            var designForward = Vector3.ProjectOnPlane(designFocusPosition - designEyePosition, Vector3.up);
            var designYaw = designForward.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(designForward);
            var rootRotation = designYaw * Quaternion.Inverse(localYaw);
            xrOrigin.SetPositionAndRotation(designEyePosition - rootRotation * localHeadPosition, rootRotation);
        }

        private void TrySetFloorTracking()
        {
            if (!Application.isPlaying) return;
            var subsystems = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
                subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }

        private static GameObject CreateBlock(string name, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().material.color = color;
            return block;
        }

        private void CreateActionButton(Transform parent, string name, string label, Vector3 position, TitleAction action, Vector3? size = null)
        {
            var button = CreateBlock(name, parent, position, size ?? new Vector3(2.05f, 0.32f, 0.10f), Color.white);
            ApplyTexture(button, "GuandanUI/TombButton", new Color(0.34f, 0.17f, 0.06f));
            button.AddComponent<TitleActionButton>().Configure(this, action);
            // Keep label text outside the scaled button hierarchy so it remains a
            // proportionate world-space inscription rather than stretching as a HUD.
            CreateText($"{name} Label", parent, label, position + new Vector3(0f, 0f, -0.065f), 0.033f, new Color(1f, 0.86f, 0.51f));
        }

        private static void CreateText(string name, Transform parent, string value, Vector3 position, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.identity;
            var text = go.AddComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = size;
            text.fontSize = 64;
            text.color = color;
        }

        private static void ApplyTexture(GameObject target, string resourcePath, Color fallback)
        {
            var texture = Resources.Load<Texture2D>(resourcePath);
            var material = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Standard"));
            if (texture != null) material.mainTexture = texture;
            material.color = texture == null ? fallback : Color.white;
            target.GetComponent<Renderer>().material = material;
        }
    }

    public enum TitleAction { Start, Exit }

    public sealed class TitleActionButton : MonoBehaviour
    {
        private TombTitleBootstrap owner;
        private TitleAction action;

        public void Configure(TombTitleBootstrap title, TitleAction titleAction)
        {
            owner = title;
            action = titleAction;
        }

        public void Activate()
        {
            if (owner == null) owner = FindFirstObjectByType<TombTitleBootstrap>();
            if (action == TitleAction.Start) owner?.StartGame(); else owner?.ExitGame();
        }
    }
}
