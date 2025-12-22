using UnityEngine;
using TMPro;

public class ScoreComponent : MonoBehaviour
{
    [Header("References")]
    public JumpComponent playerJump;
    public TMP_Text scoreText;

    private int score = 0;

    void Start()
    {
        if (playerJump != null)
            playerJump.OnJump += AddScore;

        UpdateText();
    }

    private void OnDestroy()
    {
        if (playerJump != null)
            playerJump.OnJump -= AddScore;
    }

    private void AddScore()
    {
        score++;
        UpdateText();
    }

    private void UpdateText()
    {
        if (scoreText != null)
            scoreText.text = "Score: " + score;
    }
}
