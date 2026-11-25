using UnityEngine;

public class Cannva : MonoBehaviour
{
    public GameObject firstCanvas;
    public GameObject secondCanvas;

    void Update()
    {
        if (firstCanvas != null && !firstCanvas.activeSelf)
        {
            secondCanvas.SetActive(true);
        }
        else
        {
            secondCanvas.SetActive(false);
        }
    }
}