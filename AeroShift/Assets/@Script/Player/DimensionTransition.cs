using System.Collections;
using Unity.Profiling;
using UnityEngine;

public class DimensionTransition : MonoBehaviour
{
    private static readonly ProfilerMarker CompleteMarker = new ProfilerMarker("AeroShift.Dimension.Complete");
    private static readonly ProfilerMarker CancelMarker = new ProfilerMarker("AeroShift.Dimension.Cancel");

    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerTransitionMovement transitionMovement;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private PlayerMode playerMode;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private MapManager mapManager;
    [SerializeField] private MissileSpawner missileSpawner;
    [SerializeField] private CloudSpawner cloudSpawner;
    [SerializeField] private WorldMovement worldMovement;

    [Header("Transition Timing")]
    [SerializeField, Min(0f)] private float riseDelay = 2f;
    [SerializeField, Min(0.01f)] private float baseWorldSpeed = 30f;
    [SerializeField, Min(1f)] private float safeMapWaitTimeout = 45f;

    private Coroutine runningTransition;
    private bool isTransitioning;
    private bool transitioningTo2D;
    private bool controlSuspended;
    private bool scoringSuspended;
    private bool cameraLocked;
    private bool poseSaved;
    private Vector3 savedPosition;
    private Quaternion savedRotation;

    public bool IsTransitioning => isTransitioning;
    public float LastSafeMapWaitSeconds { get; private set; }
    public int SafeMapTimeoutCount { get; private set; }
    public int TransitionCancelCount { get; private set; }

    public void TransitionTo2D()
    {
        if (isTransitioning || !isActiveAndEnabled || !IsGamePlaying() ||
            playerMode == null || !playerMode.Is3D() || !ValidateReferences())
            return;

        isTransitioning = true;
        transitioningTo2D = true;
        runningTransition = StartCoroutine(TransitionTo2DSequence());
    }

    public void TransitionTo3D()
    {
        if (isTransitioning || !isActiveAndEnabled || !IsGamePlaying() ||
            playerMode == null || !playerMode.IsMode2D() || !ValidateReferences())
            return;

        isTransitioning = true;
        transitioningTo2D = false;
        runningTransition = StartCoroutine(TransitionTo3DSequence());
    }

    private IEnumerator TransitionTo2DSequence()
    {
        LastSafeMapWaitSeconds = 0f;
        mapManager.SpawnTransitionSafeMaps();

        // 2D pools are not needed in 3D. Prepare them across transition frames,
        // while the safe map approaches, before enabling 2D missile spawning.
        bool missilePoolsPrepared = false;
        while (!mapManager.IsTransitionMapArrived(0f) || !missilePoolsPrepared)
        {
            missilePoolsPrepared = missileSpawner.PrewarmFor2D();
            if (!IsGamePlaying())
            {
                CancelCurrentTransition("게임 상태가 변경되어 3D→2D 전환을 취소했습니다.");
                yield break;
            }

            if (!mapManager.isActiveAndEnabled)
            {
                CancelCurrentTransition("MapManager가 비활성화되어 안전 맵을 준비할 수 없습니다.");
                yield break;
            }

            LastSafeMapWaitSeconds += Time.unscaledDeltaTime;

            if (LastSafeMapWaitSeconds >= safeMapWaitTimeout)
            {
                SafeMapTimeoutCount++;
                CancelCurrentTransition($"안전 맵 도착을 {safeMapWaitTimeout:F1}초 동안 기다렸지만 완료되지 않았습니다.");
                yield break;
            }

            yield return null;
        }

        float speed = worldMovement.CurrentSpeed;
        float delay = speed > 0.01f
            ? Mathf.Max(0f, riseDelay * baseWorldSpeed / speed - 1f)
            : Mathf.Max(0f, riseDelay - 1f);

        while (delay > 0f)
        {
            if (!IsGamePlaying())
            {
                CancelCurrentTransition("게임이 종료되어 상승 대기 중 전환을 취소했습니다.");
                yield break;
            }

            delay -= Time.deltaTime;
            yield return null;
        }

        if (!IsGamePlaying())
        {
            CancelCurrentTransition("상승 시작 전에 게임 상태가 변경됐습니다.");
            yield break;
        }

        playerController.SetControlEnabled(false);
        controlSuspended = true;
        cameraController.LockCamera();
        cameraLocked = true;
        scoreManager.StopScoring();
        scoringSuspended = true;

        SaveTransitionPose();
        yield return transitionMovement.Rise();

        if (!IsGamePlaying())
        {
            CancelCurrentTransition("상승 도중 게임 상태가 변경됐습니다.");
            yield break;
        }

        mapManager.HideMaps();
        cameraController.Set2DView();
        playerMode.SetMode(PlayerModes.Mode2D);
        playerController.Set2DInput();
        cloudSpawner.Set2DMode();
        mapManager.EndTransitionMap();
        missileSpawner.SetSpawning(true);
        scoreManager.StartScoring();
        playerController.SetControlEnabled(true);

        CompleteTransition();
    }

    private IEnumerator TransitionTo3DSequence()
    {
        scoreManager.StopScoring();
        scoringSuspended = true;

        yield return missileSpawner.SpawnTransitionPattern();

        if (!IsGamePlaying())
        {
            CancelCurrentTransition("미사일 패턴 중 게임 상태가 변경됐습니다.");
            yield break;
        }

        SaveTransitionPose();
        yield return transitionMovement.Dive();

        if (!IsGamePlaying())
        {
            CancelCurrentTransition("하강 도중 게임 상태가 변경됐습니다.");
            yield break;
        }

        mapManager.ResetMaps();
        cameraController.Set3DView();
        playerMode.SetMode(PlayerModes.Mode3D);
        playerController.Set3DInput();
        cloudSpawner.Set3DMode();
        scoreManager.StartScoring();
        playerController.SetControlEnabled(true);

        CompleteTransition();
    }

    private void SaveTransitionPose()
    {
        savedPosition = transitionMovement.transform.position;
        savedRotation = transitionMovement.transform.rotation;
        poseSaved = true;
    }

    private bool ValidateReferences()
    {
        if (playerController != null && transitionMovement != null &&
            cameraController != null && playerMode != null &&
            scoreManager != null && mapManager != null &&
            missileSpawner != null && cloudSpawner != null && worldMovement != null)
            return true;

        Debug.LogError("DimensionTransition의 Inspector 참조가 누락됐습니다.", this);
        return false;
    }

    private static bool IsGamePlaying()
    {
        return GameManager.Instance != null &&
            GameManager.Instance.CurrentState == GameStates.Playing;
    }

    private void CompleteTransition()
    {
        using (CompleteMarker.Auto())
        {
            ResetTransitionFlags();
        }
    }

    private void CancelCurrentTransition(string reason)
    {
        using (CancelMarker.Auto())
        {
            TransitionCancelCount++;
            Debug.LogWarning($"DimensionTransition: {reason}", this);

            if (transitioningTo2D && mapManager != null)
                mapManager.EndTransitionMap();

            if (poseSaved && transitionMovement != null)
                transitionMovement.transform.SetPositionAndRotation(savedPosition, savedRotation);

            if (IsGamePlaying())
            {
                if (transitioningTo2D)
                {
                    playerMode.SetMode(PlayerModes.Mode3D);

                    if (cameraLocked)
                        cameraController.Set3DView();

                    if (controlSuspended)
                    {
                        playerController.Set3DInput();
                        playerController.ResetMovementTarget();
                        playerController.SetControlEnabled(true);
                    }
                }
                else
                {
                    playerMode.SetMode(PlayerModes.Mode2D);
                    cameraController.Set2DView();
                    playerController.Set2DInput();
                    playerController.SetControlEnabled(true);
                    missileSpawner.SetSpawning(true);
                    cloudSpawner.Set2DMode();
                }

                if (scoringSuspended)
                    scoreManager.StartScoring();
            }

            ResetTransitionFlags();
        }
    }

    private void ResetTransitionFlags()
    {
        runningTransition = null;
        isTransitioning = false;
        transitioningTo2D = false;
        controlSuspended = false;
        scoringSuspended = false;
        cameraLocked = false;
        poseSaved = false;
    }

    private void OnDisable()
    {
        if (!isTransitioning)
            return;

        if (runningTransition != null)
            StopCoroutine(runningTransition);

        CancelCurrentTransition("컴포넌트가 비활성화됐습니다.");
    }

    private void OnValidate()
    {
        riseDelay = Mathf.Max(0f, riseDelay);
        baseWorldSpeed = Mathf.Max(0.01f, baseWorldSpeed);
        safeMapWaitTimeout = Mathf.Max(1f, safeMapWaitTimeout);
    }
}
