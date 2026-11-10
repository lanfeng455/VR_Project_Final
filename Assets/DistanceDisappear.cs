

using UnityEngine;

public class DistanceDisappear : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Distance Settings")]
    public float disappearDistance = 10f;
    public float reappearDistance = 15f;

    [Header("Behavior")]
    public bool reappearWhenFar = true;
    public bool disappearForever = false;

    [Header("Monster Visuals")]
    public GameObject monsterVisual;

    private bool hasDisappeared = false;
    private bool isVisible = true;

    private void Start()
    {
        if (monsterVisual == null)
        {
            Debug.LogWarning(
                "Please assign Monster Visual!",
                this
            );

            enabled = false;
            return;
        }

        UpdateVisibility(true);
    }

    private void Update()
    {
        if (player == null)
            return;

        float distance = Vector3.Distance(
            player.position,
            transform.position
        );

        // Player approaches the monster
        if (distance <= disappearDistance && isVisible)
        {
            UpdateVisibility(false);
            hasDisappeared = true;
        }

        // Player moves away from the monster
        else if (
            distance >= reappearDistance &&
            !isVisible &&
            reappearWhenFar &&
            !disappearForever
        )
        {
            UpdateVisibility(true);
        }
    }

    private void UpdateVisibility(bool visible)
    {
        // Permanently hidden if requested
        if (disappearForever && hasDisappeared && visible)
            return;

        isVisible = visible;

        monsterVisual.SetActive(visible);
    }
}