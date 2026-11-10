using System;
using System.Collections;

using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class ChaseSoundTrigger : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public AudioSource chaseAudio;
    public AudioClip chaseClip;

    [Header("Chase Settings")]
    public float startDistance = 40f;
    public float endDistance = 2f;
    public float chaseDuration = 15f;
    public float startDelay = 0f;

    [Header("Movement Settings")]
    [Range(1f, 5f)]
    public float accelerationPower = 2f;

    [Header("Sound Settings")]
    [Range(0f, 1f)]
    public float startVolume = 0.15f;

    [Range(0f, 1f)]
    public float maxVolume = 0.9f;

    public float startPitch = 0.85f;
    public float endPitch = 1.15f;

    [Header("Behavior")]
    public bool triggerOnlyOnce = true;

    [Header("Debug")]
    public bool showDebugLogs = true;

    private bool hasTriggered = false;
    private bool isPlaying = false;

    private Coroutine chaseCoroutine;

    // ==========================================
    // INITIALIZE
    // ==========================================

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    // ==========================================
    // TRIGGER
    // ==========================================

    private void OnTriggerEnter(Collider other)
    {
        if (player == null ||
            chaseAudio == null ||
            chaseClip == null)
        {
            return;
        }

        // Only the assigned player can trigger
        if (other.transform != player &&
            !other.transform.IsChildOf(player))
        {
            return;
        }

        if (isPlaying)
            return;

        if (triggerOnlyOnce && hasTriggered)
            return;

        hasTriggered = true;

        chaseCoroutine = StartCoroutine(
            PlayChaseSound()
        );
    }

    // ==========================================
    // CHASE EVENT
    // ==========================================

    private IEnumerator PlayChaseSound()
    {
        isPlaying = true;

        // Delay before starting
        yield return new WaitForSeconds(
            Mathf.Max(0f, startDelay)
        );

        // Determine the direction behind the player
        Vector3 backward = -player.forward;
        backward.y = 0f;

        if (backward.sqrMagnitude < 0.001f)
        {
            backward = Vector3.back;
        }

        backward.Normalize();

        float duration = Mathf.Max(
            0.1f,
        chaseDuration
        );

        float initialDistance = Mathf.Max(
            0f,
        startDistance
        );

        float finalDistance = Mathf.Clamp(
            endDistance,
            0f,
            initialDistance
        );

        // Initial audio position
        chaseAudio.transform.position =
            player.position + backward * initialDistance;

        // ======================================
        // AUDIO SETUP
        // ======================================

        chaseAudio.Stop();

        chaseAudio.clip = chaseClip;

        // Important: loop even if audio is short
        chaseAudio.loop = true;

        chaseAudio.playOnAwake = false;

        chaseAudio.spatialBlend = 1f;

        chaseAudio.dopplerLevel = 0f;

        chaseAudio.volume = startVolume;

        chaseAudio.pitch = startPitch;

        // Start playing
        chaseAudio.Play();

        if (showDebugLogs)
        {
            UnityEngine.Debug.Log(
                "CHASE START at " + Time.time
            );
        }

        // ======================================
        // MOVEMENT LOOP
        // ======================================

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsed / duration
            );

            // ==================================
            // AUTOMATIC ACCELERATION
            // ==================================

            float progress = Mathf.Pow(
                t,
                accelerationPower
            );

            // Distance decreases as sound approaches
            float currentDistance = Mathf.Lerp(
                initialDistance,
                finalDistance,
                progress
            );

            // Always stay behind the player
            Vector3 targetPosition =
                player.position + backward * currentDistance;

            chaseAudio.transform.position =
                targetPosition;

            // ==================================
            // VOLUME INCREASE
            // ==================================

            chaseAudio.volume = Mathf.Lerp(
                startVolume,
                maxVolume,
                t
            );

            // ==================================
            // PITCH INCREASE
            // ==================================

            chaseAudio.pitch = Mathf.Lerp(
                startPitch,
                endPitch,
                t
            );

            yield return null;
        }

        // ======================================
        // SUDDEN STOP
        // ======================================

        chaseAudio.Stop();

        chaseAudio.volume = 0f;

        if (showDebugLogs)
        {
            UnityEngine.Debug.Log(
                "CHASE END at " + Time.time
            );
        }

        isPlaying = false;
        chaseCoroutine = null;
    }

    // ==========================================
    // CLEANUP
    // ==========================================

    private void OnDisable()
    {
        if (chaseCoroutine != null)
        {
            StopCoroutine(chaseCoroutine);
            chaseCoroutine = null;
        }

        if (chaseAudio != null)
        {
            chaseAudio.Stop();
        }

        isPlaying = false;
    }
}