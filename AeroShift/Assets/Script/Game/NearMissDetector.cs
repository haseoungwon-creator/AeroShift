using UnityEngine;

public class NearMissDetector : MonoBehaviour
{
    [SerializeField] ScoreManager scoreManager;
    [SerializeField] LayerMask obstacleLayer;
    [SerializeField] float detectDistance = 20f;
    [SerializeField] float dangerDistance = 5f;
    [SerializeField] int requiredDangerDirections = 2;

    private bool isDangerous;
    private bool wasDangerous;
    private bool isRewinding;

    private void Update()
    {
        if (isRewinding)
            return;

        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        CheckDanger();
    }

    public void SetRewinding(bool value)
    {
        isRewinding = value;

        if (value)
        {
            isDangerous = false;
            wasDangerous = false;
        }
    }

    private void CheckDanger()
    {
        Vector3 position = transform.position;

        int dangerCount = 0;

        if (IsDangerous(position, Vector3.up))
            dangerCount++;

        if (IsDangerous(position, Vector3.down))
            dangerCount++;

        if (IsDangerous(position, Vector3.left))
            dangerCount++;

        if (IsDangerous(position, Vector3.right))
            dangerCount++;

        wasDangerous = isDangerous;
        isDangerous = dangerCount >= requiredDangerDirections;


        if (wasDangerous && !isDangerous)
        {
            scoreManager.AddNearMiss();
        }
    }

    private bool IsDangerous(Vector3 origin, Vector3 direction)
    {
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, detectDistance, obstacleLayer))
            return false;

        return hit.distance <= dangerDistance;
    }

    public void ResetDetector()
    {
        isDangerous = false;
        wasDangerous = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Vector3 position = transform.position;

        Gizmos.DrawRay(position, Vector3.up * detectDistance);
        Gizmos.DrawRay(position, Vector3.down * detectDistance);
        Gizmos.DrawRay(position, Vector3.left * detectDistance);
        Gizmos.DrawRay(position, Vector3.right * detectDistance);

        Gizmos.color = Color.red;

        Gizmos.DrawRay(position, Vector3.up * dangerDistance);
        Gizmos.DrawRay(position, Vector3.down * dangerDistance);
        Gizmos.DrawRay(position, Vector3.left * dangerDistance);
        Gizmos.DrawRay(position, Vector3.right * dangerDistance);
    }
}