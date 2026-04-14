using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed = 200f;
    [Export] public float JumpVelocity = -450f;

    private const float Gravity = 980f;

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;

        // Apply gravity
        if (!IsOnFloor())
            velocity.Y += Gravity * (float)delta;

        // Jump
        if (Input.IsActionJustPressed("jump") && IsOnFloor())
            velocity.Y = JumpVelocity;

        // Horizontal movement
        float direction = Input.GetAxis("move_left", "move_right");
        velocity.X = direction * Speed;

        Velocity = velocity;
        MoveAndSlide();
    }
}
