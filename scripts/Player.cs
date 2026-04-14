using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public int PlayerIndex = 0;
    [Export] public float Speed      = 200f;
    [Export] public float JumpVelocity = -450f;

    public int Lives = 3;

    private const float Gravity              = 980f;
    private const float InvincibilityDuration = 1.5f;

    private float    _invincibleTimer = 0f;
    private bool     _canDoubleJump   = false;
    private Vector2  _spawnPoint;
    private Polygon2D _visual = null!;
    private string    _prefix = "p1";

    public override void _Ready()
    {
        _spawnPoint = GlobalPosition;
        _visual     = GetNode<Polygon2D>("Visual");
        _prefix     = $"p{PlayerIndex + 1}";
        AddToGroup("players");
    }

    public override void _PhysicsProcess(double delta)
    {
        TickInvincibility(delta);

        var velocity = Velocity;

        // Gravity
        if (!IsOnFloor())
            velocity.Y += Gravity * (float)delta;

        // Restore double-jump when landing
        if (IsOnFloor())
            _canDoubleJump = true;

        // Jump / double-jump
        if (Input.IsActionJustPressed($"{_prefix}_jump"))
        {
            if (IsOnFloor())
            {
                velocity.Y = JumpVelocity;
            }
            else if (_canDoubleJump)
            {
                velocity.Y      = JumpVelocity;
                _canDoubleJump  = false;
            }
        }

        velocity.X = Input.GetAxis($"{_prefix}_left", $"{_prefix}_right") * Speed;

        Velocity = velocity;
        MoveAndSlide();

        CheckStomp();
    }

    private void TickInvincibility(double delta)
    {
        if (_invincibleTimer <= 0f)
        {
            _visual.Visible = true;
            return;
        }
        _invincibleTimer -= (float)delta;
        _visual.Visible   = (int)(_invincibleTimer * 10) % 2 == 0;
    }

    private void CheckStomp()
    {
        if (_invincibleTimer > 0f) return;

        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            var col = GetSlideCollision(i);
            if (col.GetCollider() is not Player other) continue;

            // Normal points from collider toward us; upward normal = we're on top
            bool landingOnTop = col.GetNormal().Y < -0.7f && Velocity.Y >= 0f;
            if (!landingOnTop) continue;

            other.TakeHit();

            // Bounce stomper upward
            var v = Velocity;
            v.Y     = JumpVelocity * 0.65f;
            Velocity = v;
        }
    }

    public void TakeHit()
    {
        if (_invincibleTimer > 0f) return;

        Lives--;

        if (Lives <= 0)
        {
            GameManager.Instance?.OnPlayerEliminated(PlayerIndex);
            QueueFree();
            return;
        }

        GameManager.Instance?.OnPlayerHit(PlayerIndex, Lives);
        Respawn();
    }

    private void Respawn()
    {
        GlobalPosition = _spawnPoint;
        Velocity        = Vector2.Zero;
        _canDoubleJump  = false;
        _invincibleTimer = InvincibilityDuration;
    }
}
