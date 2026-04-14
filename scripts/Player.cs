using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public int   PlayerIndex  = 0;
    [Export] public float Speed        = 200f;
    [Export] public float JumpVelocity = -450f;

    public bool IsEliminated { get; private set; } = false;

    private const float Gravity         = 980f;
    private const float SpawnProtection = 1.5f;
    private const float PlayerHalfWidth = 14f;

    private float     _spawnTimer    = SpawnProtection;
    private bool      _canDoubleJump = false;
    private Polygon2D _visual        = null!;
    private string    _prefix        = "p1";

    // --- Audio ---
    private AudioStreamPlayer _audio = null!;

    // Generated once, shared across all Player instances
    private static AudioStreamWav? _sndJump;
    private static AudioStreamWav? _sndDoubleJump;
    private static AudioStreamWav? _sndStomp;

    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        _visual = GetNode<Polygon2D>("Visual");
        _prefix = $"p{PlayerIndex + 1}";
        AddToGroup("players");

        _sndJump       ??= SynthSquare(freqA: 190f, freqB: 380f, dur: 0.11f);
        _sndDoubleJump ??= SynthSquare(freqA: 320f, freqB: 520f, dur: 0.09f);
        _sndStomp      ??= SynthStomp();

        _audio = new AudioStreamPlayer { VolumeDb = -5f };
        AddChild(_audio);
    }

    public override void _PhysicsProcess(double delta)
    {
        TickSpawnProtection(delta);

        var velocity = Velocity;

        if (!IsOnFloor())
            velocity.Y += Gravity * (float)delta;

        if (IsOnFloor())
            _canDoubleJump = true;

        if (Input.IsActionJustPressed($"{_prefix}_jump"))
        {
            if (IsOnFloor())
            {
                velocity.Y = JumpVelocity;
                Play(_sndJump!);
            }
            else if (_canDoubleJump)
            {
                velocity.Y     = JumpVelocity;
                _canDoubleJump = false;
                Play(_sndDoubleJump!);
            }
        }

        velocity.X = Input.GetAxis($"{_prefix}_left", $"{_prefix}_right") * Speed;

        Velocity = velocity;
        MoveAndSlide();

        ClampToBounds();
        CheckStomp();
    }

    // -------------------------------------------------------------------------
    // Boundary push — used during shrink phase instead of killing players
    // -------------------------------------------------------------------------

    private void ClampToBounds()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsShrinking) return;

        float lb = gm.EffectiveLeft  + PlayerHalfWidth;
        float rb = gm.EffectiveRight - PlayerHalfWidth;

        var pos = GlobalPosition;
        if (pos.X >= lb && pos.X <= rb) return;

        pos.X = Mathf.Clamp(pos.X, lb, rb);
        GlobalPosition = pos;

        // Zero horizontal velocity so the player doesn't fight the wall
        var vel = Velocity;
        vel.X    = 0f;
        Velocity = vel;
    }

    // -------------------------------------------------------------------------

    private void TickSpawnProtection(double delta)
    {
        if (_spawnTimer <= 0f) return;
        _spawnTimer    -= (float)delta;
        _visual.Visible = (int)(_spawnTimer * 10) % 2 == 0;
        if (_spawnTimer <= 0f) _visual.Visible = true;
    }

    private void CheckStomp()
    {
        if (_spawnTimer > 0f) return;

        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            var col = GetSlideCollision(i);
            if (col.GetCollider() is not Player other) continue;
            if (other.IsEliminated || other._spawnTimer > 0f) continue;

            bool landingOnTop = col.GetNormal().Y < -0.7f && Velocity.Y >= 0f;
            if (!landingOnTop) continue;

            Play(_sndStomp!);
            other.Eliminate(PlayerIndex);

            var v = Velocity;
            v.Y      = JumpVelocity * 0.65f;
            Velocity = v;
        }
    }

    public void Eliminate(int killerIndex = -1)
    {
        if (IsEliminated) return;
        IsEliminated = true;
        GameManager.Instance?.OnPlayerEliminated(PlayerIndex, killerIndex);
        QueueFree();
    }

    public void EliminateQuietly()
    {
        if (IsEliminated) return;
        IsEliminated = true;
        QueueFree();
    }

    // -------------------------------------------------------------------------
    // Audio helpers
    // -------------------------------------------------------------------------

    private void Play(AudioStreamWav stream)
    {
        _audio.Stream = stream;
        _audio.Play();
    }

    // Lofi square-wave beep with a linear frequency sweep.
    // 8 kHz sample rate keeps it grainy and retro.
    private static AudioStreamWav SynthSquare(float freqA, float freqB, float dur)
    {
        const int Rate = 8000;
        int n = (int)(Rate * dur);
        var data = new byte[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / n;
            float freq = freqA + (freqB - freqA) * t;
            phase += freq / Rate;

            float env    = 1f - t;                          // linear decay
            float square = (phase % 1f) < 0.5f ? 1f : -1f;
            data[i] = (byte)(sbyte)(square * env * 85f);
        }

        return new AudioStreamWav
        {
            Data     = data,
            Format   = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate  = Rate,
            Stereo   = false
        };
    }

    // Short percussive stomp: noise burst mixed with a descending sine thud.
    private static AudioStreamWav SynthStomp()
    {
        const int Rate = 8000;
        const float Dur = 0.14f;
        int n = (int)(Rate * Dur);
        var data = new byte[n];
        var rng = new System.Random(7);

        for (int i = 0; i < n; i++)
        {
            float t     = (float)i / n;
            float env   = Mathf.Exp(-t * 22f);             // sharp punch, fast tail-off
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float freq  = 110f - 70f * t;                  // pitch falls: 110 → 40 Hz
            float sine  = Mathf.Sin(2f * Mathf.Pi * freq * t);
            float s     = (noise * 0.45f + sine * 0.55f) * env;
            data[i] = (byte)(sbyte)(s * 105f);
        }

        return new AudioStreamWav
        {
            Data     = data,
            Format   = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate  = Rate,
            Stereo   = false
        };
    }
}
