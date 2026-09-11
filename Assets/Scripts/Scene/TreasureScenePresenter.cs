using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Guandan.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Guandan.Scene
{
    /// <summary>Animates the two authored treasure-room tracks and the replaceable team pieces.</summary>
    public sealed class TreasureScenePresenter : MonoBehaviour
    {
        private const string TrackTilePrefix = "tripo_convert_837dc130-08b0-4532-88c3-f84119b271a1";
        private const string BluePiecePrefix = "tripo_convert_78d51c0b-20fb-45b5-8e1a-d4e720c754fa";
        private const string RedPiecePrefix = "tripo_convert_9f99bec5-da45-4749-862c-27964f41de26";
        private const string FlagPrefix = "tripo_convert_6aae2ee7-2e5a-4250-8a64-96d8baa205e1";
        private static readonly string[] ChestPrefixes =
        {
            "tripo_convert_5833946d-ac7a-49c2-8f93-b1da70214cf4", // c1
            "tripo_convert_846f4421-ec41-4628-8120-1a878f855c7e", // p23
            "tripo_convert_f8e50aa0-b2bb-4e50-8fea-d4e53c92ec6d"  // p4
        };
        private Transform sceneRoot;
        private Transform[] pieces;
        private Vector3[][] trackPositions;
        private float[][] trackTileTopY;
        private Vector3[] pieceBaseScales;
        private Quaternion[] pieceBaseRotations;
        private Coroutine finaleRoutine;
        private TreasureCelebration celebration;
        public bool ResultReady { get; private set; }
        private Transform treasureGlow;
        private ParticleSystem treasureParticles;
        private Light treasureLight;
        private ParticleSystem treasureHaze;
        private Material treasureGlowMaterial;
        private float treasureCelebration;
        private Text[] flagScoreTexts;
        private Coroutine[] moveRoutines;
        private Coroutine chestRoutine;

        public void Initialize(Transform root)
        {
            if (sceneRoot != null) return;
            sceneRoot = root;
            BuildTrackPositions();
            pieces = new Transform[2];
            moveRoutines = new Coroutine[2];
            pieces[0] = FindAuthoredPiece(0);
            pieces[1] = FindAuthoredPiece(1);
            pieceBaseScales = new Vector3[2];
            pieceBaseRotations = new Quaternion[2];
            for (var team = 0; team < pieces.Length; team++)
            {
                pieceBaseScales[team] = pieces[team] != null ? pieces[team].localScale : Vector3.one;
                pieceBaseRotations[team] = pieces[team] != null ? pieces[team].rotation : Quaternion.identity;
            }
            celebration = gameObject.AddComponent<TreasureCelebration>();
            BindScoreFlags();
            BindChestTreasureGlow();
            ResetPresentation();
        }

        public void ResetPresentation()
        {
            if (pieces == null || trackPositions == null) return;
            if (finaleRoutine != null) StopCoroutine(finaleRoutine);
            finaleRoutine = null;
            ResultReady = false;
            celebration?.Clear();
            if (chestRoutine != null) StopCoroutine(chestRoutine);
            chestRoutine = null;
            treasureCelebration = 0f;
            for (var team = 0; team < pieces.Length; team++)
            {
                if (moveRoutines[team] != null) StopCoroutine(moveRoutines[team]);
                moveRoutines[team] = null;
                if (pieces[team] != null)
                {
                    pieces[team].rotation = pieceBaseRotations[team];
                    pieces[team].localScale = pieceBaseScales[team];
                    pieces[team].position = TrackPosition(team, 0);
                }
            }
            if (treasureGlow != null) treasureGlow.gameObject.SetActive(true);
            if (treasureParticles != null) { treasureParticles.Clear(); treasureParticles.Play(); }
            if (treasureHaze != null) { treasureHaze.Clear(); treasureHaze.Play(); }
        }

        private void Update()
        {
            if (treasureLight == null) return;
            // Slow breathing illumination, never a full-screen flash or rapid strobe.
            treasureLight.intensity = 2.5f + 0.35f * Mathf.Sin(Time.time * 1.65f) + treasureCelebration * 2.2f;
        }

        private void OnDestroy()
        {
            if (treasureGlowMaterial != null) Destroy(treasureGlowMaterial);
        }

        public void MoveTeam(int team, int from, int to, bool openChest)
        {
            if (pieces == null || team < 0 || team >= pieces.Length) return;
            if (pieces[team] != null)
            {
                if (moveRoutines[team] != null) StopCoroutine(moveRoutines[team]);
                moveRoutines[team] = StartCoroutine(MoveRoutine(team, from, to));
            }
            if (openChest)
            {
                if (finaleRoutine != null) StopCoroutine(finaleRoutine);
                finaleRoutine = StartCoroutine(FinaleRoutine(team));
            }
        }

        private IEnumerator FinaleRoutine(int team)
        {
            ResultReady = false;
            while (moveRoutines[team] != null) yield return null;
            if (chestRoutine != null) StopCoroutine(chestRoutine);
            chestRoutine = StartCoroutine(OpenChestRoutine());
            var piece = pieces[team];
            if (piece != null && Camera.main != null)
            {
                // Imported model axes include the authored -90 degree FBX correction.
                // Rotate in WORLD yaw from its authored chest-facing +X, preserving that correction.
                var toward = Camera.main.transform.position - piece.position; toward.y = 0f;
                var target = Quaternion.FromToRotation(Vector3.right, toward.normalized) * pieceBaseRotations[team];
                var initial = piece.rotation;
                for (var t = 0f; t < 0.4f; t += Time.deltaTime)
                {
                    piece.rotation = Quaternion.Slerp(initial, target, Mathf.SmoothStep(0f, 1f, t / 0.4f));
                    yield return null;
                }
                piece.rotation = target;
            }
            ResultReady = true;
            FindFirstObjectByType<Guandan.UI.ScreenGameUi>()?.Refresh();
            if (team == 0) celebration.Play(Camera.main, sceneRoot);
            if (piece != null)
            {
                var origin = piece.position;
                var facing = piece.rotation;
                var side = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized : Vector3.right;
                // Six alternating hops, then settle. Never move the camera or deform bones.
                for (var hop = 0; hop < 6; hop++)
                {
                    var start = piece.position;
                    var end = origin + side * (hop == 5 ? 0f : (hop % 2 == 0 ? -0.22f : 0.22f));
                    for (var t = 0f; t < 0.64f; t += Time.deltaTime)
                    {
                        var p = Mathf.Clamp01(t / 0.64f);
                        var arc = Mathf.Sin(p * Mathf.PI);
                        piece.position = Vector3.Lerp(start, end, p) + Vector3.up * arc * 0.42f;
                        piece.rotation = Quaternion.AngleAxis((hop % 2 == 0 ? -1f : 1f) * arc * 7f, Vector3.up) * facing;
                        yield return null;
                    }
                    piece.position = end; piece.rotation = facing;
                }
                piece.position = origin; piece.rotation = facing;
            }
            finaleRoutine = null;
        }

        private void BuildTrackPositions()
        {
            var tiles = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item.name.StartsWith(TrackTilePrefix, StringComparison.Ordinal)
                    && item.position.y < 0.8f
                    && item.position.z > 20f
                    && item.position.z < 30f)
                .ToArray();
            var lanes = tiles.GroupBy(item => Mathf.RoundToInt(item.position.z * 10f))
                .OrderBy(group => group.Key)
                .Select(group => group.OrderBy(item => item.position.x).ToArray())
                .Where(group => group.Length >= TreasureRace.TrackLength + 1)
                .Take(2)
                .ToArray();
            if (lanes.Length == 2)
            {
                trackPositions = new Vector3[2][];
                trackTileTopY = new float[2][];
                for (var team = 0; team < 2; team++)
                {
                    var ordered = lanes[team];
                    var start = ordered[0].position;
                    trackPositions[team] = new Vector3[TreasureRace.TrackLength + 1];
                    trackTileTopY[team] = new float[TreasureRace.TrackLength + 1];
                    trackPositions[team][0] = start;
                    trackTileTopY[team][0] = TileTopY(ordered[0]);
                    for (var position = 1; position <= TreasureRace.TrackLength; position++)
                    {
                        trackPositions[team][position] = ordered[position].position;
                        trackTileTopY[team][position] = TileTopY(ordered[position]);
                    }
                }
                return;
            }

            // Fallback for an older editable scene without authored route tiles.
            trackPositions = new Vector3[2][];
            trackTileTopY = new float[2][];
            for (var team = 0; team < 2; team++)
            {
                trackPositions[team] = new Vector3[TreasureRace.TrackLength + 1];
                trackTileTopY[team] = new float[TreasureRace.TrackLength + 1];
                for (var position = 0; position <= TreasureRace.TrackLength; position++)
                {
                    trackPositions[team][position] = new Vector3(-6.99f + position * 1.08f, 0.58f, 25.22f + team * 1.30f);
                    trackTileTopY[team][position] = float.NaN;
                }
            }
        }

        private static float TileTopY(Transform tile)
        {
            if (tile == null) return float.NaN;
            var renderers = tile.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return tile.position.y;
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds.max.y;
        }

        private Transform FindAuthoredPiece(int team)
        {
            var prefix = team == 0 ? BluePiecePrefix : RedPiecePrefix;
            var authored = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item != null && item.name.StartsWith(prefix, StringComparison.Ordinal)
                    && item.position.z > 23f && item.position.z < 29f);
            if (authored == null)
                Debug.LogWarning($"[Guandan] 未找到{(team == 0 ? "青队" : "朱队")}现有棋子，不生成方块占位物。");
            return authored;
        }

        private void BindScoreFlags()
        {
            var flags = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item != null && item.name.StartsWith(FlagPrefix, StringComparison.Ordinal)
                    && item.position.z > 27f && item.position.z < 31f)
                .OrderBy(item => item.position.x)
                .Take(2)
                .ToArray();
            flagScoreTexts = new Text[2];
            for (var index = 0; index < flags.Length; index++)
            {
                var team = index == 0 ? 0 : 1;
                flagScoreTexts[team] = CreateFlagScore(flags[index], team);
            }
        }

        private static Text CreateFlagScore(Transform flag, int team)
        {
            var existing = flag.Find(team == 0 ? "FlagScore_青队" : "FlagScore_朱队");
            if (existing != null) return existing.GetComponentInChildren<Text>();
            var canvasObject = new GameObject(team == 0 ? "FlagScore_青队" : "FlagScore_朱队", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(flag, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(420f, 520f);
            canvasRect.localPosition = new Vector3(0f, 0.49f, -0.148f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.00145f;

            var plate = new GameObject("ScorePlate", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(canvasObject.transform, false);
            var plateRect = plate.GetComponent<RectTransform>();
            plateRect.sizeDelta = new Vector2(390f, 480f);
            plate.GetComponent<Image>().color = team == 0
                ? new Color(0.035f, 0.19f, 0.17f, 0.84f)
                : new Color(0.28f, 0.055f, 0.035f, 0.84f);
            plate.GetComponent<Image>().raycastTarget = false;

            var textObject = new GameObject("Score", typeof(RectTransform), typeof(Text), typeof(Shadow));
            textObject.transform.SetParent(plate.transform, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(350f, 430f);
            var text = textObject.GetComponent<Text>();
            text.font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Noto Sans CJK SC", "Arial" }, 42);
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 39;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 0.88f, 0.56f, 1f);
            text.raycastTarget = false;
            text.text = team == 0 ? "青队\n打 2\n\n0 / 12 格" : "朱队\n打 2\n\n0 / 12 格";
            var shadow = textObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(3f, -3f);
            return text;
        }

        public void UpdateFlagScores(int blueLevel, int redLevel, int bluePosition, int redPosition)
        {
            if (flagScoreTexts == null) return;
            if (flagScoreTexts.Length > 0 && flagScoreTexts[0] != null)
                flagScoreTexts[0].text = $"青队\n打 {GuandanMatchEngine.RankLabel(blueLevel)}\n\n{bluePosition} / {TreasureRace.TrackLength} 格";
            if (flagScoreTexts.Length > 1 && flagScoreTexts[1] != null)
                flagScoreTexts[1].text = $"朱队\n打 {GuandanMatchEngine.RankLabel(redLevel)}\n\n{redPosition} / {TreasureRace.TrackLength} 格";
        }

        private void BindChestTreasureGlow()
        {
            var chest = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item != null && item.position.z > 23f && item.position.z < 34f)
                .Where(item => item.name.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                    || ChestPrefixes.Any(prefix => item.name.StartsWith(prefix, StringComparison.Ordinal)))
                .OrderBy(item => item.name.StartsWith(ChestPrefixes[0], StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(item => Vector3.SqrMagnitude(item.position - new Vector3(0f, 0f, 26.5f)))
                .FirstOrDefault();
            if (chest == null)
            {
                Debug.LogWarning("[Guandan] 未找到用户的真实夺宝宝箱，未创建假箱盖。");
                return;
            }
            var renderers = chest.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(chest.position, Vector3.one);
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var root = new GameObject("TreasureGlow · 宝箱内部金光");
            root.transform.SetParent(sceneRoot, false);
            root.transform.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.43f, bounds.center.z);
            treasureGlow = root.transform;
            treasureLight = root.AddComponent<Light>();
            treasureLight.type = LightType.Point;
            treasureLight.color = new Color(1f, 0.78f, 0.30f);
            treasureLight.range = 3.2f;
            treasureLight.intensity = 1.65f;
            treasureLight.shadows = LightShadows.None;

            treasureParticles = root.AddComponent<ParticleSystem>();
            treasureParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = treasureParticles.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.23f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.21f, 0.8f), new Color(1f, 0.95f, 0.64f, 1f));
            main.maxParticles = 110;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = treasureParticles.emission;
            emission.rateOverTime = 38f;
            var shape = treasureParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Mathf.Max(0.3f, bounds.size.x * 0.62f), 0.12f, Mathf.Max(0.2f, bounds.size.z * 0.32f));
            var velocity = treasureParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.035f, 0.035f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.22f, 0.48f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.035f, 0.035f);
            var color = treasureParticles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.76f, 0.30f), 0f), new GradientColorKey(new Color(1f, 0.95f, 0.62f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var particleRenderer = treasureParticles.GetComponent<ParticleSystemRenderer>();
            // Explicit Resources reference keeps this procedural shader in Android builds.
            var particleShader = Resources.Load<Shader>("GuandanUI/TreasureSparkle");
            if (particleShader != null)
            {
                treasureGlowMaterial = new Material(particleShader);
                particleRenderer.sharedMaterial = treasureGlowMaterial;
            }
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            var hazeObject = new GameObject("Treasure mouth · soft gold");
            hazeObject.transform.SetParent(root.transform, false);
            treasureHaze = hazeObject.AddComponent<ParticleSystem>();
            treasureHaze.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var hazeMain = treasureHaze.main;
            hazeMain.loop = true; hazeMain.prewarm = true;
            hazeMain.startLifetime = 2.4f; hazeMain.startSpeed = 0f;
            hazeMain.startSize = Mathf.Clamp(bounds.size.x * 0.82f, 0.8f, 2.2f);
            hazeMain.startColor = new Color(1f, 0.74f, 0.24f, 0.22f);
            hazeMain.maxParticles = 6;
            var hazeEmission = treasureHaze.emission; hazeEmission.rateOverTime = 2f;
            var hazeShape = treasureHaze.shape; hazeShape.enabled = false;
            var hazeColor = treasureHaze.colorOverLifetime; hazeColor.enabled = true; hazeColor.color = gradient;
            var hazeRenderer = treasureHaze.GetComponent<ParticleSystemRenderer>();
            hazeRenderer.sharedMaterial = treasureGlowMaterial;
            hazeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private IEnumerator MoveRoutine(int team, int from, int to)
        {
            var piece = pieces[team];
            from = Mathf.Clamp(from, 0, TreasureRace.TrackLength);
            to = Mathf.Clamp(to, 0, TreasureRace.TrackLength);
            var direction = to >= from ? 1 : -1;
            var current = from;
            piece.position = TrackPosition(team, current);
            while (current != to)
            {
                var next = current + direction;
                var start = TrackPosition(team, current);
                var end = TrackPosition(team, next);
                const float duration = 0.36f;
                for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
                {
                    var progress = Mathf.Clamp01(elapsed / duration);
                    var eased = progress * progress * (3f - 2f * progress);
                    var position = Vector3.Lerp(start, end, eased);
                    position.y += Mathf.Sin(progress * Mathf.PI) * 0.42f;
                    piece.position = position;
                    var squash = Mathf.Sin(progress * Mathf.PI);
                    piece.localScale = Vector3.Scale(piece.localScale, new Vector3(1f + squash * 0.0015f, 1f - squash * 0.0012f, 1f + squash * 0.0015f));
                    yield return null;
                }
                piece.position = end;
                piece.localScale = pieceBaseScales != null && pieceBaseScales.Length > team
                    ? pieceBaseScales[team]
                    : piece.localScale;
                FindFirstObjectByType<GameDirector>()?.PlayTreasureStepFeedback();
                yield return LandingPulse(end, team);
                yield return new WaitForSeconds(0.08f);
                current = next;
            }
            moveRoutines[team] = null;
        }

        private IEnumerator LandingPulse(Vector3 position, int team)
        {
            var go = new GameObject("棋子落点");
            go.transform.SetParent(sceneRoot, false);
            var groundY = position.y - PieceBottomOffset(pieces != null && team >= 0 && team < pieces.Length ? pieces[team] : null);
            go.transform.position = new Vector3(position.x, groundY + 0.012f, position.z);
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.positionCount = 32;
            line.startWidth = line.endWidth = 0.025f;
            var lineShader = ResolveEffectShader();
            if (lineShader != null) line.material = new Material(lineShader);
            var baseColor = team == 0 ? new Color(0.18f, 0.86f, 0.71f) : new Color(1f, 0.28f, 0.15f);
            for (var time = 0f; time < 0.16f; time += Time.deltaTime)
            {
                var progress = time / 0.16f;
                var radius = Mathf.Lerp(0.12f, 0.42f, progress);
                for (var index = 0; index < line.positionCount; index++)
                {
                    var angle = index / (float)line.positionCount * Mathf.PI * 2f;
                    line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }
                line.material.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - progress);
                yield return null;
            }
            Destroy(go);
        }

        private static Shader ResolveEffectShader()
        {
            // Shader names differ between the Editor and stripped player builds. Keep
            // the effect optional so a missing particle shader can never stop gameplay.
            return Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
        }

        private IEnumerator OpenChestRoutine()
        {
            treasureParticles?.Emit(35);
            for (var time = 0f; time < 1.1f; time += Time.deltaTime)
            {
                treasureCelebration = Mathf.Sin(Mathf.Clamp01(time / 1.1f) * Mathf.PI);
                yield return null;
            }
            treasureCelebration = 0.3f;
            chestRoutine = null;
        }

        private Vector3 TrackPosition(int team, int position)
        {
            if (trackPositions != null && team >= 0 && team < trackPositions.Length && trackPositions[team] != null)
            {
                var index = Mathf.Clamp(position, 0, TreasureRace.TrackLength);
                var raw = trackPositions[team][index];
                if (trackTileTopY != null && team < trackTileTopY.Length
                    && trackTileTopY[team] != null && index < trackTileTopY[team].Length
                    && !float.IsNaN(trackTileTopY[team][index]))
                {
                    raw.y = trackTileTopY[team][index] + PieceBottomOffset(pieces != null && team < pieces.Length ? pieces[team] : null) + 0.012f;
                }
                return raw;
            }
            return new Vector3(-6.99f + position * 1.08f, 0.58f, 25.22f + team * 1.30f);
        }

        private static float PieceBottomOffset(Transform piece)
        {
            if (piece == null) return 0.58f;
            var renderers = piece.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0.58f;
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return piece.position.y - bounds.min.y;
        }
    }
}
