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

    private float safeMapWaitSeconds;
    public void TransitionTo2D()
    {
        if (isTransitioning || !isActiveAndEnabled || !GameManager.IsPlaying ||
            playerMode == null || !playerMode.Is3D() || !ValidateReferences())
            return;

        isTransitioning = true;
        transitioningTo2D = true;
        runningTransition = StartCoroutine(TransitionTo2DSequence());
    }

    public void TransitionTo3D()
    {
        if (isTransitioning || !isActiveAndEnabled || !GameManager.IsPlaying ||
            playerMode == null || !playerMode.IsMode2D() || !ValidateReferences())
            return;

        isTransitioning = true;
        transitioningTo2D = false;
        runningTransition = StartCoroutine(TransitionTo3DSequence());
    }

    private IEnumerator TransitionTo2DSequence()
    {
        safeMapWaitSeconds = 0f;
        mapManager.SpawnTransitionSafeMaps();

        bool missilePoolsPrepared = false;
        while (!mapManager.IsTransitionMapArrived() || !missilePoolsPrepared)
        {
            missilePoolsPrepared = missileSpawner.PrewarmFor2D();
            if (!GameManager.IsPlaying)
            {
                CancelCurrentTransition();
                yield break;
            }

            if (!mapManager.isActiveAndEnabled)
            {
                CancelCurrentTransition();
                yield break;
            }

            safeMapWaitSeconds += Time.unscaledDeltaTime;

            if (safeMapWaitSeconds >= safeMapWaitTimeout)
            {
                CancelCurrentTransition();
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
            if (!GameManager.IsPlaying)
            {
                CancelCurrentTransition();
                yield break;
            }

            delay -= Time.deltaTime;
            yield return null;
        }

        if (!GameManager.IsPlaying)
        {
            CancelCurrentTransition();
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

        if (!GameManager.IsPlaying)
        {
            CancelCurrentTransition();
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

        if (!GameManager.IsPlaying)
        {
            CancelCurrentTransition();
            yield break;
        }

        SaveTransitionPose();
        yield return transitionMovement.Dive();

        if (!GameManager.IsPlaying)
        {
            CancelCurrentTransition();
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

        return false;
    }

    private void CompleteTransition()
    {
        using (CompleteMarker.Auto())
        {
            ResetTransitionFlags();
        }
    }

    private void CancelCurrentTransition()
    {
        using (CancelMarker.Auto())
        {
            if (transitioningTo2D && mapManager != null)
                mapManager.EndTransitionMap();

            if (poseSaved && transitionMovement != null)
                transitionMovement.transform.SetPositionAndRotation(savedPosition, savedRotation);

            if (GameManager.IsPlaying)
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

        CancelCurrentTransition();
    }

    private void OnValidate()
    {
        riseDelay = Mathf.Max(0f, riseDelay);
        baseWorldSpeed = Mathf.Max(0.01f, baseWorldSpeed);
        safeMapWaitTimeout = Mathf.Max(1f, safeMapWaitTimeout);
    }
}
