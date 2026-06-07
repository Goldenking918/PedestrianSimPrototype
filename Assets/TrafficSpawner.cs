using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    public GameObject carPrefab;
    public Transform[] waypoints;

    public float spawnInterval = 3f;

    void Start()
    {
        InvokeRepeating(nameof(SpawnCar), 0f, spawnInterval);
    }

    void SpawnCar()
    {
        GameObject car = Instantiate(
            carPrefab,
            transform.position,
            transform.rotation
        );

        car.GetComponent<CarMovement>().waypoints = waypoints;
    }
}