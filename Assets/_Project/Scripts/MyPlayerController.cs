using UnityEngine;

public class MyPlayerController : MonoBehaviour
{
    public Vector3 transformOffset = new Vector3(0, 1, 0);
    void Awake()
    {
        transform.GetChild(0).localPosition = transformOffset;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
