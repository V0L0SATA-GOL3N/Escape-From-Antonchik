using UnityEngine;
using UnityEngine.UI;

public class Play : MonoBehaviour
{
    private Button but;

    void Start()
    {
        if (FindObjectOfType<MenuController>() != null)
        {
            return;
        }

        but = GetComponent<Button>();
        but.onClick.AddListener(OnPlayClicked);
    }

    private void OnPlayClicked()
    {
        SceneLoadingScreen.Load("gamePlay");
    }
}
