using System.Collections;
using UnityEngine;

public class PlayerRewind : MonoBehaviour
{
    [SerializeField] float rewindDuration = 5f;
    [SerializeField] float recordInterval = 0.02f;
    [SerializeField] float rewindPlayDuration = 2f;
    [SerializeField] ScoreManager scoreManager;
    [SerializeField] PlayerController playerController;
    [SerializeField] NearMissDetector nearMissDetector;
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] Collider playerCollider;

    private struct PositionData
    {
        public Vector3 playerPosition;
        public Quaternion playerRotation;
        public Vector3 worldPosition;

        public PositionData(
            Vector3 playerPosition,
            Quaternion playerRotation,
            Vector3 worldPosition)
        {
            this.playerPosition = playerPosition;
            this.playerRotation = playerRotation;
            this.worldPosition = worldPosition;
        }
    }

    private PositionData[] positionBuffer;
    private int bufferIndex;
    private int bufferCount;
    private float recordTimer;

    private bool hasUsed;
    private bool isRewinding;

    public bool IsRewinding => isRewinding;
    public bool CanRewind => !hasUsed && !isRewinding;

    private void Awake()
    {
        int bufferSize = Mathf.CeilToInt(
            rewindDuration / recordInterval);

        positionBuffer = new PositionData[bufferSize];
    }

    private void Update()
    {
        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        if (isRewinding)
            return;

        RecordPosition();
    }

    private void RecordPosition()
    {
        recordTimer += Time.deltaTime;

        if (recordTimer < recordInterval)
            return;

        recordTimer -= recordInterval;

        Vector3 worldPosition = Vector3.zero;

        if (worldMovement != null)
            worldPosition = worldMovement.transform.position;

        positionBuffer[bufferIndex] = new PositionData(
            transform.position,
            transform.rotation,
            worldPosition);

        bufferIndex =
            (bufferIndex + 1) % positionBuffer.Length;

        if (bufferCount < positionBuffer.Length)
            bufferCount++;
    }

    public void Rewind()
    {
        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        if (!CanRewind)
            return;

        if (bufferCount == 0)
            return;

        positionBuffer[bufferIndex] =
            new PositionData(
                transform.position,
                transform.rotation,
                worldMovement != null
                    ? worldMovement.transform.position
                    : Vector3.zero);

        bufferIndex =
            (bufferIndex + 1) % positionBuffer.Length;

        if (bufferCount < positionBuffer.Length)
            bufferCount++;

        hasUsed = true;

        StartCoroutine(RewindSequence());
    }

    private IEnumerator RewindSequence()
    {
        isRewinding = true;

        playerController.SetControlEnabled(false);

        if (worldMovement != null)
        {
            worldMovement.SetRewinding(true);
            worldMovement.SetMovement(false);
        }

        if (scoreManager != null)
            scoreManager.SetRewinding(true);

        if (nearMissDetector != null)
            nearMissDetector.SetRewinding(true);

        if (playerCollider != null)
            playerCollider.enabled = false;

        int newestIndex = bufferIndex - 1;

        if (newestIndex < 0)
            newestIndex = positionBuffer.Length - 1;

        float elapsedTime = 0f;

        while (elapsedTime < rewindPlayDuration)
        {
            elapsedTime += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsedTime / rewindPlayDuration);

            float historyPosition =
                t * (bufferCount - 1);

            int offset =
                Mathf.FloorToInt(historyPosition);

            int currentIndex =
                WrapIndex(newestIndex - offset);

            int previousIndex =
                WrapIndex(
                    newestIndex -
                    Mathf.Min(
                        offset + 1,
                        bufferCount - 1));

            PositionData current =
                positionBuffer[currentIndex];

            PositionData previous =
                positionBuffer[previousIndex];

            float interpolation =
                historyPosition - offset;

            transform.SetPositionAndRotation(
                Vector3.Lerp(
                    current.playerPosition,
                    previous.playerPosition,
                    interpolation),
                Quaternion.Slerp(
                    current.playerRotation,
                    previous.playerRotation,
                    interpolation));

            if (worldMovement != null)
            {
                worldMovement.transform.position =
                    Vector3.Lerp(
                        current.worldPosition,
                        previous.worldPosition,
                        interpolation);
            }

            yield return null;
        }

        int oldestIndex =
            WrapIndex(
                newestIndex -
                (bufferCount - 1));

        PositionData oldest =
            positionBuffer[oldestIndex];

        transform.SetPositionAndRotation(
            oldest.playerPosition,
            oldest.playerRotation);

        if (worldMovement != null)
        {
            worldMovement.transform.position =
                oldest.worldPosition;
        }

        if (playerCollider != null)
            playerCollider.enabled = true;

        if (nearMissDetector != null)
            nearMissDetector.SetRewinding(false);

        if (scoreManager != null)
            scoreManager.SetRewinding(false);

        if (worldMovement != null)
        {
            worldMovement.SetRewinding(false);
            worldMovement.SetMovement(true);
        }

        playerController.ResetMovementTarget();
        playerController.SetControlEnabled(true);

        isRewinding = false;
    }

    private int WrapIndex(int index)
    {
        index %= positionBuffer.Length;

        if (index < 0)
            index += positionBuffer.Length;

        return index;
    }

    public void ResetRewind()
    {
        bufferIndex = 0;
        bufferCount = 0;
        recordTimer = 0f;

        hasUsed = false;
        isRewinding = false;
    }
}