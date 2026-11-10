using System.Collections;
using UnityEngine;

public class LightningController : MonoBehaviour
{
    // =========================================
    // LIGHTNING LIGHTS
    // =========================================

    [Header("Lightning Lights")]

    public Light lightningLightA;
    public Light lightningLightB;
    public Light lightningLightC;


    // =========================================
    // INDIVIDUAL LIGHT INTENSITIES
    // =========================================

    [Header("Light A - Directional")]

    public float lightAMinIntensity = 80f;
    public float lightAMaxIntensity = 110f;


    [Header("Light B - Directional")]

    public float lightBMinIntensity = 3.5f;
    public float lightBMaxIntensity = 6f;


    [Header("Light C - Spot")]

    public float lightCMinIntensity = 40000f;
    public float lightCMaxIntensity = 55000f;


    // =========================================
    // STORM TIMING
    // =========================================

    [Header("Storm Timing")]

    public float minStormInterval = 5f;
    public float maxStormInterval = 12f;


    // =========================================
    // FLASH PATTERN
    // =========================================

    [Header("Flash Pattern")]

    public int minFlashCount = 1;
    public int maxFlashCount = 4;

    public float minFlashDuration = 0.03f;
    public float maxFlashDuration = 0.10f;

    public float minPauseBetweenFlashes = 0.03f;
    public float maxPauseBetweenFlashes = 0.18f;


    // =========================================
    // RESIDUAL FLASH
    // =========================================

    [Header("Residual Flash")]

    [Range(0f, 1f)]
    public float residualFlashChance = 0.35f;

    [Range(0f, 1f)]
    public float residualIntensityMultiplier = 0.4f;


    // =========================================
    // THUNDER AUDIO
    // =========================================

    [Header("Thunder Audio")]

    public AudioSource thunderSource;
    public AudioClip thunderClip;

    public float minThunderDelay = 0.4f;
    public float maxThunderDelay = 2f;


    [Header("Thunder Variation")]

    public float minPitch = 0.92f;
    public float maxPitch = 1.05f;

    public float minThunderVolume = 0.65f;
    public float maxThunderVolume = 0.9f;


    // =========================================
    // INTERNAL VARIABLES
    // =========================================

    private float currentIntensityA;
    private float currentIntensityB;
    private float currentIntensityC;


    // =========================================
    // START
    // =========================================

    private void Start()
    {
        // Ensure all lightning lights start dark
        SetAllLights(0f);

        // Start the thunderstorm
        StartCoroutine(StormLoop());
    }


    // =========================================
    // STORM LOOP
    // =========================================

    private IEnumerator StormLoop()
    {
        while (true)
        {
            // Random waiting time between storms
            float delay = UnityEngine.Random.Range(
                minStormInterval,
                maxStormInterval
            );

            yield return new WaitForSeconds(delay);


            // Generate individual light intensities
            GenerateLightningIntensities();


            // Start thunder delay simultaneously
            StartCoroutine(PlayThunderAfterDelay());


            // Play the lightning sequence
            yield return StartCoroutine(
                PlayLightningSequence()
            );
        }
    }


    // =========================================
    // GENERATE LIGHT INTENSITIES
    // =========================================

    private void GenerateLightningIntensities()
    {
        currentIntensityA = UnityEngine.Random.Range(
            lightAMinIntensity,
            lightAMaxIntensity
        );

        currentIntensityB = UnityEngine.Random.Range(
            lightBMinIntensity,
            lightBMaxIntensity
        );

        currentIntensityC = UnityEngine.Random.Range(
            lightCMinIntensity,
            lightCMaxIntensity
        );
    }


    // =========================================
    // LIGHTNING SEQUENCE
    // =========================================

    private IEnumerator PlayLightningSequence()
    {
        // Random number of flashes
        int flashCount = UnityEngine.Random.Range(
            minFlashCount,
            maxFlashCount + 1
        );


        for (int i = 0; i < flashCount; i++)
        {
            // Synchronized brightness variation
            float intensityVariation =
                UnityEngine.Random.Range(0.65f, 1.0f);


            // Turn on all lightning lights
            SetAllLights(intensityVariation);


            // Random flash duration
            float flashDuration =
                UnityEngine.Random.Range(
                    minFlashDuration,
                    maxFlashDuration
                );

            yield return new WaitForSeconds(
                flashDuration
            );


            // Turn off all lights
            SetAllLights(0f);


            // Pause before next flash
            if (i < flashCount - 1)
            {
                float pause =
                    UnityEngine.Random.Range(
                        minPauseBetweenFlashes,
                        maxPauseBetweenFlashes
                    );

                yield return new WaitForSeconds(
                    pause
                );
            }
        }


        // =====================================
        // RESIDUAL FLASH
        // =====================================

        if (UnityEngine.Random.value <
            residualFlashChance)
        {
            yield return new WaitForSeconds(
                UnityEngine.Random.Range(
                    0.12f,
                    0.35f
                )
            );


            SetAllLights(
                residualIntensityMultiplier
            );


            yield return new WaitForSeconds(
                UnityEngine.Random.Range(
                    0.03f,
                    0.08f
                )
            );


            SetAllLights(0f);
        }
    }


    // =========================================
    // THUNDER AUDIO
    // =========================================

    private IEnumerator PlayThunderAfterDelay()
    {
        if (thunderSource == null ||
            thunderClip == null)
        {
            yield break;
        }


        // Random thunder delay
        float thunderDelay =
            UnityEngine.Random.Range(
                minThunderDelay,
                maxThunderDelay
            );

        yield return new WaitForSeconds(
            thunderDelay
        );


        // Random thunder pitch
        thunderSource.pitch =
            UnityEngine.Random.Range(
                minPitch,
                maxPitch
            );


        // Random thunder volume
        float thunderVolume =
            UnityEngine.Random.Range(
                minThunderVolume,
                maxThunderVolume
            );


        // Play one thunder sound
        thunderSource.PlayOneShot(
            thunderClip,
            thunderVolume
        );
    }


    // =========================================
    // CONTROL ALL LIGHTS
    // =========================================

    private void SetAllLights(float multiplier)
    {
        if (lightningLightA != null)
        {
            lightningLightA.intensity =
                currentIntensityA * multiplier;
        }


        if (lightningLightB != null)
        {
            lightningLightB.intensity =
                currentIntensityB * multiplier;
        }


        if (lightningLightC != null)
        {
            lightningLightC.intensity =
                currentIntensityC * multiplier;
        }
    }


    // =========================================
    // CLEANUP
    // =========================================

    private void OnDisable()
    {
        StopAllCoroutines();

        SetAllLights(0f);
    }
}