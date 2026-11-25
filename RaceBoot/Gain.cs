using UnityEngine;

namespace RaceBoot
{
    // Amplifies the AudioSource of its GameObject (volumes above 100 % in the audio options).
    // Runs on the audio thread.
    public class Gain : MonoBehaviour
    {
        public volatile float Factor = 1f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            float g = Factor;
            if (g <= 1.001f) return;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * g;
                data[i] = v > 1f ? 1f : v < -1f ? -1f : v;
            }
        }
    }
}
