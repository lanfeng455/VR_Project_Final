using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class FallRespawn : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Respawn Point")]
    public Transform respawnPoint;

    [Header("Settings")]
    public bool resetRotation = true;

    private CharacterController characterController;

    private void Start()
    {
        if (player == null || respawnPoint == null)
        {
            UnityEngine.Debug.LogError(
                "FallRespawn: Player or RespawnPoint is missing!",
                this
            );

            enabled = false;
            return;
        }

        characterController =
            player.GetComponent<CharacterController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (player == null || respawnPoint == null)
            return;

        // Only respond to the assigned player
        if (other.transform != player &&
            !other.transform.IsChildOf(player))
            return;

        Respawn();
    }

    private void Respawn()
    {
        // Temporarily disable collision
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        // Move player to the spawn point
        player.position = respawnPoint.position;

        // Restore starting direction
        if (resetRotation)
        {
            player.rotation = respawnPoint.rotation;
        }

        // Re-enable CharacterController
        if (characterController != null)
        {
            characterController.enabled = true;
        }

        UnityEngine.Debug.Log(
            "Player Respawned!",
            this
        );
    }

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }
}