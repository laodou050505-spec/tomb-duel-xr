using UnityEngine;
using UnityEngine.Rendering;

namespace Guandan.Scene
{
    [ExecuteAlways]
    public sealed class TombRoomBuilder : MonoBehaviour
    {
        [Header("Scene dimensions")]
        [Min(6f)] public float roomWidth = 18f;
        [Min(6f)] public float roomDepth = 18f;
        [Min(3f)] public float roomHeight = 5.8f;
        [Min(1f)] public float tableRadius = 3.7f;
        [Tooltip("关闭后，已生成的物件不会因为 Inspector 改动而重建，便于在 Scene 中替换、移动和增删模型。")]
        public bool rebuildOnValidate = false;
        public bool includeDecor = true;

        [Header("Palette")]
        public Color earth = new Color(0.20f, 0.15f, 0.11f);
        public Color packedEarth = new Color(0.30f, 0.23f, 0.17f);
        public Color bronze = new Color(0.47f, 0.39f, 0.22f);
        public Color patina = new Color(0.18f, 0.39f, 0.34f);
        public Color cinnabar = new Color(0.60f, 0.16f, 0.11f);
        public Color warmLight = new Color(1f, 0.48f, 0.16f);

        private const string GeneratedRootName = "_Generated_墓穴空间";

        private void OnEnable()
        {
            if (rebuildOnValidate) Rebuild();
        }

        private void OnValidate()
        {
            if (rebuildOnValidate) Rebuild();
        }

        [ContextMenu("Rebuild Tomb Room")]
        public void Rebuild()
        {
            RemoveGenerated();
            var root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);

            CreatePrimitive(root.transform, "Floor_探方地面", PrimitiveType.Cube, new Vector3(0f, -0.15f, 0f), new Vector3(roomWidth, 0.3f, roomDepth), earth);
            CreatePrimitive(root.transform, "Ceiling_墓室顶", PrimitiveType.Cube, new Vector3(0f, roomHeight, 0f), new Vector3(roomWidth, 0.25f, roomDepth), earth);
            CreatePrimitive(root.transform, "Wall_North_夯土墙", PrimitiveType.Cube, new Vector3(0f, roomHeight * 0.5f, roomDepth * 0.5f), new Vector3(roomWidth, roomHeight, 0.35f), packedEarth);
            CreatePrimitive(root.transform, "Wall_South_夯土墙", PrimitiveType.Cube, new Vector3(0f, roomHeight * 0.5f, -roomDepth * 0.5f), new Vector3(roomWidth, roomHeight, 0.35f), packedEarth);
            CreatePrimitive(root.transform, "Wall_East_夯土墙", PrimitiveType.Cube, new Vector3(roomWidth * 0.5f, roomHeight * 0.5f, 0f), new Vector3(0.35f, roomHeight, roomDepth), packedEarth);
            CreatePrimitive(root.transform, "Wall_West_夯土墙", PrimitiveType.Cube, new Vector3(-roomWidth * 0.5f, roomHeight * 0.5f, 0f), new Vector3(0.35f, roomHeight, roomDepth), packedEarth);

            CreatePortal(root.transform);
            CreateTable(root.transform);
            CreateSeatMarkers(root.transform);
            CreateTreasureTrack(root.transform);
            CreateLighting(root.transform);
            if (includeDecor) CreateDecor(root.transform);
        }

        private void CreatePortal(Transform parent)
        {
            var portal = new GameObject("Portal_墓门与木构屏风");
            portal.transform.SetParent(parent, false);
            portal.transform.localPosition = new Vector3(0f, 0f, roomDepth * 0.5f - 0.4f);
            CreatePrimitive(portal.transform, "Pillar_L", PrimitiveType.Cube, new Vector3(-3.4f, roomHeight * 0.5f, 0f), new Vector3(0.65f, roomHeight, 0.7f), packedEarth);
            CreatePrimitive(portal.transform, "Pillar_R", PrimitiveType.Cube, new Vector3(3.4f, roomHeight * 0.5f, 0f), new Vector3(0.65f, roomHeight, 0.7f), packedEarth);
            CreatePrimitive(portal.transform, "Lintel_门楣", PrimitiveType.Cube, new Vector3(0f, roomHeight - 0.4f, 0f), new Vector3(7.5f, 0.65f, 0.75f), packedEarth);
            CreatePrimitive(portal.transform, "Screen_木构屏风", PrimitiveType.Cube, new Vector3(0f, roomHeight * 0.48f, -0.28f), new Vector3(5.8f, roomHeight * 0.75f, 0.10f), earth);
            for (var i = -2; i <= 2; i++)
            {
                CreatePrimitive(portal.transform, $"Screen_Rail_{i}", PrimitiveType.Cube, new Vector3(i * 1.0f, roomHeight * 0.48f, -0.36f), new Vector3(0.07f, roomHeight * 0.72f, 0.08f), bronze);
            }
        }

        private void CreateTable(Transform parent)
        {
            var table = new GameObject("Table_夯土探方牌桌");
            table.transform.SetParent(parent, false);
            table.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            CreatePrimitive(table.transform, "TableBase", PrimitiveType.Cylinder, Vector3.zero, new Vector3(tableRadius * 0.52f, 1.25f, tableRadius * 0.52f), earth);
            CreatePrimitive(table.transform, "TableRim", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(tableRadius * 1.12f, 0.22f, tableRadius * 1.12f), bronze);
            CreatePrimitive(table.transform, "TableSurface", PrimitiveType.Cylinder, new Vector3(0f, 0.98f, 0f), new Vector3(tableRadius, 0.08f, tableRadius), new Color(0.22f, 0.16f, 0.11f));
            for (var i = -2; i <= 2; i++)
            {
                CreatePrimitive(table.transform, $"Grid_H_{i}", PrimitiveType.Cube, new Vector3(0f, 1.03f, i * 1.55f), new Vector3(tableRadius * 1.8f, 0.02f, 0.025f), bronze);
                CreatePrimitive(table.transform, $"Grid_V_{i}", PrimitiveType.Cube, new Vector3(i * 1.55f, 1.03f, 0f), new Vector3(0.025f, 0.02f, tableRadius * 1.8f), bronze);
            }
            CreatePrimitive(table.transform, "CenterSeal", PrimitiveType.Cylinder, new Vector3(0f, 1.05f, 0f), new Vector3(0.66f, 0.03f, 0.66f), cinnabar);
        }

        private void CreateSeatMarkers(Transform parent)
        {
            var seats = new GameObject("Seats_四家座位与角色锚点");
            seats.transform.SetParent(parent, false);
            CreateSeat(seats.transform, "Seat_South_玩家", new Vector3(0f, 0f, -4.6f), patina, 180f);
            CreateSeat(seats.transform, "Seat_North_队友", new Vector3(0f, 0f, 4.6f), patina, 0f);
            CreateSeat(seats.transform, "Seat_East_对手", new Vector3(4.6f, 0f, 0f), cinnabar, -90f);
            CreateSeat(seats.transform, "Seat_West_对手", new Vector3(-4.6f, 0f, 0f), cinnabar, 90f);
        }

        private void CreateSeat(Transform parent, string name, Vector3 position, Color color, float rotation)
        {
            var seat = new GameObject(name);
            seat.transform.SetParent(parent, false);
            seat.transform.localPosition = position;
            seat.transform.localRotation = Quaternion.Euler(0f, rotation, 0f);
            CreatePrimitive(seat.transform, "Marker", PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.95f, 0.08f, 0.95f), color);
            var anchor = new GameObject("AvatarAnchor");
            anchor.transform.SetParent(seat.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        }

        private void CreateTreasureTrack(Transform parent)
        {
            var treasure = new GameObject("Treasure_夺宝轨道与中心宝箱");
            treasure.transform.SetParent(parent, false);
            treasure.transform.localPosition = new Vector3(0f, 0.08f, 6.75f);
            CreatePrimitive(treasure.transform, "TrackPlatform", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(12.8f, 0.35f, 2.7f), earth);
            for (var i = 0; i < 10; i++)
            {
                var x = -4.75f + i * 1.0f;
                CreatePrimitive(treasure.transform, $"Track_青队_{i + 1:00}", PrimitiveType.Cylinder, new Vector3(x, 0.2f, -0.55f), new Vector3(0.68f, 0.16f, 0.68f), patina);
                CreatePrimitive(treasure.transform, $"Track_朱队_{i + 1:00}", PrimitiveType.Cylinder, new Vector3(x, 0.2f, 0.55f), new Vector3(0.68f, 0.16f, 0.68f), cinnabar);
            }
            CreatePrimitive(treasure.transform, "TreasureChest_中心宝箱", PrimitiveType.Cube, new Vector3(5.65f, 0.85f, 0f), new Vector3(1.4f, 1.35f, 1.05f), bronze);
            CreatePrimitive(treasure.transform, "TreasureGlow", PrimitiveType.Sphere, new Vector3(5.65f, 1.55f, 0f), new Vector3(0.7f, 0.7f, 0.7f), warmLight);
        }

        private void CreateLighting(Transform parent)
        {
            var lighting = new GameObject("Lighting_青铜灯与暖光");
            lighting.transform.SetParent(parent, false);
            var positions = new[] { new Vector3(-6.2f, 2.3f, -4.2f), new Vector3(6.2f, 2.3f, -4.2f), new Vector3(-6.2f, 2.3f, 4.2f), new Vector3(6.2f, 2.3f, 4.2f) };
            for (var i = 0; i < positions.Length; i++)
            {
                var lamp = new GameObject($"BronzeLamp_{i + 1}");
                lamp.transform.SetParent(lighting.transform, false);
                lamp.transform.localPosition = positions[i];
                CreatePrimitive(lamp.transform, "LampBody", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.38f, 0.65f, 0.38f), bronze);
                var point = lamp.AddComponent<Light>();
                point.type = LightType.Point;
                point.color = warmLight;
                point.range = 6.5f;
                point.intensity = 7f;
                point.shadows = LightShadows.Soft;
            }
        }

        private void CreateDecor(Transform parent)
        {
            var decor = new GameObject("Decor_可替换墓葬陈设");
            decor.transform.SetParent(parent, false);
            CreatePrimitive(decor.transform, "RuinColumn_L", PrimitiveType.Cylinder, new Vector3(-7.6f, 1.15f, 4.8f), new Vector3(0.7f, 2.3f, 0.7f), packedEarth);
            CreatePrimitive(decor.transform, "RuinColumn_R", PrimitiveType.Cylinder, new Vector3(7.6f, 1.15f, 4.8f), new Vector3(0.7f, 2.3f, 0.7f), packedEarth);
            CreatePrimitive(decor.transform, "RelicTable_L", PrimitiveType.Cube, new Vector3(-6.8f, 0.55f, -6.3f), new Vector3(1.5f, 1.1f, 0.9f), earth);
            CreatePrimitive(decor.transform, "RelicTable_R", PrimitiveType.Cube, new Vector3(6.8f, 0.55f, -6.3f), new Vector3(1.5f, 1.1f, 0.9f), earth);
            CreatePrimitive(decor.transform, "PropBasket_Tomato", PrimitiveType.Cylinder, new Vector3(-3.35f, 1.35f, -4.0f), new Vector3(1.0f, 0.42f, 1.0f), packedEarth);
            CreatePrimitive(decor.transform, "PropBasket_Flower", PrimitiveType.Cylinder, new Vector3(3.35f, 1.35f, -4.0f), new Vector3(1.0f, 0.42f, 1.0f), packedEarth);
        }

        private GameObject CreatePrimitive(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = CreateMaterial(color);
            return go;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color, name = "MAT_Tomb_Editable" };
            material.SetFloat("_Metallic", 0.18f);
            material.SetFloat("_Smoothness", 0.28f);
            return material;
        }

        private void RemoveGenerated()
        {
            var old = transform.Find(GeneratedRootName);
            if (old == null) return;
            if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
        }
    }
}
