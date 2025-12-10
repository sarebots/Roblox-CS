namespace RollABall.Game;

public sealed class PlayerController
{
    private const float MoveSpeed = 12f;
    private float _score;

    public event System.Action<string>? OnCollect;

    public void ResetPlayer()
    {
        _score = 0f;
        Roblox.UnityAliases.Log($"[{nameof(PlayerController)}] Resetting player for RollABall");
    }

    public void Update(float deltaTime)
    {
        _score += deltaTime * MoveSpeed;
        if (_score >= 10f)
        {
            _score = 0f;
            OnCollect?.Invoke("Runner");
        }
    }
}
