using UnityEngine;

namespace Guandan.Game
{
    /// <summary>Small procedural cue set so the prototype keeps the web version's audible state changes without external assets.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private AudioSource musicSource;
        private AudioSource cueSource;
        private AudioClip cardCue;
        private AudioClip dealCue;
        private AudioClip bombCue;
        private AudioClip tributeCue;
        private AudioClip returnTributeCue;
        private AudioClip rewardCue;
        private AudioClip chestCue;
        private AudioClip flowerCue;
        private AudioClip tomatoCue;
        private AudioClip bellCue;
        private AudioClip uiClickCue;
        private AudioClip pickupCue;
        private AudioClip passCue;
        private AudioClip treasureStepCue;

        public bool Muted
        {
            get => musicSource != null && musicSource.mute;
            set
            {
                if (musicSource != null) musicSource.mute = value;
                if (cueSource != null) cueSource.mute = value;
            }
        }

        private void Awake()
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.volume = 0.10f;
            musicSource.spatialBlend = 0f;
            musicSource.clip = CreatePentatonicLoop();
            musicSource.Play();

            cueSource = gameObject.AddComponent<AudioSource>();
            cueSource.volume = 0.34f;
            cueSource.spatialBlend = 0f;
            cardCue = CreateTone("Card", 520f, 0.075f, 0.22f);
            dealCue = CreateTone("Deal", 250f, 0.045f, 0.10f);
            bombCue = CreateTone("Bomb", 90f, 0.42f, 0.72f);
            tributeCue = CreateTone("Tribute", 330f, 0.22f, 0.34f);
            returnTributeCue = CreateTone("ReturnTribute", 430f, 0.20f, 0.30f);
            rewardCue = CreateTone("Reward", 660f, 0.42f, 0.38f);
            chestCue = CreateTone("Chest", 880f, 0.72f, 0.42f);
            flowerCue = CreateFlowerCue();
            tomatoCue = CreateSplashCue();
            bellCue = CreateBellCue();
            uiClickCue = CreateTone("UiClick", 690f, 0.045f, 0.14f);
            pickupCue = CreateTone("Pickup", 410f, 0.085f, 0.18f);
            passCue = CreateWhooshCue();
            treasureStepCue = CreateTone("TreasureStep", 185f, 0.11f, 0.20f);
        }

        public void Card() => Play(cardCue);
        public void Deal() => Play(dealCue);
        public void Bomb() => Play(bombCue);
        public void Tribute() => Play(tributeCue);
        public void ReturnTribute() => Play(returnTributeCue);
        public void Reward() => Play(rewardCue);
        public void Chest() => Play(chestCue);
        public void Flower() => Play(flowerCue);
        public void Tomato() => Play(tomatoCue);
        public void Bell() => Play(bellCue);
        public void UiClick() => Play(uiClickCue);
        public void Pickup(SocialPropType type) => Play(pickupCue);
        public void Pass() => Play(passCue);
        public void TreasureStep() => Play(treasureStepCue);

        private void Play(AudioClip clip)
        {
            if (clip != null) cueSource.PlayOneShot(clip);
        }

        private static AudioClip CreateTone(string name, float frequency, float duration, float gain)
        {
            const int sampleRate = 22050;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var time = i / (float)sampleRate;
                var envelope = Mathf.Pow(1f - i / (float)samples, 2f);
                data[i] = Mathf.Sin(time * frequency * Mathf.PI * 2f) * envelope * gain;
            }
            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreatePentatonicLoop()
        {
            const int sampleRate = 22050;
            const float duration = 8f;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            var notes = new[] { 220f, 247f, 294f, 330f, 392f, 330f, 294f, 247f };
            var noteSamples = samples / notes.Length;
            for (var i = 0; i < samples; i++)
            {
                var note = notes[Mathf.Min(notes.Length - 1, i / noteSamples)];
                var local = (i % noteSamples) / (float)noteSamples;
                var envelope = Mathf.Sin(local * Mathf.PI) * 0.16f;
                data[i] = (Mathf.Sin(i / (float)sampleRate * note * Mathf.PI * 2f)
                           + Mathf.Sin(i / (float)sampleRate * note * 0.5f * Mathf.PI * 2f) * 0.35f) * envelope;
            }
            var clip = AudioClip.Create("PentatonicTombLoop", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateBellCue()
        {
            const int sampleRate = 22050;
            const float duration = 1.15f;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)sampleRate;
                var envelope = Mathf.Exp(-2.8f * t);
                data[i] = (Mathf.Sin(t * 1660f * Mathf.PI * 2f)
                    + Mathf.Sin(t * 2410f * Mathf.PI * 2f) * 0.48f
                    + Mathf.Sin(t * 3320f * Mathf.PI * 2f) * 0.18f) * envelope * 0.48f;
            }
            var clip = AudioClip.Create("TombBell", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateFlowerCue()
        {
            const int sampleRate = 22050;
            const float duration = 0.62f;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            var notes = new[] { 740f, 988f, 1244f };
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)sampleRate;
                var value = 0f;
                for (var note = 0; note < notes.Length; note++)
                {
                    var start = note * 0.105f;
                    if (t < start) continue;
                    var local = t - start;
                    value += Mathf.Sin(local * notes[note] * Mathf.PI * 2f) * Mathf.Exp(-7.2f * local) * 0.24f;
                }
                data[i] = value;
            }
            var clip = AudioClip.Create("FlowerChime", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateSplashCue()
        {
            const int sampleRate = 22050;
            const float duration = 0.38f;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            var random = new System.Random(7319);
            var filtered = 0f;
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)sampleRate;
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered = filtered * 0.83f + noise * 0.17f;
                var envelope = Mathf.Exp(-9f * t);
                var body = Mathf.Sin(t * 118f * Mathf.PI * 2f) * Mathf.Exp(-13f * t) * 0.30f;
                data[i] = filtered * envelope * 0.68f + body;
            }
            var clip = AudioClip.Create("TomatoSplash", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateWhooshCue()
        {
            const int sampleRate = 22050;
            const float duration = 0.16f;
            var samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            var random = new System.Random(1481);
            var filtered = 0f;
            for (var i = 0; i < samples; i++)
            {
                var progress = i / (float)samples;
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered = filtered * 0.72f + noise * 0.28f;
                data[i] = filtered * Mathf.Sin(progress * Mathf.PI) * 0.18f;
            }
            var clip = AudioClip.Create("PassWhoosh", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
