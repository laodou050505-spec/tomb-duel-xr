using System;
using Guandan.Game;
using UnityEngine;

namespace Guandan.Scene
{
    [Serializable]
    public sealed class AvatarSeatLayout
    {
        public Vector3 positionOffset = Vector3.zero;
        [Range(0.65f, 1.65f)] public float scale = 1f;
        [Range(-45f, 45f)] public float yawOffset;
        [Range(0.65f, 1.45f)] public float chairHeightMultiplier = 1f;
    }

    [CreateAssetMenu(fileName = "AvatarLayoutSettings", menuName = "Guandan/Avatar Layout Settings")]
    public sealed class AvatarLayoutSettings : ScriptableObject
    {
        public const string ResourceName = "AvatarLayoutSettings";

        [Range(0.75f, 1.45f)] public float globalScale = 1f;
        [Range(-0.45f, 0.45f)] public float chestToTableOffset;
        [Range(0.02f, 0.28f)] public float chairToHipGap = 0.08f;
        public AvatarSeatLayout east = new();
        public AvatarSeatLayout north = new();
        public AvatarSeatLayout west = new();

        public AvatarSeatLayout ForSeat(PlayerSeat seat)
        {
            return seat switch
            {
                PlayerSeat.East => east,
                PlayerSeat.North => north,
                PlayerSeat.West => west,
                _ => north,
            };
        }

        public static AvatarLayoutSettings LoadOrCreateRuntimeDefault()
        {
            var settings = Resources.Load<AvatarLayoutSettings>(ResourceName);
            return settings != null ? settings : CreateInstance<AvatarLayoutSettings>();
        }
    }
}
