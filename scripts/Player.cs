using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public int   PlayerIndex  = 0;
    [Export] public float Speed        = 200f;
    [Export] public float JumpVelocity = -450f;

    public bool IsEliminated { get; private set; } = false;

    // ---- Sci-fi neon colour palette — one per player slot ----
    private static readonly Color[] _palette =
    [
        new(0.00f, 0.85f, 1.00f),  // P1  cyan
        new(1.00f, 0.45f, 0.00f),  // P2  orange
        new(0.80f, 0.00f, 1.00f),  // P3  violet
        new(0.10f, 1.00f, 0.30f),  // P4  lime
        new(1.00f, 0.10f, 0.25f),  // P5  red
        new(1.00f, 0.88f, 0.00f),  // P6  gold
        new(0.10f, 0.35f, 1.00f),  // P7  blue
        new(1.00f, 0.30f, 0.75f),  // P8  pink
    ];
    public static Color ColorFor(int index) => _palette[index % _palette.Length];

    // ---- Physics ----
    private const float Gravity         = 980f;
    private const float SpawnProtection = 1.5f;
    private const float PlayerHalfWidth = 14f;

    private float  _spawnTimer    = SpawnProtection;
    private bool   _canDoubleJump = false;
    private string _prefix        = "p1";

    // ---- Body visuals ----
    private Node2D _bodyRoot     = null!;
    private Node2D _legLeftNode  = null!;
    private Node2D _legRightNode = null!;
    private float  _stridePhase  = 0f;

    // ---- Audio ----
    private AudioStreamPlayer _audio = null!;

    private const int MaxPlayers = 8;

    // Per-player jump sounds — slightly shifted pitch for each player
    private static readonly AudioStreamWav?[] _sndJumps       = new AudioStreamWav?[MaxPlayers];
    private static readonly AudioStreamWav?[] _sndDoubleJumps = new AudioStreamWav?[MaxPlayers];
    private static          AudioStreamWav?   _sndStomp;

    // Semitone offsets: 0, +2, −2, +4, −4, +1, −3, +3
    // Applied as freq multiplier = 2^(semitones/12)
    private static readonly float[] _pitchMult =
        { 1.000f, 1.122f, 0.891f, 1.260f, 0.794f, 1.059f, 0.891f, 1.189f };

    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Clear the scene-defined placeholder polygon — we build everything here
        GetNode<Polygon2D>("Visual").Polygon = [];

        _prefix = $"p{PlayerIndex + 1}";
        AddToGroup("players");

        _bodyRoot = new Node2D();
        AddChild(_bodyRoot);
        BuildBody(ColorFor(PlayerIndex));

        int   pi = PlayerIndex;
        float pm = _pitchMult[pi % _pitchMult.Length];
        _sndJumps[pi]       ??= SynthSquare(190f * pm, 380f * pm, 0.11f);
        _sndDoubleJumps[pi] ??= SynthSquare(320f * pm, 520f * pm, 0.09f);
        _sndStomp           ??= SynthStomp();

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
                Play(_sndJumps[PlayerIndex]!);
            }
            else if (_canDoubleJump)
            {
                velocity.Y     = JumpVelocity;
                _canDoubleJump = false;
                Play(_sndDoubleJumps[PlayerIndex]!);
            }
        }

        velocity.X = Input.GetAxis($"{_prefix}_left", $"{_prefix}_right") * Speed;

        Velocity = velocity;
        MoveAndSlide();

        ClampToBounds();
        CheckStomp();
        AnimateLegs();
    }

    // =========================================================================
    // Body construction — pure code, no sprites
    //
    // Four silhouettes rotate through PlayerIndex % 4:
    //   0 Chunky  — wide rectangular body, single wide visor eye
    //   1 Tall    — narrow body + head slightly wider, two separated eyes
    //   2 Tank    — head & body same max-width with no neck, three dot eyes
    //   3 Wedge   — triangular head on trapezoid body (wide at base), one eye
    //
    // Accent colour is the dominant body colour — each player reads as
    // "their colour + their shape" rather than a dark blob.
    // =========================================================================

    private void BuildBody(Color accent)
    {
        var body  = new Color(accent.R * 0.62f, accent.G * 0.62f, accent.B * 0.62f);
        var head  = new Color(accent.R * 0.46f, accent.G * 0.46f, accent.B * 0.46f);
        var panel = new Color(0.07f, 0.07f, 0.10f);
        var eye   = accent.Lerp(Colors.White, 0.55f);
        var legC  = new Color(accent.R * 0.38f, accent.G * 0.38f, accent.B * 0.38f);

        _legLeftNode  = new Node2D(); _bodyRoot.AddChild(_legLeftNode);
        _legRightNode = new Node2D(); _bodyRoot.AddChild(_legRightNode);

        switch (PlayerIndex % 4)
        {
            case 0: BuildChunky(body, head, panel, eye, legC); break;
            case 1: BuildTall  (body, head, panel, eye, legC); break;
            case 2: BuildTank  (body, head, panel, eye, legC); break;
            case 3: BuildWedge (body, head, panel, eye, legC); break;
        }
    }

    // ---- 0: Wide + heavy, single horizontal visor slit ----
    private void BuildChunky(Color body, Color head, Color panel, Color eye, Color leg)
    {
        P([ new(-14,-2), new(14,-2), new(14,20), new(-14,20) ], body);
        P([ new(-11,-22), new(11,-22), new(13,-2), new(-13,-2) ], head);
        P([ new( -9,-20), new( 9,-20), new( 9,-4), new( -9,-4) ], panel);
        P([ new( -8,-15), new( 8,-15), new( 8,-9), new( -8,-9) ], eye);   // wide slit
        L([ new(-10,20), new(-4,20), new(-4,24), new(-10,24) ], leg, _legLeftNode);
        L([ new(  4,20), new(10,20), new(10,24), new(  4,24) ], leg, _legRightNode);
    }

    // ---- 1: Narrow + tall, head slightly flared, two eyes ----
    private void BuildTall(Color body, Color head, Color panel, Color eye, Color leg)
    {
        P([ new(-9,-2), new(9,-2), new(9,20), new(-9,20) ], body);
        P([ new(-10,-22), new(10,-22), new(11,-2), new(-11,-2) ], head);
        P([ new( -8,-20), new( 8,-20), new( 8,-4), new( -8,-4) ], panel);
        P([ new(-7,-17), new(-2,-17), new(-2,-11), new(-7,-11) ], eye);    // left
        P([ new( 2,-17), new( 7,-17), new( 7,-11), new( 2,-11) ], eye);    // right
        L([ new(-7,20), new(-3,20), new(-3,24), new(-7,24) ], leg, _legLeftNode);
        L([ new( 3,20), new( 7,20), new( 7,24), new( 3,24) ], leg, _legRightNode);
    }

    // ---- 2: Full-width + no neck, three dot eyes ----
    private void BuildTank(Color body, Color head, Color panel, Color eye, Color leg)
    {
        P([ new(-14, 4), new(14, 4), new(14,22), new(-14,22) ], body);
        P([ new(-14,-14), new(14,-14), new(14, 4), new(-14, 4) ], head);
        P([ new(-12,-12), new(12,-12), new(12, 2), new(-12, 2) ], panel);
        P([ new(-10,-9), new(-5,-9), new(-5,-4), new(-10,-4) ], eye);      // left dot
        P([ new( -2,-9), new( 2,-9), new( 2,-4), new( -2,-4) ], eye);      // centre dot
        P([ new(  5,-9), new(10,-9), new(10,-4), new(  5,-4) ], eye);      // right dot
        L([ new(-13,22), new(-4,22), new(-4,24), new(-13,24) ], leg, _legLeftNode);
        L([ new(  4,22), new(13,22), new(13,24), new(  4,24) ], leg, _legRightNode);
    }

    // ---- 3: Triangular head on a trapezoid body (wide at base) ----
    private void BuildWedge(Color body, Color head, Color panel, Color eye, Color leg)
    {
        P([ new(-9,-2), new(9,-2), new(14,22), new(-14,22) ], body);       // trapezoid
        P([ new(0,-24), new(9,-2), new(-9,-2) ], head);                     // triangle
        P([ new(0,-22), new(6,-4), new(-6,-4) ], panel);                    // inner dark triangle
        P([ new(-4,-14), new(4,-14), new(4,-9), new(-4,-9) ], eye);        // centred rect
        L([ new(-14,22), new(-8,22), new(-10,24), new(-14,24) ], leg, _legLeftNode);   // angled out
        L([ new(  8,22), new(14,22), new( 14,24), new( 10,24) ], leg, _legRightNode);
    }

    // Appends a polygon to _bodyRoot
    private void P(Vector2[] pts, Color col)
        => _bodyRoot.AddChild(new Polygon2D { Polygon = pts, Color = col });

    // Appends a polygon to a leg sub-node
    private static void L(Vector2[] pts, Color col, Node2D parent)
        => parent.AddChild(new Polygon2D { Polygon = pts, Color = col });

    // =========================================================================
    // Leg walk animation
    // =========================================================================

    private void AnimateLegs()
    {
        bool  onFloor = IsOnFloor();
        float velX    = Velocity.X;

        if (onFloor && Mathf.Abs(velX) > 20f)
            _stridePhase += Mathf.Abs(velX) * 0.005f;

        float stride  = onFloor ? Mathf.Sin(_stridePhase) * 3f : 0f;
        float legRise = onFloor ? 0f : -4f;   // tuck legs up when airborne

        _legLeftNode.Position  = new Vector2(0f, legRise + stride);
        _legRightNode.Position = new Vector2(0f, legRise - stride);
    }

    // =========================================================================
    // Boundary push — used during shrink phase
    // =========================================================================

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

        var vel = Velocity;
        vel.X    = 0f;
        Velocity = vel;
    }

    // =========================================================================

    private void TickSpawnProtection(double delta)
    {
        if (_spawnTimer <= 0f) return;
        _spawnTimer      -= (float)delta;
        _bodyRoot.Visible = (int)(_spawnTimer * 10) % 2 == 0;
        if (_spawnTimer <= 0f) _bodyRoot.Visible = true;
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

    // =========================================================================
    // Audio helpers
    // =========================================================================

    private void Play(AudioStreamWav stream)
    {
        _audio.Stream = stream;
        _audio.Play();
    }

    // Lofi square-wave with a linear frequency sweep + pitch multiplier per player
    private static AudioStreamWav SynthSquare(float freqA, float freqB, float dur)
    {
        const int Rate = 8000;
        int   n     = (int)(Rate * dur);
        var   data  = new byte[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / n;
            float freq = freqA + (freqB - freqA) * t;
            phase += freq / Rate;
            float env    = 1f - t;
            float square = (phase % 1f) < 0.5f ? 1f : -1f;
            data[i] = (byte)(sbyte)(square * env * 85f);
        }

        return new AudioStreamWav
        {
            Data    = data,
            Format  = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = Rate,
            Stereo  = false
        };
    }

    // Short percussive stomp — shared across all players
    private static AudioStreamWav SynthStomp()
    {
        const int   Rate = 8000;
        const float Dur  = 0.14f;
        int n   = (int)(Rate * Dur);
        var data = new byte[n];
        var rng  = new System.Random(7);

        for (int i = 0; i < n; i++)
        {
            float t     = (float)i / n;
            float env   = Mathf.Exp(-t * 22f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float freq  = 110f - 70f * t;
            float sine  = Mathf.Sin(2f * Mathf.Pi * freq * t);
            float s     = (noise * 0.45f + sine * 0.55f) * env;
            data[i] = (byte)(sbyte)(s * 105f);
        }

        return new AudioStreamWav
        {
            Data    = data,
            Format  = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = Rate,
            Stereo  = false
        };
    }
}
