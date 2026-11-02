using System.Collections;
using UnityEngine;

public class DimensionTransition : MonoBehaviour
{
    [SerializeField] PlayerController playerController;
    [SerializeField] PlayerTransitionMovement transitionMovement;
    [SerializeField] CameraController cameraController;
    [SerializeField] PlayerMode playerMode;
    [SerializeField] ScoreManager scoreManager;
    [SerializeField] MapManager mapManager;
    [SerializeField] MissileSpawner missileSpawner;
    [SerializeField] CloudSpawner cloudSpawner;

    private bool isTransitioning;
    private float transitionMapEndZ;

    public bool IsTransitioning => isTransitioning;
    [SerializeField] float riseDelay = 2f;
    [SerializeField] float baseWorldSpeed = 30f;
    [SerializeField] WorldMovement worldMovement;

    public void TransitionTo2D()
    {
        if (isTransitioning || playerMode == null || !playerMode.Is3D()) return;

        StartCoroutine(TransitionTo2DSequence());
    }

    public void TransitionTo3D()
    {
        if (isTransitioning || playerMode == null || !playerMode.IsMode2D()) return;
        StartCoroutine(TransitionTo3DSequence());
    }

    private IEnumerator TransitionTo2DSequence()
    {
        isTransitioning = true;
        mapManager.SpawnTransitionSafeMaps();

        yield return WaitForTransitionMap();

        float delay = riseDelay * baseWorldSpeed / worldMovement.CurrentSpeed-1f;
        yield return new WaitForSeconds(delay);

        playerController.SetControlEnabled(false);

        cameraController.LockCamera();

        scoreManager.StopScoring();
        yield return transitionMovement.Rise();

        mapManager.HideMaps();

        cameraController.Set2DView();
        playerMode.SetMode(PlayerModes.Mode2D);
        playerController.Set2DInput();

        scoreManager.StartScoring();
        playerController.SetControlEnabled(true);

        missileSpawner.SetSpawning(true);

        mapManager.EndTransitionMap();
        isTransitioning = false;
        cloudSpawner.Set2DMode();

    }

    private IEnumerator TransitionTo3DSequence()
    {
        isTransitioning = true;

        scoreManager.StopScoring();

        yield return missileSpawner.SpawnTransitionPattern();

        yield return transitionMovement.Dive();

        mapManager.ResetMaps();

        cameraController.Set3DView();
        playerMode.SetMode(PlayerModes.Mode3D);
        playerController.Set3DInput();

        scoreManager.StartScoring();

        isTransitioning = false;
        cloudSpawner.Set3DMode();
    }

    private IEnumerator WaitForTransitionMap()
    {
        while(!mapManager.IsTransitionMapArrived(transitionMapEndZ))
            yield return null;
    }
}
