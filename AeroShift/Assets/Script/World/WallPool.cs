using Unity.VisualScripting;
using UnityEngine;

public class WallPool : MonoBehaviour
{
    [SerializeField] GameObject wallPrefab;

    private GameObject wall;

    private void Awake()
    {
        if (wallPrefab == null) return;

        wall = Instantiate (wallPrefab, transform);
        wall.SetActive(false);
    }

    public GameObject GetWall()
    {
        if(wall == null) return null;

        wall.SetActive(true);
        return wall;
    }

    public void ReturnWall()
    {
        if(wall == null) return;

        wall.SetActive(false);
    }
}
