using UnityEngine;

namespace TenDays.StoryApartment
{
    [DisallowMultipleComponent]
    public sealed class KitchenExtractorFan : MonoBehaviour
    {
        [SerializeField] Transform rotor;
        [SerializeField] AudioSource motor;
        [SerializeField, Min(0f)] float revolutionsPerMinute = 280f;
        [SerializeField] bool running = true;
        [SerializeField] Renderer[] stationaryBlades = System.Array.Empty<Renderer>();

        public bool IsRunning => running;

        void OnEnable()
        {
            ShowStationaryBlades(false);
            SyncAudio();
        }

        void Update()
        {
            if (running && rotor)
                rotor.Rotate(Vector3.right, revolutionsPerMinute * 6f * Time.deltaTime, Space.Self);
        }

        void OnDisable()
        {
            if (motor) motor.Stop();
            ShowStationaryBlades(true);
        }

        public void SetRunning(bool value)
        {
            running = value;
            SyncAudio();
        }

        void SyncAudio()
        {
            if (!motor || !Application.isPlaying) return;
            if (running && isActiveAndEnabled && motor.isActiveAndEnabled && motor.clip)
            {
                if (!motor.isPlaying) motor.Play();
            }
            else
            {
                motor.Stop();
            }
        }

        void ShowStationaryBlades(bool visible)
        {
            foreach (var blade in stationaryBlades)
            {
                if (blade) blade.enabled = visible;
            }
        }
    }
}
