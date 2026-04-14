using Godot;

public enum PowerUpType { None, Speed, HighJump, Float, Shield, GroundPound, Tiny }

public partial class Player : CharacterBody2D
{
    [Export] public int   PlayerIndex  = 0;
    [Export] public float Speed        = 200f;
    [Export] public float JumpVelocity = -450f;

    public bool IsEliminated { get; private set; } = false;

    // ---- Sci-fi neon colour palette ----
    private static readonly Color[] _palette =
    [
        new(0.00f, 0.85f, 1.00f),  // cyan
        new(1.00f, 0.45f, 0.00f),  // orange
        new(0.80f, 0.00f, 1.00f),  // violet
        new(0.10f, 1.00f, 0.30f),  // lime
        new(1.00f, 0.10f, 0.25f),  // red
        new(1.00f, 0.88f, 0.00f),  // gold
        new(0.10f, 0.35f, 1.00f),  // blue
        new(1.00f, 0.30f, 0.75f),  // pink
    ];

    // Shuffled once per match — stable across rounds, reshuffled at match restart.
    private static readonly System.Random _rng             = new();
    private static readonly int[]         _colorAssignment = [0, 1, 2, 3, 4, 5, 6, 7];
    private static readonly int[]         _shapeAssignment = [0, 1, 2, 3];
    private static          int           _patternChoice   = 0;   // 0=Pulse 1=Scanlines 2=Sweep 3=Grid
    private static          bool          _assignmentsSeeded = false;

    public static Color ColorFor(int index)
        => _palette[_colorAssignment[index % _colorAssignment.Length]];

    public static void ResetAssignmentSeed() => _assignmentsSeeded = false;

    private static void FisherYates(int[] arr)
    {
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }

    // ---- Physics ----
    private const float Gravity         = 980f;
    private const float SpawnProtection = 1.5f;
    private const float PlayerHalfWidth = 14f;

    private float  _spawnTimer    = SpawnProtection;
    private bool   _canDoubleJump = false;
    private string _prefix        = "p1";

    // ---- Body visuals ----
    private Node2D     _bodyRoot     = null!;
    private float      _stridePhase  = 0f;

    // ---- Animated skin pattern ----
    private float      _patternTime  = 0f;
    private Polygon2D[] _patternPolys = [];

    // ---- Robot legs ----
    // Each leg: thigh block (pivots at hip) + vertical shin block (piston style).
    private Polygon2D _leftThigh  = null!;
    private Polygon2D _leftShin   = null!;
    private Polygon2D _rightThigh = null!;
    private Polygon2D _rightShin  = null!;
    // Wheel anchor positions in _bodyRoot local space (set per silhouette in Build*)
    private Vector2 _leftHip, _rightHip;

    // ---- Power-up indicators ----
    // Diamond shapes added to _bodyRoot, one per power type, shown above head.
    private Polygon2D[] _pwrIndicators = null!;
    private Polygon2D   _shieldVisual  = null!;
    private static readonly PowerUpType[] _pwrOrder =
        [PowerUpType.Speed, PowerUpType.HighJump, PowerUpType.Float,
         PowerUpType.Shield, PowerUpType.GroundPound, PowerUpType.Tiny];

    // ---- Power-up state — all permanent for the round, stack freely ----
    private bool _pwrSpeed       = false;
    private bool _pwrHighJump    = false;
    private bool _pwrFloat       = false;
    private bool _pwrShield      = false;
    private bool _pwrGroundPound = false;
    private bool _pwrTiny        = false;
    private bool _groundPoundArmed = false;

    // Death animation
    private const float DeathDuration = 0.55f;
    private float _deathTimer = 0f;

    // Tiny needs to resize the collision shape exactly once
    private Vector2          _originalShapeSize;
    private CollisionShape2D _collisionShape = null!;

    // ---- Audio ----
    private AudioStreamPlayer _audio = null!;

    private const int MaxPlayers = 8;

    private static readonly AudioStreamWav?[] _sndJumps       = new AudioStreamWav?[MaxPlayers];
    private static readonly AudioStreamWav?[] _sndDoubleJumps = new AudioStreamWav?[MaxPlayers];
    private static          AudioStreamWav?   _sndStomp;
    private static          AudioStreamWav?   _sndPowerUp;
    private static          AudioStreamWav?   _sndSquish;

    private static readonly float[] _pitchMult =
        { 1.000f, 1.122f, 0.891f, 1.260f, 0.794f, 1.059f, 0.891f, 1.189f };

    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        GetNode<Polygon2D>("Visual").Polygon = [];

        if (PlayerIndex == 0 && !_assignmentsSeeded)
        {
            FisherYates(_colorAssignment);
            FisherYates(_shapeAssignment);
            _patternChoice     = _rng.Next(4);
            _assignmentsSeeded = true;
        }

        _prefix = $"p{PlayerIndex + 1}";
        AddToGroup("players");

        _collisionShape    = GetNode<CollisionShape2D>("CollisionShape2D");
        _originalShapeSize = ((RectangleShape2D)_collisionShape.Shape).Size;

        _bodyRoot = new Node2D();
        AddChild(_bodyRoot);

        // Create leg Polygon2D nodes first so they render behind the body
        _leftThigh  = new Polygon2D(); _bodyRoot.AddChild(_leftThigh);
        _leftShin   = new Polygon2D(); _bodyRoot.AddChild(_leftShin);
        _rightThigh = new Polygon2D(); _bodyRoot.AddChild(_rightThigh);
        _rightShin  = new Polygon2D(); _bodyRoot.AddChild(_rightShin);

        BuildBody(ColorFor(PlayerIndex));

        // Wheels: circle ring + rotating spoke per side.
        // Positions come from _leftHip / _rightHip set by Build*.
        const float WheelR = 6f;
        var wheelPoly = CirclePoly(WheelR, 14);
        var spokePoly = new Vector2[]
            { new(-WheelR+1f,-1.5f), new(WheelR-1f,-1.5f), new(WheelR-1f,1.5f), new(-WheelR+1f,1.5f) };
        var spokeColor = ColorFor(PlayerIndex).Lerp(Colors.White, 0.55f);

        _leftThigh.Polygon   = wheelPoly;
        _leftThigh.Position  = _leftHip;
        _rightThigh.Polygon  = wheelPoly;
        _rightThigh.Position = _rightHip;

        _leftShin.Polygon   = spokePoly;
        _leftShin.Position  = _leftHip;
        _leftShin.Color     = spokeColor;
        _rightShin.Polygon  = spokePoly;
        _rightShin.Position = _rightHip;
        _rightShin.Color    = spokeColor;

        SetupPattern(ColorFor(PlayerIndex));

        // Power-up indicator diamonds — sit in _bodyRoot so they blink + scale with body.
        // Diamond polygon centred at (0,0); Position is set dynamically in RefreshIndicators.
        _pwrIndicators = new Polygon2D[_pwrOrder.Length];
        for (int i = 0; i < _pwrOrder.Length; i++)
        {
            _pwrIndicators[i] = new Polygon2D
            {
                Polygon  = [new(0f,-6f), new(5f,0f), new(0f,6f), new(-5f,0f)],
                Color    = PowerIndicatorColor(_pwrOrder[i]),
                Position = new Vector2(0f, -38f),
                Visible  = false
            };
            _bodyRoot.AddChild(_pwrIndicators[i]);
        }

        // Shield bubble — octagon slightly larger than character, rendered on top of body
        _shieldVisual = new Polygon2D
        {
            Polygon  = ShieldPoly(17f, 27f, 8),
            Color    = new Color(0.55f, 0.85f, 1.00f, 0f),
            Visible  = false,
            Position = new Vector2(0f, -2f)   // centred on body mass
        };
        _bodyRoot.AddChild(_shieldVisual);

        int   pi = PlayerIndex;
        float pm = _pitchMult[pi % _pitchMult.Length];
        _sndJumps[pi]       ??= SynthSquare(190f * pm, 380f * pm, 0.11f);
        _sndDoubleJumps[pi] ??= SynthSquare(320f * pm, 520f * pm, 0.09f);
        _sndStomp           ??= SynthStomp();
        _sndPowerUp         ??= SynthPowerUp();
        _sndSquish          ??= SynthSquish();

        _audio = new AudioStreamPlayer { VolumeDb = -5f };
        AddChild(_audio);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsEliminated)
        {
            TickDeathAnimation((float)delta);
            return;
        }

        TickSpawnProtection(delta);

        var  velocity   = Velocity;
        bool wasOnFloor = IsOnFloor();

        // Gravity — Float makes it dreamy
        if (!IsOnFloor())
        {
            float grav = _pwrFloat ? Gravity * 0.3f : Gravity;
            velocity.Y += grav * (float)delta;
        }

        if (IsOnFloor()) _canDoubleJump = true;

        // Jump — HighJump amplifies it
        float effJump = _pwrHighJump ? JumpVelocity * 1.6f : JumpVelocity;
        if (Input.IsActionJustPressed($"{_prefix}_jump"))
        {
            if (IsOnFloor())
            {
                velocity.Y = effJump;
                Play(_sndJumps[PlayerIndex]!);
            }
            else if (_canDoubleJump)
            {
                velocity.Y     = effJump;
                _canDoubleJump = false;
                Play(_sndDoubleJumps[PlayerIndex]!);
            }
        }

        // Horizontal — Speed amplifies it
        float effSpeed = _pwrSpeed ? Speed * 1.7f : Speed;
        velocity.X = Input.GetAxis($"{_prefix}_left", $"{_prefix}_right") * effSpeed;

        // Down — fast-fall always; GroundPound arms the slam
        if (Input.IsActionJustPressed($"{_prefix}_down") && !IsOnFloor())
        {
            if (_pwrGroundPound)
            {
                _groundPoundArmed = true;
                velocity.Y = 1100f;
            }
            else
            {
                if (velocity.Y < 600f) velocity.Y = 600f;
            }
        }

        // Pre-clamp: zero velocity pushing player past shrink bounds before movement
        var _gm = GameManager.Instance;
        if (_gm?.IsShrinking == true)
        {
            float _lb = _gm.EffectiveLeft  + PlayerHalfWidth;
            float _rb = _gm.EffectiveRight - PlayerHalfWidth;
            if (GlobalPosition.X <= _lb && velocity.X < 0f) velocity.X = 0f;
            if (GlobalPosition.X >= _rb && velocity.X > 0f) velocity.X = 0f;
        }

        Velocity = velocity;
        MoveAndSlide();

        if (!wasOnFloor && IsOnFloor() && _groundPoundArmed)
            CheckGroundPoundLanding();

        ClampToBounds();
        CheckStomp();
        AnimateLegs((float)delta);
    }

    // =========================================================================
    // Power-up system — powers stack; all persist until round ends
    // =========================================================================

    public void ActivatePowerUp(PowerUpType type)
    {
        bool changed = false;
        switch (type)
        {
            case PowerUpType.Speed:       if (!_pwrSpeed)       { _pwrSpeed       = true; changed = true; } break;
            case PowerUpType.HighJump:    if (!_pwrHighJump)    { _pwrHighJump    = true; changed = true; } break;
            case PowerUpType.Float:       if (!_pwrFloat)       { _pwrFloat       = true; changed = true; } break;
            case PowerUpType.Shield:      if (!_pwrShield)      { _pwrShield      = true; changed = true; } break;
            case PowerUpType.GroundPound: if (!_pwrGroundPound) { _pwrGroundPound = true; changed = true; } break;
            case PowerUpType.Tiny:
                if (!_pwrTiny) { _pwrTiny = true; ApplyTinyScale(true); changed = true; }
                break;
        }
        if (changed) Play(_sndPowerUp!);
        RefreshIndicators();
    }

    public bool HasPowerUp(PowerUpType type) => type switch
    {
        PowerUpType.Speed       => _pwrSpeed,
        PowerUpType.HighJump    => _pwrHighJump,
        PowerUpType.Float       => _pwrFloat,
        PowerUpType.Shield      => _pwrShield,
        PowerUpType.GroundPound => _pwrGroundPound,
        PowerUpType.Tiny        => _pwrTiny,
        _                       => false
    };

    private void RefreshIndicators()
    {
        bool[] active = [_pwrSpeed, _pwrHighJump, _pwrFloat, _pwrShield, _pwrGroundPound, _pwrTiny];

        // Collect which slots are active
        var slots = new System.Collections.Generic.List<int>(6);
        for (int i = 0; i < active.Length; i++)
            if (active[i]) slots.Add(i);

        // Hide all first, then reposition and show active ones centred as a group
        for (int i = 0; i < _pwrIndicators.Length; i++)
            _pwrIndicators[i].Visible = false;

        int count = slots.Count;
        for (int j = 0; j < count; j++)
        {
            float cx = count > 1 ? (j - (count - 1) * 0.5f) * 10f : 0f;
            _pwrIndicators[slots[j]].Position = new Vector2(cx, -38f);
            _pwrIndicators[slots[j]].Visible  = true;
        }

        GameManager.Instance?.OnPowerUpChanged(PlayerIndex, BuildPowerUpDisplay());
    }

    private static Color PowerIndicatorColor(PowerUpType t) => t switch
    {
        PowerUpType.Speed       => new Color(1.00f, 1.00f, 0.00f),  // yellow
        PowerUpType.HighJump    => new Color(0.10f, 1.00f, 0.30f),  // lime
        PowerUpType.Float       => new Color(0.28f, 0.95f, 1.00f),  // cyan
        PowerUpType.Shield      => Colors.White,
        PowerUpType.GroundPound => new Color(1.00f, 0.10f, 0.10f),  // red
        PowerUpType.Tiny        => new Color(0.80f, 0.00f, 1.00f),  // violet
        _                       => Colors.White
    };

    private void ApplyTinyScale(bool shrink)
    {
        _bodyRoot.Scale = shrink ? new Vector2(0.7f, 0.7f) : Vector2.One;
        ((RectangleShape2D)_collisionShape.Shape).Size =
            shrink ? _originalShapeSize * 0.7f : _originalShapeSize;
    }

    private void CheckGroundPoundLanding()
    {
        _groundPoundArmed = false;
        Play(_sndStomp!);
        float myX = GlobalPosition.X;

        foreach (var node in GetTree().GetNodesInGroup("players"))
        {
            if (node is not Player other) continue;
            if (other == this || other.IsEliminated || other._spawnTimer > 0f) continue;
            float dx = Mathf.Abs(other.GlobalPosition.X - myX);
            if (dx <= 30f)
                other.Eliminate(PlayerIndex);
            else if (dx <= 100f)
            {
                var v = other.Velocity;
                v.X += (other.GlobalPosition.X > myX ? 1f : -1f) * 350f;
                other.Velocity = v;
            }
        }
    }

    private string BuildPowerUpDisplay()
    {
        var sb = new System.Text.StringBuilder();
        if (_pwrSpeed)       sb.Append("SPD ");
        if (_pwrHighJump)    sb.Append("HJP ");
        if (_pwrFloat)       sb.Append("FLT ");
        if (_pwrShield)      sb.Append("SLD ");
        if (_pwrGroundPound) sb.Append("GND ");
        if (_pwrTiny)        sb.Append("TNY ");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "";
    }

    // =========================================================================
    // Body construction
    //
    // Four silhouettes assigned randomly per match via _shapeAssignment:
    //   0 Chunky  — wide rectangular body, single wide visor eye
    //   1 Tall    — narrow body + head slightly wider, two separated eyes
    //   2 Tank    — head & body same max-width with no neck, three dot eyes
    //   3 Wedge   — triangular head on trapezoid body (wide at base), one eye
    // =========================================================================

    private void BuildBody(Color accent)
    {
        var body  = new Color(accent.R * 0.62f, accent.G * 0.62f, accent.B * 0.62f);
        var head  = new Color(accent.R * 0.46f, accent.G * 0.46f, accent.B * 0.46f);
        var panel = new Color(0.07f, 0.07f, 0.10f);
        var eye   = accent.Lerp(Colors.White, 0.55f);
        var legC  = new Color(accent.R * 0.50f, accent.G * 0.50f, accent.B * 0.50f);

        _leftThigh.Color  = _leftShin.Color  = legC;
        _rightThigh.Color = _rightShin.Color = legC;

        switch (_shapeAssignment[PlayerIndex % _shapeAssignment.Length])
        {
            case 0: BuildChunky(body, head, panel, eye); break;
            case 1: BuildTall  (body, head, panel, eye); break;
            case 2: BuildTank  (body, head, panel, eye); break;
            case 3: BuildWedge (body, head, panel, eye); break;
        }
    }

    private void BuildChunky(Color body, Color head, Color panel, Color eye)
    {
        P([ new(-14,-2), new(14,-2), new(14,20), new(-14,20) ], body);
        P([ new(-11,-22), new(11,-22), new(13,-2), new(-13,-2) ], head);
        P([ new( -9,-20), new( 9,-20), new( 9,-4), new( -9,-4) ], panel);
        P([ new( -8,-15), new( 8,-15), new( 8,-9), new( -8,-9) ], eye);
        _leftHip  = new Vector2(-9f, 20f);
        _rightHip = new Vector2( 9f, 20f);
    }

    private void BuildTall(Color body, Color head, Color panel, Color eye)
    {
        P([ new(-9,-2), new(9,-2), new(9,20), new(-9,20) ], body);
        P([ new(-10,-22), new(10,-22), new(11,-2), new(-11,-2) ], head);
        P([ new( -8,-20), new( 8,-20), new( 8,-4), new( -8,-4) ], panel);
        P([ new(-7,-17), new(-2,-17), new(-2,-11), new(-7,-11) ], eye);
        P([ new( 2,-17), new( 7,-17), new( 7,-11), new( 2,-11) ], eye);
        _leftHip  = new Vector2(-8f, 20f);
        _rightHip = new Vector2( 8f, 20f);
    }

    private void BuildTank(Color body, Color head, Color panel, Color eye)
    {
        P([ new(-14, 4), new(14, 4), new(14,22), new(-14,22) ], body);
        P([ new(-14,-14), new(14,-14), new(14, 4), new(-14, 4) ], head);
        P([ new(-12,-12), new(12,-12), new(12, 2), new(-12, 2) ], panel);
        P([ new(-10,-9), new(-5,-9), new(-5,-4), new(-10,-4) ], eye);
        P([ new( -2,-9), new( 2,-9), new( 2,-4), new( -2,-4) ], eye);
        P([ new(  5,-9), new(10,-9), new(10,-4), new(  5,-4) ], eye);
        _leftHip  = new Vector2(-9f, 22f);
        _rightHip = new Vector2( 9f, 22f);
    }

    private void BuildWedge(Color body, Color head, Color panel, Color eye)
    {
        P([ new(-9,-2), new(9,-2), new(14,22), new(-14,22) ], body);
        P([ new(0,-24), new(9,-2), new(-9,-2) ], head);
        P([ new(0,-22), new(6,-4), new(-6,-4) ], panel);
        P([ new(-4,-14), new(4,-14), new(4,-9), new(-4,-9) ], eye);
        _leftHip  = new Vector2(-10f, 20f);
        _rightHip = new Vector2( 10f, 20f);
    }

    private void P(Vector2[] pts, Color col)
        => _bodyRoot.AddChild(new Polygon2D { Polygon = pts, Color = col });

    // =========================================================================
    // Wheel animation — spokes rotate with rolling constraint
    // =========================================================================

    private void AnimateLegs(float delta)
    {
        const float WheelR = 6f;
        _stridePhase       += Velocity.X * delta / WheelR;  // rolling without slipping
        _leftShin.Rotation  = _stridePhase;
        _rightShin.Rotation = _stridePhase;
    }

    // =========================================================================
    // Animated skin patterns — chosen once per match, same for all players
    // =========================================================================

    public override void _Process(double delta)
    {
        _patternTime += (float)delta;
        UpdatePattern();
        UpdateShieldVisual();
    }

    private void UpdateShieldVisual()
    {
        if (!_pwrShield)
        {
            _shieldVisual.Visible = false;
            return;
        }
        _shieldVisual.Visible = true;
        float pulse = Mathf.Sin(_patternTime * 4.5f) * 0.5f + 0.5f;
        _shieldVisual.Color = new Color(0.55f, 0.85f, 1.00f, 0.12f + pulse * 0.18f);
    }

    private void SetupPattern(Color accent)
    {
        const float BW = 12f, BT = -20f, BB = 20f;
        var tr = new Color(0f, 0f, 0f, 0f);  // transparent placeholder

        switch (_patternChoice)
        {
            case 0:  // PULSE — single overlay rect whose alpha oscillates
                _patternPolys = [Overlay(new Vector2[] { new(-BW,BT),new(BW,BT),new(BW,BB),new(-BW,BB) }, tr)];
                break;

            case 1:  // SCANLINES — 4 dark bands that scroll downward
                _patternPolys = new Polygon2D[4];
                for (int i = 0; i < 4; i++)
                    _patternPolys[i] = Overlay(new Vector2[] { new(-BW,0f),new(BW,0f),new(BW,2.5f),new(-BW,2.5f) },
                                               new Color(0f, 0f, 0f, 0.28f));
                break;

            case 2:  // SWEEP — 2 glowing vertical strips scanning across
                _patternPolys = new Polygon2D[2];
                for (int i = 0; i < 2; i++)
                    _patternPolys[i] = Overlay(new Vector2[] { new(0f,BT),new(3f,BT),new(3f,BB),new(0f,BB) }, tr);
                break;

            case 3:  // GRID — 3×3 pulsing LED dots
                _patternPolys = new Polygon2D[9];
                float[] xs = [-8f, 0f, 8f];
                float[] ys = [-13f, -3f, 7f];
                for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    var p = Overlay(new Vector2[] { new(-2f,-2f),new(2f,-2f),new(2f,2f),new(-2f,2f) }, tr);
                    p.Position         = new Vector2(xs[c], ys[r]);
                    _patternPolys[r*3+c] = p;
                }
                break;
        }

        foreach (var p in _patternPolys) _bodyRoot.AddChild(p);
    }

    // Creates a Polygon2D overlay, added to _bodyRoot later by SetupPattern.
    private static Polygon2D Overlay(Vector2[] pts, Color col)
        => new Polygon2D { Polygon = pts, Color = col };

    private void UpdatePattern()
    {
        var   ac = ColorFor(PlayerIndex);
        const float BW = 12f, BT = -20f, BB = 20f, BH = 40f;

        switch (_patternChoice)
        {
            case 0:  // PULSE
            {
                float a = (Mathf.Sin(_patternTime * 2.4f) * 0.5f + 0.5f) * 0.22f;
                _patternPolys[0].Color = new Color(ac.R, ac.G, ac.B, a);
                break;
            }
            case 1:  // SCANLINES
            {
                float spacing = BH / _patternPolys.Length;
                for (int i = 0; i < _patternPolys.Length; i++)
                {
                    float y = BT + ((_patternTime * 20f + i * spacing) % BH);
                    _patternPolys[i].Polygon = new Vector2[] { new(-BW,y),new(BW,y),new(BW,y+2.5f),new(-BW,y+2.5f) };
                }
                break;
            }
            case 2:  // SWEEP
            {
                for (int i = 0; i < _patternPolys.Length; i++)
                {
                    float phase = (_patternTime * 0.65f + i * 0.5f) % 1.0f;
                    float x     = Mathf.Lerp(-BW, BW, phase);
                    float a     = Mathf.Sin(phase * Mathf.Pi) * 0.60f;
                    _patternPolys[i].Polygon = new Vector2[] { new(x-1.5f,BT),new(x+1.5f,BT),new(x+1.5f,BB),new(x-1.5f,BB) };
                    _patternPolys[i].Color   = new Color(ac.R, ac.G, ac.B, a);
                }
                break;
            }
            case 3:  // GRID
            {
                for (int i = 0; i < _patternPolys.Length; i++)
                {
                    float a = (Mathf.Sin(_patternTime * 2.1f + i * 0.72f) * 0.5f + 0.5f) * 0.75f + 0.05f;
                    _patternPolys[i].Color = new Color(ac.R, ac.G, ac.B, a);
                }
                break;
            }
        }
    }

    // Ellipse-ish polygon for shield bubble (rx = half-width, ry = half-height)
    private static Vector2[] ShieldPoly(float rx, float ry, int segs)
    {
        var pts = new Vector2[segs];
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.Tau / segs;
            pts[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }

    private static Vector2[] CirclePoly(float r, int segs)
    {
        var pts = new Vector2[segs];
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.Tau / segs;
            pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return pts;
    }

    // =========================================================================
    // Boundary clamp — applied during shrink phase
    // =========================================================================

    private void ClampToBounds()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsShrinking) return;

        float lb = gm.EffectiveLeft  + PlayerHalfWidth;
        float rb = gm.EffectiveRight - PlayerHalfWidth;

        var pos = GlobalPosition;
        var vel = Velocity;

        if (pos.X < lb) { pos.X = lb; if (vel.X < 0f) vel.X = 0f; }
        if (pos.X > rb) { pos.X = rb; if (vel.X > 0f) vel.X = 0f; }

        GlobalPosition = pos;
        Velocity       = vel;
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

        if (_pwrShield)
        {
            _pwrShield = false;
            RefreshIndicators();
            return;
        }

        IsEliminated    = true;
        _deathTimer     = DeathDuration;
        _collisionShape.Disabled = true;  // stop blocking others immediately
        Velocity        = Vector2.Zero;
        Play(_sndSquish!);
        GameManager.Instance?.OnPlayerEliminated(PlayerIndex, killerIndex);
        // QueueFree happens at end of TickDeathAnimation
    }

    private void TickDeathAnimation(float delta)
    {
        _deathTimer -= delta;
        float t = 1f - Mathf.Max(_deathTimer / DeathDuration, 0f);  // 0 → 1

        float scaleX, scaleY;
        if (t < 0.18f)
        {
            // Brief upward stretch — anticipation
            float p = t / 0.18f;
            scaleX = Mathf.Lerp(1.00f, 0.70f, p);
            scaleY = Mathf.Lerp(1.00f, 1.45f, p);
        }
        else
        {
            // Rapid squash to a flat pancake, then fade out
            float p    = (t - 0.18f) / 0.82f;
            float ease = 1f - Mathf.Pow(1f - p, 2.5f);  // fast start, eases off
            scaleX = Mathf.Lerp(0.70f, 3.20f, ease);
            scaleY = Mathf.Lerp(1.45f, 0.00f, ease);
        }

        _bodyRoot.Scale = new Vector2(scaleX, scaleY);

        if (_deathTimer <= 0f)
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

    private static AudioStreamWav SynthSquish()
    {
        const int   Rate = 8000;
        const float Dur  = 0.45f;
        int   n     = (int)(Rate * Dur);
        var   data  = new byte[n];
        var   rng   = new System.Random(99);
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;

            // Low thump: sine sweeping quickly downward (impact body)
            float freq = 180f * Mathf.Exp(-t * 7f) + 35f;
            phase += freq / Rate;
            float thump = Mathf.Sin(phase * Mathf.Pi * 2f) * Mathf.Exp(-t * 6f);

            // Wet splat: noise burst that decays fast
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float splat = noise * Mathf.Exp(-t * 28f);

            // Squeaky high chirp at the very start (the squish moment)
            float chirpFreq = 1200f - 900f * t;
            float chirp     = Mathf.Sin(2f * Mathf.Pi * chirpFreq * t) * Mathf.Exp(-t * 40f) * 0.5f;

            float s = thump * 0.50f + splat * 0.30f + chirp * 0.20f;
            data[i] = (byte)(sbyte)(Mathf.Clamp(s * 115f, -127f, 127f));
        }

        return new AudioStreamWav
        {
            Data    = data,
            Format  = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = Rate,
            Stereo  = false
        };
    }

    private static AudioStreamWav SynthPowerUp()
    {
        const int   Rate = 8000;
        const float Dur  = 0.22f;
        int   n     = (int)(Rate * Dur);
        var   data  = new byte[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / n;
            float freq = 480f + 620f * t;           // sweep 480 → 1100 Hz
            phase += freq / Rate;
            float env  = t < 0.08f ? t / 0.08f      // sharp attack
                       : Mathf.Exp(-(t - 0.08f) * 9f); // smooth decay
            float wave = Mathf.Sin(phase * Mathf.Pi * 2f);
            data[i] = (byte)(sbyte)(wave * env * 95f);
        }

        return new AudioStreamWav
        {
            Data    = data,
            Format  = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = Rate,
            Stereo  = false
        };
    }

    private static AudioStreamWav SynthStomp()
    {
        const int   Rate = 8000;
        const float Dur  = 0.14f;
        int  n   = (int)(Rate * Dur);
        var  data = new byte[n];
        var  rng  = new System.Random(7);

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
