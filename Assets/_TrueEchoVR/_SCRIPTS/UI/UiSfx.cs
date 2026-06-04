using UnityEngine;

namespace TEVR
{
    /// <summary>
    /// Singleton that procedurally synthesizes short UI feedback sounds (hover, click,
    /// success, error) at runtime and plays them via a single AudioSource.
    /// No audio assets are required — every clip is generated in <see cref="Awake"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class UiSfx : MonoBehaviour
    {
        public static UiSfx Instance { get; private set; }

        private const int SampleRate = 44100;

        [SerializeField] private AudioSource audioSource;

        private AudioClip _hoverClip;
        private AudioClip _clickClip;
        private AudioClip _successClip;
        private AudioClip _errorClip;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource != null)
            {
                audioSource.playOnAwake = false;
            }

            BuildClips();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void BuildClips()
        {
            // Hover: soft high tick (~880Hz, very quiet, ~0.04s).
            _hoverClip = MakeTone("UiHover", 0.04f, 880f, 880f, 0.18f, WaveType.Sine);

            // Click: crisp blip sweeping 1200Hz -> 600Hz (~0.08s).
            _clickClip = MakeTone("UiClick", 0.08f, 1200f, 600f, 0.5f, WaveType.Triangle);

            // Success: two-note rising chime (660Hz then 990Hz).
            _successClip = MakeChime("UiSuccess", 660f, 990f, 0.45f);

            // Error: low buzz (~160Hz, square-ish, ~0.18s).
            _errorClip = MakeTone("UiError", 0.18f, 160f, 150f, 0.4f, WaveType.Square);
        }

        private enum WaveType { Sine, Triangle, Square }

        private static AudioClip MakeTone(string name, float duration, float startFreq, float endFreq, float volume, WaveType wave)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            double phase = 0d;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = Mathf.Lerp(startFreq, endFreq, t);
                phase += 2d * Mathf.PI * freq / SampleRate;

                float raw = WaveSample(wave, (float)phase);
                samples[i] = raw * Envelope(t) * volume;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip MakeChime(string name, float freqA, float freqB, float volume)
        {
            float noteDuration = 0.09f;
            int noteSamples = Mathf.RoundToInt(SampleRate * noteDuration);
            int sampleCount = noteSamples * 2;
            float[] samples = new float[sampleCount];

            double phase = 0d;
            for (int i = 0; i < sampleCount; i++)
            {
                bool secondNote = i >= noteSamples;
                int localIndex = secondNote ? i - noteSamples : i;
                float localT = (float)localIndex / noteSamples;
                float freq = secondNote ? freqB : freqA;

                phase += 2d * Mathf.PI * freq / SampleRate;
                samples[i] = WaveSample(WaveType.Sine, (float)phase) * Envelope(localT) * volume;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float WaveSample(WaveType wave, float phase)
        {
            switch (wave)
            {
                case WaveType.Sine:
                    return Mathf.Sin(phase);
                case WaveType.Triangle:
                    return Mathf.PingPong(phase / Mathf.PI, 2f) - 1f;
                case WaveType.Square:
                    return Mathf.Sin(phase) >= 0f ? 0.7f : -0.7f;
                default:
                    return Mathf.Sin(phase);
            }
        }

        /// <summary>Short attack / decay envelope to avoid clicks at start/end.</summary>
        private static float Envelope(float t)
        {
            const float attack = 0.15f;
            const float release = 0.55f;

            if (t < attack)
            {
                return t / attack;
            }

            if (t > 1f - release)
            {
                return Mathf.Clamp01((1f - t) / release);
            }

            return 1f;
        }

        public void PlayHover()
        {
            if (audioSource == null || _hoverClip == null)
            {
                return;
            }

            audioSource.PlayOneShot(_hoverClip, 0.2f);
        }

        public void PlayClick()
        {
            if (audioSource == null || _clickClip == null)
            {
                return;
            }

            audioSource.PlayOneShot(_clickClip, 0.5f);
        }

        public void PlaySuccess()
        {
            if (audioSource == null || _successClip == null)
            {
                return;
            }

            audioSource.PlayOneShot(_successClip, 0.5f);
        }

        public void PlayError()
        {
            if (audioSource == null || _errorClip == null)
            {
                return;
            }

            audioSource.PlayOneShot(_errorClip, 0.6f);
        }
    }
}
