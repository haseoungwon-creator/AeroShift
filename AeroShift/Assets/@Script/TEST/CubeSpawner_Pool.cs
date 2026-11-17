using UnityEngine;

public class CubeSpawner_Pool : MonoBehaviour
{
    [SerializeField] GameObject cubePrefab;
    [SerializeField] int cubeCount = 5;
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float spawnX = 12f;
    [SerializeField] float destroyX = -12f;

    private GameObject[] cubes;

    private void Start()
    {
        cubes = new GameObject[cubeCount];

        for (int i = 0; i < cubeCount; i++)
        {
            cubes[i] = Instantiate(
                cubePrefab,
                new Vector3(i * 3f, 0f, 0f),
                Quaternion.identity
            );
        }
    }

    private void Update()
    {
        for (int i = 0; i < cubeCount; i++)
        {
            cubes[i].transform.Translate(Vector3.left * moveSpeed * Time.deltaTime);

            if (cubes[i].transform.position.x <= destroyX)
            {
                cubes[i].transform.position = new Vector3(spawnX, 0f, 0f);
            }
        }
    }
}