using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Guandan.Game;
using UnityEngine;

namespace Guandan.Scene
{
    /// <summary>
    /// Runtime-only binding layer for the user-authored room. It adds gameplay actors and
    /// interaction colliders without moving, deleting, or rebuilding authored scene objects.
    /// </summary>
    public sealed class RuntimeGameplayBindings : MonoBehaviour
    {
        private const string TomatoModelPrefix = "tripo_convert_95bef798-c181-478f-970a-2f2f20ffbb60";
        private const string FlowerModelPrefix = "tripo_convert_12ef2a73-0324-4ad9-88ee-41de83e7c215";
        private const string BellModelPrefix = "tripo_convert_63da15bc-d2df-4494-b3bc-9b6ee2e87a5a";
        private static readonly Dictionary<string, int> AuthoredAvatarModelIds = new(StringComparer.Ordinal)
        {
            ["tripo_convert_d2277f49-c6a0-4ac8-a629-8761277c87e5"] = 1,
            ["tripo_convert_f9a97509-3e52-4a8b-ae86-ad722d7374c7"] = 2,
            ["tripo_convert_897a88cf-4206-44c7-8a26-67179e2c26e9"] = 3,
            ["tripo_convert_38724699-3504-418c-a65d-36d61449aba5"] = 4,
            ["tripo_convert_ec488428-2e0b-4ff8-bdb9-7819075a02c6"] = 5,
        };
        private readonly Dictionary<PlayerSeat, Transform> avatars = new();
        private readonly Dictionary<PlayerSeat, Transform> chairs = new();
        private readonly Dictionary<PlayerSeat, Transform> seatAnchorBySeat = new();
        private readonly Dictionary<PlayerSeat, Vector3> avatarSourceScales = new();
        private readonly Dictionary<PlayerSeat, Vector3> chairSourceScales = new();
        private readonly Dictionary<PlayerSeat, SeatedAvatarMotion> avatarMotions = new();
        private readonly List<GameObject> runtimeSeatObjects = new();
        private Transform runtimeRoot;
        private GameDirector director;
        private Transform[] boundSeatAnchors;
        private Vector3 tableCenter;
        private float tableTopY;
        private float floorY;
        private AvatarLayoutSettings layoutSettings;
        private bool initialized;
        private BellReminder bell;
        private bool spawnRuntimeAvatars;

        public bool IsInitialized => initialized;

        public void Initialize(GameDirector owner, Transform[] seatAnchors, int avatarSeed = 0, bool spawnAvatars = false)
        {
            if (initialized) return;
            director = owner;
            boundSeatAnchors = seatAnchors;
            spawnRuntimeAvatars = spawnAvatars;
            tableCenter = ResolveTableCenter(seatAnchors);
            tableTopY = ResolveTableTopY();
            floorY = ResolveFloorY();
            layoutSettings = AvatarLayoutSettings.LoadOrCreateRuntimeDefault();
            runtimeRoot = new GameObject("RuntimeGameplayBindings · 不修改场景").transform;
            runtimeRoot.SetParent(transform.root, false);
            BindSeats(seatAnchors, avatarSeed);
            if (!spawnRuntimeAvatars) BindAuthoredAvatars(avatarSeed);
            BindSocialProps();
            BindReminderBell();
            initialized = true;
        }

        public void RerollAvatars(int avatarSeed)
        {
            foreach (var item in runtimeSeatObjects)
            {
                if (item != null) Destroy(item);
            }
            runtimeSeatObjects.Clear();
            avatars.Clear();
            chairs.Clear();
            seatAnchorBySeat.Clear();
            avatarSourceScales.Clear();
            chairSourceScales.Clear();
            avatarMotions.Clear();
            BindSeats(boundSeatAnchors, avatarSeed);
        }

        public void ApplyLayoutSettings(AvatarLayoutSettings settings = null)
        {
            layoutSettings = settings != null ? settings : AvatarLayoutSettings.LoadOrCreateRuntimeDefault();
            foreach (var seat in avatars.Keys.ToArray()) ApplySeatLayout(seat);
        }

        public void SetThinking(PlayerSeat seat, bool thinking)
        {
            // The camera-facing seat plaque already owns this state. A second world-space
            // label becomes enormous at close range and duplicates the same information.
        }

        public void PlaySocialReaction(PlayerSeat seat, SocialPropType type)
        {
            if (avatars.TryGetValue(seat, out var avatar) && avatar != null)
            {
                if (avatarMotions.TryGetValue(seat, out var motion) && motion != null) motion.React(type);
                if (type == SocialPropType.Tomato) StartCoroutine(TomatoReactionRoutine(avatar));
                else StartCoroutine(FlowerReactionRoutine(avatar));
            }
        }

        public void PlayBellPulse(Vector3 position)
        {
            StartCoroutine(BellPulseRoutine(position));
        }

        private void BindSeats(Transform[] seatAnchors, int avatarSeed)
        {
            if (seatAnchors == null || seatAnchors.Length < 4) return;
            var random = new System.Random(avatarSeed == 0 ? Environment.TickCount : avatarSeed);
            var avatarIds = new[] { 1, 2, 3, 4, 5 };
            for (var index = avatarIds.Length - 1; index > 0; index--)
            {
                var swap = random.Next(index + 1);
                (avatarIds[index], avatarIds[swap]) = (avatarIds[swap], avatarIds[index]);
            }
            var avatarCursor = 0;
            for (var i = 0; i < 4; i++)
            {
                var anchor = seatAnchors[i];
                if (anchor == null) continue;
                var seat = (PlayerSeat)i;
                if (seat == PlayerSeat.South) continue;
                seatAnchorBySeat[seat] = anchor;
                if (!spawnRuntimeAvatars) continue;
                HidePlaceholder(anchor);

                var avatarPrefab = Resources.Load<GameObject>($"GuandanAvatars/Avatar_{avatarIds[avatarCursor++]}");
                var chairPrefab = Resources.Load<GameObject>("GuandanAvatars/Chair_f4");
                if (avatarPrefab == null || chairPrefab == null)
                {
                    Debug.LogWarning($"[Guandan] Runtime avatar assets are missing for {seat}. Run 掼蛋/生成运行时人物预制体 once.");
                    continue;
                }

                var chair = Instantiate(chairPrefab, runtimeRoot);
                chair.name = $"Chair_{SeatLabel(seat)} · f4箱子";
                chair.transform.position = new Vector3(anchor.position.x, floorY, anchor.position.z);
                chairs[seat] = chair.transform;
                chairSourceScales[seat] = chair.transform.localScale;
                runtimeSeatObjects.Add(chair);

                var avatar = Instantiate(avatarPrefab, runtimeRoot);
                avatar.name = $"AI_{SeatLabel(seat)} · 坐姿";
                avatar.transform.position = new Vector3(anchor.position.x, floorY, anchor.position.z);
                avatar.transform.rotation = FaceTableVisual(avatar.transform.position, tableCenter);
                avatarSourceScales[seat] = avatar.transform.localScale;
                ApplyMatteAvatarMaterials(avatar.transform);
                SmoothAvatarMeshes(avatar.transform);
                avatars[seat] = avatar.transform;
                runtimeSeatObjects.Add(avatar);
                StartCoroutine(ActivateAvatarMotion(avatar.transform, seat, avatarIds[avatarCursor - 1], (float)random.NextDouble()));
            }
        }

        private void BindAuthoredAvatars(int avatarSeed)
        {
            var candidates = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(item => item != null && AuthoredAvatarModelIds.ContainsKey(item.name))
                .OrderBy(item => item.position.x)
                .ToArray();
            if (candidates.Length == 0)
            {
                Debug.LogWarning("[GuandanAvatar] 未发现用户在 Scene 中摆放的 1—5 号人物，未创建替代人物。");
                return;
            }

            var remainingSeats = new HashSet<PlayerSeat>(new[] { PlayerSeat.East, PlayerSeat.North, PlayerSeat.West });
            var random = new System.Random(avatarSeed == 0 ? Environment.TickCount : avatarSeed);
            foreach (var avatar in candidates)
            {
                var seat = remainingSeats
                    .OrderBy(value => Vector3.SqrMagnitude(avatar.position - SeatPosition(value)))
                    .FirstOrDefault();
                remainingSeats.Remove(seat);

                var authoredPosition = avatar.position;
                var authoredRotation = avatar.rotation;
                var authoredScale = avatar.localScale;
                var avatarIndex = AuthoredAvatarModelIds[avatar.name];
                ApplyMatteAvatarMaterials(avatar);
                Debug.Log($"[GuandanAvatar] {avatar.name} matte materials ready.");

                var presentation = avatar.GetComponent<AuthoredAvatarPresentation>()
                    ?? avatar.gameObject.AddComponent<AuthoredAvatarPresentation>();
                Debug.Log($"[GuandanAvatar] {avatar.name} presentation component ready.");
                presentation.Configure(avatarIndex, (float)random.NextDouble());
                Debug.Log($"[GuandanAvatar] {avatar.name} sit animation ready.");
                AddAvatarTarget(avatar.gameObject, seat);
                Debug.Log($"[GuandanAvatar] {avatar.name} interaction target ready.");
                avatars[seat] = avatar;

                // Animator setup, material instances and hit targets are allowed; the
                // user's placement is not. This also protects against future regressions.
                avatar.SetPositionAndRotation(authoredPosition, authoredRotation);
                avatar.localScale = authoredScale;
                presentation.CaptureBaseline();
                Debug.Log($"[GuandanAvatar] 绑定手动人物 Avatar_{avatarIndex} -> {seat}; root Transform preserved.");
            }
        }

        private Vector3 SeatPosition(PlayerSeat seat)
        {
            return seatAnchorBySeat.TryGetValue(seat, out var anchor) && anchor != null
                ? anchor.position
                : seat switch
                {
                    PlayerSeat.East => tableCenter + Vector3.right * 4f,
                    PlayerSeat.North => tableCenter + Vector3.forward * 4f,
                    PlayerSeat.West => tableCenter + Vector3.left * 4f,
                    _ => tableCenter,
                };
        }

        private void BindSocialProps()
        {
            var sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var tomatoes = FindBasketContents(sceneTransforms, TomatoModelPrefix);
            var flowers = FindBasketContents(sceneTransforms, FlowerModelPrefix);
            for (var index = 0; index < tomatoes.Length; index++)
                ConfigureSocialProp(tomatoes[index].gameObject, SocialPropType.Tomato, $"SocialProp · 西红柿 {index + 1}");
            for (var index = 0; index < flowers.Length; index++)
                ConfigureSocialProp(flowers[index].gameObject, SocialPropType.Flower, $"SocialProp · 鲜花 {index + 1}");
        }

        private Transform[] FindBasketContents(IEnumerable<Transform> candidates, string modelPrefix)
        {
            return candidates
                .Where(item => item != null
                    && item.name.StartsWith(modelPrefix, StringComparison.Ordinal))
                .OrderBy(item => item.position.x)
                .ToArray();
        }

        private static void ConfigureSocialProp(GameObject prop, SocialPropType type, string name)
        {
            prop.name = name;
            var component = prop.GetComponent<SocialProp>() ?? prop.AddComponent<SocialProp>();
            component.Configure(type);
            if (prop.GetComponentInChildren<Collider>() == null)
            {
                var collider = prop.AddComponent<BoxCollider>();
                var renderers = prop.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers.Length == 0 ? new Bounds(prop.transform.position, Vector3.one * 0.8f) : renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                collider.center = prop.transform.InverseTransformPoint(bounds.center);
                collider.size = new Vector3(
                    Mathf.Max(0.45f, bounds.size.x / Mathf.Max(0.01f, prop.transform.lossyScale.x)),
                    Mathf.Max(0.45f, bounds.size.y / Mathf.Max(0.01f, prop.transform.lossyScale.y)),
                    Mathf.Max(0.45f, bounds.size.z / Mathf.Max(0.01f, prop.transform.lossyScale.z)));
            }
        }

        private void BindReminderBell()
        {
            // Resolve the authored bell by semantic name or its imported model identity.
            // Never use the old world position: the user is free to swap the bell and
            // ding between locations in the Scene window.
            var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bellTransform = all.FirstOrDefault(item => item != null &&
                (item.name.Contains("钟", StringComparison.OrdinalIgnoreCase)
                 || item.name.Contains("bell", StringComparison.OrdinalIgnoreCase)));
            bellTransform ??= all.FirstOrDefault(item => item != null && item.name.StartsWith(BellModelPrefix, StringComparison.Ordinal));
            if (bellTransform == null)
            {
                Debug.LogWarning("[Guandan] 未找到用户场景中的真实吊钟，未创建替代钟。");
                return;
            }

            if (bellTransform.GetComponentInChildren<Collider>() == null)
            {
                var renderers = bellTransform.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                    var hit = bellTransform.gameObject.AddComponent<BoxCollider>();
                    hit.center = bellTransform.InverseTransformPoint(bounds.center);
                    var scale = bellTransform.lossyScale;
                    var localSize = new Vector3(
                        bounds.size.x / Mathf.Max(0.01f, Mathf.Abs(scale.x)),
                        bounds.size.y / Mathf.Max(0.01f, Mathf.Abs(scale.y)),
                        bounds.size.z / Mathf.Max(0.01f, Mathf.Abs(scale.z)));
                    hit.size = Vector3.Scale(localSize, new Vector3(0.52f, 0.58f, 0.48f));
                }
            }
            bell = bellTransform.GetComponent<BellReminder>() ?? bellTransform.gameObject.AddComponent<BellReminder>();
        }

        private void AddAvatarTarget(GameObject avatar, PlayerSeat seat)
        {
            var target = avatar.GetComponent<Guandan.Game.AvatarTarget>();
            if (target == null) target = avatar.AddComponent<Guandan.Game.AvatarTarget>();
            target.Configure(seat);
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length == 0 ? new Bounds(avatar.transform.position + Vector3.up, Vector3.one) : renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Construct the component together with the object. A few imported avatar
            // hierarchies expose a stale collider-shaped component that can make a
            // later GetComponent/AddComponent expression return a Unity fake-null.
            // Creating the hitbox with its collider avoids that editor-only failure.
            var hitbox = new GameObject("AvatarInteractionHitbox", typeof(CapsuleCollider));
            hitbox.transform.SetParent(avatar.transform, false);
            var collider = hitbox.GetComponent<CapsuleCollider>();
            if (collider == null)
            {
                Debug.LogWarning($"[Guandan] 无法创建 {avatar.name} 的交互碰撞体，跳过该人物的投掷命中。");
                return;
            }
            collider.center = hitbox.transform.InverseTransformPoint(bounds.center);
            collider.height = Mathf.Max(1.2f, bounds.size.y / Mathf.Max(0.01f, avatar.transform.lossyScale.y));
            collider.radius = Mathf.Clamp(bounds.size.x / Mathf.Max(0.01f, avatar.transform.lossyScale.x) * 0.34f, 0.18f, 0.48f);
        }

        private static void HidePlaceholder(Transform anchor)
        {
            foreach (var renderer in anchor.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var collider in anchor.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        private static Quaternion FaceTableVisual(Vector3 position, Vector3 center)
        {
            var target = new Vector3(center.x, position.y, center.z);
            var direction = Vector3.ProjectOnPlane(target - position, Vector3.up);
            if (direction.sqrMagnitude < 0.001f) return Quaternion.identity;
            // Tripo's characters visually face local -Z after import.
            return Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }

        private static Vector3 ResolveTableCenter(Transform[] seatAnchors)
        {
            var table = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name == "Table_夯土探方牌桌"
                    || item.name.StartsWith("tripo_convert_536e6678-3785-43f8-b864-087a847da18f", StringComparison.Ordinal));
            if (table != null) return table.position;
            if (seatAnchors == null) return Vector3.zero;
            var valid = seatAnchors.Where(item => item != null).ToArray();
            if (valid.Length == 0) return Vector3.zero;
            var sum = Vector3.zero;
            foreach (var item in valid) sum += item.position;
            return sum / valid.Length;
        }

        private static float ResolveTableTopY()
        {
            var table = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name.StartsWith("tripo_convert_536e6678-3785-43f8-b864-087a847da18f", StringComparison.Ordinal));
            return table != null && TryGetBounds(table, out var bounds) ? bounds.max.y : 2.14f;
        }

        private static float ResolveFloorY()
        {
            var candidates = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item.name.StartsWith("Floor_探方地面", StringComparison.Ordinal));
            var heights = candidates
                .Select(item => TryGetBounds(item, out var bounds) ? bounds.max.y : float.PositiveInfinity)
                .Where(value => value < 0.5f)
                .ToArray();
            return heights.Length > 0 ? heights.Max() : 0f;
        }

        private static void FitToHeight(Transform root, float targetHeight, bool alignToFloor)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var factor = targetHeight / Mathf.Max(0.01f, bounds.size.y);
            root.localScale *= factor;
            renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (alignToFloor) root.position += Vector3.up * (-bounds.min.y);
        }

        private void ApplySeatLayout(PlayerSeat seat)
        {
            if (!avatars.TryGetValue(seat, out var root)
                || !chairs.TryGetValue(seat, out var chair)
                || !seatAnchorBySeat.TryGetValue(seat, out var anchor)) return;

            var tuning = layoutSettings.ForSeat(seat);
            root.localScale = avatarSourceScales[seat];
            root.SetPositionAndRotation(
                new Vector3(anchor.position.x, floorY, anchor.position.z) + tuning.positionOffset,
                FaceTableVisual(anchor.position + tuning.positionOffset, tableCenter) * Quaternion.Euler(0f, tuning.yawOffset, 0f));

            var animator = FindAnimator(root);
            if (animator != null && animator.enabled) animator.Update(0f);
            if (!TryGetBounds(root, out var initialBounds)) return;
            root.position += Vector3.up * (floorY + tuning.positionOffset.y - initialBounds.min.y);
            if (!TryGetBounds(root, out initialBounds)) return;

            var chestY = ResolveBoneY(animator, root, HumanBodyBones.Chest, HumanBodyBones.UpperChest, "Spine02", "Chest", "UpperChest");
            if (float.IsNaN(chestY)) chestY = Mathf.Lerp(initialBounds.min.y, initialBounds.max.y, 0.64f);
            var chestAboveFloor = Mathf.Max(0.25f, chestY - initialBounds.min.y);
            var desiredChestY = tableTopY + layoutSettings.chestToTableOffset;
            var calibratedScale = Mathf.Clamp((desiredChestY - floorY) / chestAboveFloor, 0.45f, 4.5f);
            root.localScale = Vector3.Scale(
                avatarSourceScales[seat],
                Vector3.one * calibratedScale * layoutSettings.globalScale * tuning.scale);

            if (animator != null && animator.enabled) animator.Update(0f);
            if (TryGetBounds(root, out var finalBounds))
                root.position += Vector3.up * (floorY + tuning.positionOffset.y - finalBounds.min.y);
            root.rotation = FaceTableVisual(root.position, tableCenter) * Quaternion.Euler(0f, tuning.yawOffset, 0f);

            if (animator != null && animator.enabled) animator.Update(0f);
            if (TryGetBounds(root, out finalBounds))
            {
                var measuredChestY = ResolveBoneY(animator, root, HumanBodyBones.Chest, HumanBodyBones.UpperChest, "Spine02", "Chest", "UpperChest");
                if (float.IsNaN(measuredChestY)) measuredChestY = Mathf.Lerp(finalBounds.min.y, finalBounds.max.y, 0.64f);
                Debug.Log($"[GuandanAvatar] {seat} chest={measuredChestY:F2} table={tableTopY:F2} height={finalBounds.size.y:F2} scale={root.localScale.x:F2}");
            }
            var hipsY = ResolveBoneY(animator, root, HumanBodyBones.Hips, HumanBodyBones.Hips, "Hips", "Pelvis");
            if (float.IsNaN(hipsY) && TryGetBounds(root, out finalBounds))
                hipsY = Mathf.Lerp(finalBounds.min.y, finalBounds.max.y, 0.43f);
            var chairHeight = Mathf.Clamp(
                (hipsY - floorY - layoutSettings.chairToHipGap) * tuning.chairHeightMultiplier,
                0.55f,
                1.75f);
            chair.localScale = chairSourceScales[seat];
            chair.position = new Vector3(root.position.x, floorY, root.position.z);
            FitToHeight(chair, chairHeight, false);
            if (TryGetBounds(chair, out var chairBounds)) chair.position += Vector3.up * (floorY - chairBounds.min.y);

            avatarMotions.TryGetValue(seat, out var motion);
            motion?.Rebase();
        }

        private static float ResolveBoneY(Animator animator, Transform root, HumanBodyBones preferred, HumanBodyBones alternate, params string[] fallbackNames)
        {
            if (animator != null && animator.isHuman)
            {
                var bone = animator.GetBoneTransform(preferred) ?? animator.GetBoneTransform(alternate);
                if (bone != null) return bone.position.y;
            }
            var fallback = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => fallbackNames.Any(name => item.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || item.name.EndsWith(name, StringComparison.OrdinalIgnoreCase)));
            return fallback != null ? fallback.position.y : float.NaN;
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(root.position, Vector3.zero);
                return false;
            }
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return true;
        }

        internal static void ApplyMatteAvatarMaterials(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                try
                {
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.receiveShadows = true;
                    var materials = renderer.materials;
                    for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        var material = materials[materialIndex];
                        if (material == null) continue;
                    // Some Tripo imports ship with a custom shader that ignores the
                    // standard roughness/specular channels. Rebind those instances to
                    // URP Lit while carrying over albedo, normal and cutoff textures;
                    // this keeps the character recognisable but guarantees a matte,
                    // non-reflective response under the tomb's warm lights.
                    var matteShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (matteShader != null && material.shader != matteShader)
                    {
                        var matte = new Material(matteShader) { name = $"{material.name} · Matte" };
                        matte.enableInstancing = true;
                        CopyTexture(material, matte, "_BaseMap", "_MainTex");
                        CopyTexture(material, matte, "_BumpMap", "_NormalMap");
                        CopyTexture(material, matte, "_MaskMap", "_MetallicGlossMap");
                        var sourceColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
                            : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                        matte.SetColor("_BaseColor", new Color(sourceColor.r, sourceColor.g, sourceColor.b, 1f));
                        if (material.HasProperty("_BumpScale")) matte.SetFloat("_BumpScale", material.GetFloat("_BumpScale"));
                        materials[materialIndex] = matte;
                        material = matte;
                    }
                    // Imported Tripo characters use a few different Lit/Standard
                    // variants. Set every compatible channel rather than relying on
                    // one shader's property names, while preserving each material's
                    // original albedo and normal textures.
                        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                        if (material.HasProperty("_WorkflowMode")) material.SetFloat("_WorkflowMode", 0f);
                        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                        if (material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap", null);
                        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.045f);
                        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.035f);
                        if (material.HasProperty("_GlossMapScale")) material.SetFloat("_GlossMapScale", 0.035f);
                        if (material.HasProperty("_Roughness")) material.SetFloat("_Roughness", 0.94f);
                        if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
                        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
                        if (material.HasProperty("_Specular")) material.SetFloat("_Specular", 0f);
                        if (material.HasProperty("_Reflectance")) material.SetFloat("_Reflectance", 0f);
                        if (material.HasProperty("_ClearCoatMask")) material.SetFloat("_ClearCoatMask", 0f);
                        if (material.HasProperty("_ClearCoatSmoothness")) material.SetFloat("_ClearCoatSmoothness", 0f);
                        if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", new Color(0.012f, 0.012f, 0.012f, 1f));
                        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                        material.DisableKeyword("_CLEARCOAT");
                        material.DisableKeyword("_CLEARCOATMAP");
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    }
                    renderer.materials = materials;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[GuandanAvatar] {renderer.name} 某个导入网格无法实例化材质，已跳过但继续绑定玩法：{exception.Message}");
                }
            }
        }

        /// <summary>
        /// Rebuilds smooth normals on a runtime-only mesh copy. Tripo exports often
        /// split vertices at UV seams, so RecalculateNormals alone still leaves hard
        /// triangular highlights. Averaging normals for coincident rest-pose vertices
        /// keeps the existing skin weights/UVs while removing those visible breaks.
        /// </summary>
        internal static void SmoothAvatarMeshes(Transform root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = renderer.sharedMesh;
                if (source == null) continue;
                try
                {
                    var mesh = UnityEngine.Object.Instantiate(source);
                    mesh.name = $"{source.name} · RuntimeSmooth";
                    mesh.RecalculateNormals();
                    var vertices = mesh.vertices;
                    var normals = mesh.normals;
                    if (vertices.Length == normals.Length && vertices.Length > 0)
                    {
                        var sums = new Dictionary<Vector3Int, Vector3>(vertices.Length);
                        var counts = new Dictionary<Vector3Int, int>(vertices.Length);
                        for (var index = 0; index < vertices.Length; index++)
                        {
                            var key = QuantizeVertex(vertices[index]);
                            sums.TryGetValue(key, out var sum);
                            counts.TryGetValue(key, out var count);
                            sums[key] = sum + normals[index].normalized;
                            counts[key] = count + 1;
                        }
                        for (var index = 0; index < vertices.Length; index++)
                        {
                            var key = QuantizeVertex(vertices[index]);
                            var averaged = sums[key] / Mathf.Max(1, counts[key]);
                            normals[index] = averaged.sqrMagnitude > 0.0001f
                                ? averaged.normalized
                                : Vector3.up;
                        }
                        mesh.normals = normals;
                        if (mesh.tangents != null && mesh.tangents.Length == vertices.Length)
                            mesh.RecalculateTangents();
                    }
                    renderer.sharedMesh = mesh;
                }
                catch (Exception exception)
                {
                    // Non-readable imported meshes are still valid for display and
                    // animation; leave them untouched rather than breaking the table.
                    Debug.LogWarning($"[GuandanAvatar] {renderer.name} 网格平滑跳过：{exception.Message}");
                }
            }
        }

        private static Vector3Int QuantizeVertex(Vector3 vertex)
        {
            const float precision = 10000f;
            return new Vector3Int(
                Mathf.RoundToInt(vertex.x * precision),
                Mathf.RoundToInt(vertex.y * precision),
                Mathf.RoundToInt(vertex.z * precision));
        }

        private static void CopyTexture(Material source, Material target, string targetProperty, string sourceProperty)
        {
            if (source == null || target == null || !target.HasProperty(targetProperty)) return;
            if (source.HasProperty(sourceProperty))
            {
                target.SetTexture(targetProperty, source.GetTexture(sourceProperty));
                target.SetTextureScale(targetProperty, source.GetTextureScale(sourceProperty));
                target.SetTextureOffset(targetProperty, source.GetTextureOffset(sourceProperty));
            }
        }

        private IEnumerator ActivateAvatarMotion(Transform root, PlayerSeat seat, int avatarIndex, float phase)
        {
            yield return null;
            ForceSittingAnimator(root, avatarIndex);
            var motion = root.gameObject.AddComponent<SeatedAvatarMotion>();
            motion.Configure(phase);
            avatarMotions[seat] = motion;
            ApplySeatLayout(seat);
            AddAvatarTarget(root.gameObject, seat);
        }

        private static void ForceSittingAnimator(Transform root, int avatarIndex)
        {
            if (avatarIndex < 1 || avatarIndex > 5) return;
            var sittingController = Resources.Load<RuntimeAnimatorController>($"GuandanAvatars/Avatar_{avatarIndex}_Sit");
            if (sittingController == null)
            {
                Debug.LogWarning($"[Guandan] 未找到 Avatar_{avatarIndex}_Sit 控制器，保留人物原有动画。");
                return;
            }
            var animatorHost = FindAnimatorHost(root);
            foreach (var candidate in root.GetComponentsInChildren<Animator>(true))
            {
                if (candidate == null || candidate.transform == animatorHost) continue;
                candidate.enabled = false;
                candidate.runtimeAnimatorController = null;
            }
            var animator = animatorHost.GetComponent<Animator>() ?? animatorHost.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = sittingController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
        }

        private static Transform FindAnimatorHost(Transform root)
        {
            if (root == null) return null;
            var armature = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item != null && item.name.Equals("Armature", StringComparison.OrdinalIgnoreCase));
            return armature != null && armature.parent != null ? armature.parent : root;
        }

        private static Animator FindAnimator(Transform root)
        {
            if (root == null) return null;
            var animators = root.GetComponentsInChildren<Animator>(true);
            Animator fallback = null;
            foreach (var candidate in animators)
            {
                if (candidate == null) continue;
                fallback ??= candidate;
                if (candidate.runtimeAnimatorController != null) return candidate;
            }
            return fallback;
        }

        private IEnumerator TomatoReactionRoutine(Transform avatar)
        {
            var group = new GameObject("Reaction · 番茄酱");
            group.transform.SetParent(runtimeRoot, false);
            var towardTable = Vector3.ProjectOnPlane(tableCenter - avatar.position, Vector3.up).normalized;
            group.transform.position = new Vector3(
                avatar.position.x + towardTable.x * 0.28f,
                avatar.position.y + 0.025f,
                avatar.position.z + towardTable.z * 0.28f);
            FaceReactionToCamera(group.transform);
            var sauce = CreateEffectPrimitive(PrimitiveType.Cylinder, "Sauce", group.transform, new Color(0.64f, 0.025f, 0.012f));
            sauce.transform.localScale = new Vector3(0.50f, 0.012f, 0.38f);
            for (var index = 0; index < 7; index++)
            {
                var angle = index / 7f * Mathf.PI * 2f;
                var drop = CreateEffectPrimitive(PrimitiveType.Sphere, $"Splash_{index + 1}", group.transform, new Color(0.78f, 0.045f, 0.018f));
                drop.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.48f, 0.014f, Mathf.Sin(angle) * 0.34f);
                drop.transform.localScale = new Vector3(0.10f + index % 3 * 0.025f, 0.018f, 0.075f);
            }
            group.transform.localScale = Vector3.zero;
            for (var time = 0f; time < 2.8f; time += Time.deltaTime)
            {
                var appear = Mathf.Clamp01(time / 0.20f);
                var disappear = time < 2.25f ? 1f : 1f - Mathf.Clamp01((time - 2.25f) / 0.55f);
                group.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, appear) * disappear;
                FaceReactionToCamera(group.transform);
                yield return null;
            }
            Destroy(group);
        }

        private static void FaceReactionToCamera(Transform reaction)
        {
            if (reaction == null) return;
            var camera = Camera.main ?? FindFirstObjectByType<Camera>();
            if (camera == null) return;
            var direction = Vector3.ProjectOnPlane(camera.transform.position - reaction.position, Vector3.up);
            if (direction.sqrMagnitude < 0.0001f) return;
            // Keep the sauce on the ground while rotating its authored front toward
            // the current viewer, so a head turn never reveals a sideways decal.
            reaction.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private IEnumerator FlowerReactionRoutine(Transform avatar)
        {
            var group = new GameObject("Reaction · 鲜花环绕");
            group.transform.SetParent(runtimeRoot, false);
            var flowers = new Transform[10];
            var colors = new[]
            {
                new Color(0.96f, 0.22f, 0.43f),
                new Color(1f, 0.62f, 0.18f),
                new Color(0.96f, 0.72f, 0.78f),
            };
            for (var index = 0; index < flowers.Length; index++)
                flowers[index] = CreateFlower(group.transform, $"Flower_{index + 1}", colors[index % colors.Length]);

            for (var time = 0f; time < 2.25f; time += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(time / 2.25f);
                var envelope = Mathf.Sin(progress * Mathf.PI);
                for (var index = 0; index < flowers.Length; index++)
                {
                    var angle = index / (float)flowers.Length * Mathf.PI * 2f + progress * Mathf.PI * 2f;
                    var radius = 0.68f + Mathf.Sin(angle * 2f + time) * 0.06f;
                    flowers[index].position = avatar.position + new Vector3(
                        Mathf.Cos(angle) * radius,
                        0.72f + index % 3 * 0.20f + progress * 0.30f,
                        Mathf.Sin(angle) * radius);
                    flowers[index].localScale = Vector3.one * envelope;
                    flowers[index].Rotate(Vector3.up, 120f * Time.deltaTime, Space.World);
                }
                yield return null;
            }
            Destroy(group);
        }

        private IEnumerator BellPulseRoutine(Vector3 position)
        {
            var go = new GameObject("BellPulse · 声波环");
            go.transform.SetParent(runtimeRoot, false);
            go.transform.position = position + Vector3.up * 0.08f;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 40;
            line.startWidth = 0.032f;
            line.endWidth = 0.032f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            for (var time = 0f; time < 0.52f; time += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(time / 0.52f);
                var radius = Mathf.Lerp(0.12f, 1.05f, progress);
                for (var index = 0; index < line.positionCount; index++)
                {
                    var angle = index / (float)line.positionCount * Mathf.PI * 2f;
                    line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }
                line.material.color = new Color(1f, 0.72f, 0.26f, 1f - progress);
                yield return null;
            }
            Destroy(go);
        }

        private static Transform CreateFlower(Transform parent, string name, Color color)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var petalMaterial = new Material(Shader.Find("Standard")) { color = color };
            for (var index = 0; index < 5; index++)
            {
                var angle = index / 5f * Mathf.PI * 2f;
                var petal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                petal.name = $"Petal_{index + 1}";
                petal.transform.SetParent(root, false);
                petal.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.075f, Mathf.Sin(angle) * 0.075f, 0f);
                petal.transform.localScale = new Vector3(0.075f, 0.11f, 0.035f);
                petal.transform.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
                petal.GetComponent<Renderer>().sharedMaterial = petalMaterial;
                Destroy(petal.GetComponent<Collider>());
            }
            var center = CreateEffectPrimitive(PrimitiveType.Sphere, "Center", root, new Color(0.96f, 0.70f, 0.12f));
            center.transform.localScale = Vector3.one * 0.075f;
            return root;
        }

        private static GameObject CreateEffectPrimitive(PrimitiveType type, string name, Transform parent, Color color)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            var collider = item.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            item.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
            return item;
        }

        private static string SeatLabel(PlayerSeat seat) => seat switch
        {
            PlayerSeat.East => "东家",
            PlayerSeat.North => "北家",
            PlayerSeat.West => "西家",
            _ => "南家",
        };
    }
}
