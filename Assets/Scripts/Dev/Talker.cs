using UnityEngine;

public class Talker : MonoBehaviour
{
    [SerializeField]
    private string text;

    public void Print(){
        Debug.Log(text);
    }
}
