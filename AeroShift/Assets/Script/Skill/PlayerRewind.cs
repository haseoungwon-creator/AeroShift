using System;
using System.Collections;
using UnityEngine;

public class PlayerRewind : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float rewindDuration = 5f;
    [SerializeField, Min(0.001f)] private float recordInterval = 0.02f;
    [SerializeField, Min(0.01f)] private float rewindPlayDuration = 2f;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private WorldMovement worldMovement;
    [SerializeField] private Collider playerCollider;
    [SerializeField] private SkillManager skillManager;

    private struct PositionData
    {
        public Vector3 playerPosition;
        public Quaternion playerRotation;
        public Vector3 worldPosition;

        public PositionData(Vector3 playerPosition, Quaternion playerRotation, Vector3 worldPosition)
        {
            this.playerPosition = playerPosition;
            this.playerRotation = playerRotation;
            this.worldPosition = worldPosition;
        }
    }

    private PositionData[] positionBuffer;
    private Collider[] playerColliders;
    private bool[] colliderEnabledStates;
    private Coroutine rewindCoroutine;

    private int bufferIndex;
    private int bufferCount;
    private float recordTimer;
    private bool hasUsed;
    private bool isRewinding;
    private bool collidersSuspended;
    private bool controlWasEnabled;

    public bool IsRewinding => isRewinding;
    public bool CanRewind => !hasUsed && !isRewinding;
    // ���õ��� ���� ��ų�� ���/�� ���� ����� �߻���Ű�� �ʽ��ϴ�.
    private bool IsRewindSelected =>
        skillManager != null && skillManager.IsSelected(SkillType.Rewind);

    // Streaming retains actual recorded history, including at low frame rates.
    public bool TryGetOldestLogicalZ(out float logicalZ)
    {
        logicalZ = 0f;

        if (!IsRewindSelected || !CanRewind || bufferCount == 0 || !isActiveAndEnabled)
            return false;

        PositionData oldest = positionBuffer[WrapIndex(bufferIndex - bufferCount)];
        logicalZ = oldest.playerPosition.z - oldest.worldPosition.z;
        return true;
    }

    private void Awake()
    {
        rewindDuration = Mathf.Max(0.01f, rewindDuration);
        recordInterval = Mathf.Max(0.001f, recordInterval);
        rewindPlayDuration = Mathf.Max(0.01f, rewindPlayDuration);

        int bufferSize = Mathf.Max(2, Mathf.CeilToInt(rewindDuration / recordInterval) + 1);
        positionBuffer = new PositionData[bufferSize];

        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        // Main ���� SkillManager�� ���� �� �� ���� Ž���մϴ�.
        if (skillManager == null)
            skillManager = FindFirstObjectByType<SkillManager>();

        CachePlayerColliders();
    }

    private void Update()
    {
        // TimeSlow/�̼���/�̹� ����� Rewind�� ����� �ʿ䰡 �����ϴ�.
        if (!IsGamePlaying() || !IsRewindSelected || !CanRewind)
            return;

        RecordPosition();
    }

    private void RecordPosition()
    {
        recordTimer += Time.deltaTime;

        if (recordTimer < recordInterval)
            return;

        recordTimer = Mathf.Min(recordTimer - recordInterval, recordInterval);

        positionBuffer[bufferIndex] = CapturePosition();
        bufferIndex = (bufferIndex + 1) % positionBuffer.Length;

        if (bufferCount < positionBuffer.Length)
            bufferCount++;
    }

    private PositionData CapturePosition()
    {
        Vector3 worldPosition = worldMovement != null
            ? worldMovement.transform.position
            : Vector3.zero;

        return new PositionData(transform.position, transform.rotation, worldPosition);
    }

    public void Rewind()
    {
        if (!isActiveAndEnabled || !IsGamePlaying() || !IsRewindSelected ||
            !CanRewind || bufferCount == 0)
            return;

        if (playerController == null)
        {
            Debug.LogError("PlayerRewind�� PlayerController�� ������� �ʾҽ��ϴ�.", this);
            return;
        }

        positionBuffer[bufferIndex] = CapturePosition();
        bufferIndex = (bufferIndex + 1) % positionBuffer.Length;

        if (bufferCount < positionBuffer.Length)
            bufferCount++;

        hasUsed = true;
        isRewinding = true;
        controlWasEnabled = playerController.CanControl;
        rewindCoroutine = StartCoroutine(RewindSequence());
    }

    private IEnumerator RewindSequence()
    {
        playerController.SetControlEnabled(false);

        if (worldMovement != null)
        {
            worldMovement.SetRewinding(true);
            worldMovement.SetMovement(false);
        }

        if (scoreManager != null)
            scoreManager.SetRewinding(true);

        SuspendPlayerColliders();

        int newestIndex = WrapIndex(bufferIndex - 1);
        float elapsedTime = 0f;

        while (elapsedTime < rewindPlayDuration)
        {
            if (!IsGamePlaying())
            {
                FinishRewind();
                yield break;
            }

            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / rewindPlayDuration);
            float historyPosition = t * (bufferCount - 1);
            int offset = Mathf.FloorToInt(historyPosition);

            int currentIndex = WrapIndex(newestIndex - offset);
            int previousIndex = WrapIndex(newestIndex - Mathf.Min(offset + 1, bufferCount - 1));

            PositionData current = positionBuffer[currentIndex];
            PositionData previous = positionBuffer[previousIndex];
            float interpolation = historyPosition - offset;

            transform.SetPositionAndRotation(
                Vector3.Lerp(current.playerPosition, previous.playerPosition, interpolation),
                Quaternion.Slerp(current.playerRotation, previous.playerRotation, interpolation));

            if (worldMovement != null)
            {
                worldMovement.transform.position = Vector3.Lerp(
                    current.worldPosition,
                    previous.worldPosition,
                    interpolation);
            }

            yield return null;
        }

        int oldestIndex = WrapIndex(newestIndex - (bufferCount - 1));
        PositionData oldest = positionBuffer[oldestIndex];

        transform.SetPositionAndRotation(oldest.playerPosition, oldest.playerRotation);

        if (worldMovement != null)
            worldMovement.transform.position = oldest.worldPosition;

        FinishRewind();
    }

    private void CachePlayerColliders()
    {
        playerColliders = GetComponentsInChildren<Collider>(true);

        if (playerCollider != null && Array.IndexOf(playerColliders, playerCollider) < 0)
        {
            int previousCount = playerColliders.Length;
            Array.Resize(ref playerColliders, previousCount + 1);
            playerColliders[previousCount] = playerCollider;
        }

        colliderEnabledStates = new bool[playerColliders.Length];
    }

    private void SuspendPlayerColliders()
    {
        if (playerColliders == null)
            CachePlayerColliders();

        collidersSuspended = true;

        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider collider = playerColliders[i];

            if (collider == null)
                continue;

            colliderEnabledStates[i] = collider.enabled;
            collider.enabled = false;
        }
    }

    private void RestorePlayerColliders()
    {
        if (!collidersSuspended)
            return;

        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider collider = playerColliders[i];

            if (collider != null)
                collider.enabled = colliderEnabledStates[i];
        }

        collidersSuspended = false;
    }

    private void FinishRewind()
    {
        RestorePlayerColliders();

        if (!isRewinding)
            return;

        isRewinding = false;
        rewindCoroutine = null;

        if (scoreManager != null)
            scoreManager.SetRewinding(false);

        bool isPlaying = IsGamePlaying();

        if (worldMovement != null)
        {
            worldMovement.SetRewinding(false);
            worldMovement.SetMovement(isPlaying);
        }

        if (playerController != null)
        {
            if (isPlaying)
                playerController.ResetMovementTarget();

            playerController.SetControlEnabled(isPlaying && controlWasEnabled);
        }
    }

    private void StopRewindAndRestore()
    {
        if (rewindCoroutine != null)
        {
            StopCoroutine(rewindCoroutine);
            rewindCoroutine = null;
        }

        FinishRewind();
    }

    private int WrapIndex(int index)
    {
        index %= positionBuffer.Length;
        return index < 0 ? index + positionBuffer.Length : index;
    }

    private static bool IsGamePlaying()
    {
        return GameManager.Instance != null &&
            GameManager.Instance.CurrentState == GameStates.Playing;
    }

    public void ResetRewind()
    {
        StopRewindAndRestore();

        bufferIndex = 0;
        bufferCount = 0;
        recordTimer = 0f;
        hasUsed = false;
    }

    private void OnDisable()
    {
        StopRewindAndRestore();
    }
}
