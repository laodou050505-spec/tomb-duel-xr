using System;

namespace Guandan.Game
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public string Name;
        public string Title;
        public string Description;
        public string Reaction;
        public string[] Traits;
        public float RiskBias;
        public float BombBias;
        public float ThinkMin;
        public float ThinkMax;

        public PlayerProfile(
            string name,
            string title,
            string description,
            string reaction,
            string[] traits,
            float riskBias,
            float bombBias,
            float thinkMin,
            float thinkMax)
        {
            Name = name;
            Title = title;
            Description = description;
            Reaction = reaction;
            Traits = traits;
            RiskBias = riskBias;
            BombBias = bombBias;
            ThinkMin = thinkMin;
            ThinkMax = thinkMax;
        }
    }
}
