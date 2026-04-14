using Godot;

/// Generates a procedural platform layout guaranteed to be fully reachable with double-jump.
///
/// Reachability proof (see Player.cs for physics constants):
///   v=450, g=980, spd=200
///   Double jump (second jump at first apex):
///     max height  ≈ 206px   |   max air time ≈ 1.84s   |   max dist ≈ 367px
///
///   Chosen conservative limits:
///     MaxGap        = 200px (edge-to-edge)
///     MaxHeightStep = 160px (per adjacent platform, up or down)
///
///   Verification at the hardest case (gap=200, height=160):
///     At t = 200/200 = 1.0s, player height = 203px  ≥  160px  ✓
///     Player descends back to 160px at x ≈ 245px — lands 45px onto platform.
///     Minimum platform width is 80px, so there is always room to land.  ✓
public partial class LevelGenerator : Node2D
{
    [Export] public int PlayerCount = 2;

    // --- Arena bounds (must match wall/ground positions in Level.tscn) ---
    private const float LevelLeft   =   30f;
    private const float LevelRight  = 1220f;
    private const float PlatMinY    =  180f;  // highest platform
    private const float PlatMaxY    =  520f;  // lowest platform (above ground)

    // --- Spawn anchor specs ---
    public const float AnchorY      = 480f;
    public const float AnchorWidth  = 160f;
    private const float AnchorMargin =  100f;

    // Player spawn Y = platform top (AnchorY-10) minus player half-height (24) = 446
    public const float PlayerSpawnY = 446f;

    // --- Reachability limits ---
    private const float MaxGap        = 200f;
    private const float MaxHeightStep = 160f;
    private const float MinPlatWidth  =  80f;
    private const float MaxPlatWidth  = 150f;

    // =========================================================================
    // Level theme — randomised each run, colours & decorations change together
    // =========================================================================

    public enum LevelTheme { Circuit, Pulse, Hazard }
    public static LevelTheme CurrentTheme { get; private set; }

    // Exposed so GameManager can tint shrink walls and HUD to match the level palette.
    public static Color ThemeAccent { get; private set; }
    public static Color ThemeDark   { get; private set; }

    private readonly record struct ThemeData(
        Color Dark, Color Bright, Color Accent);

    // Three sci-fi palettes.  Dark/Bright drive the height-based depth shading;
    // Accent is used for decorative detail elements on each platform.
    private static readonly ThemeData[] _themes =
    [
        // Circuit — deep green, circuit-board traces
        new(new Color(0.08f, 0.15f, 0.08f),
            new Color(0.20f, 0.38f, 0.20f),
            new Color(0.20f, 0.88f, 0.28f)),

        // Pulse — dark navy, cyan scan lines
        new(new Color(0.07f, 0.09f, 0.20f),
            new Color(0.15f, 0.18f, 0.38f),
            new Color(0.28f, 0.62f, 1.00f)),

        // Hazard — amber rust, diagonal warning marks
        new(new Color(0.17f, 0.11f, 0.04f),
            new Color(0.32f, 0.22f, 0.08f),
            new Color(1.00f, 0.62f, 0.08f)),
    ];

    // =========================================================================

    private readonly RandomNumberGenerator _rng = new();
    private float _lastY = AnchorY;

    public override void _Ready()
    {
        _rng.Randomize();
        CurrentTheme = (LevelTheme)_rng.RandiRange(0, _themes.Length - 1);
        ThemeAccent  = _themes[(int)CurrentTheme].Accent;
        ThemeDark    = _themes[(int)CurrentTheme].Dark;
        Generate();
        DecorateArena();
    }

    /// World-space spawn position for a given player index.
    public static Vector2 SpawnPositionFor(int playerIndex, int playerCount)
    {
        float start = LevelLeft  + AnchorMargin;
        float end   = LevelRight - AnchorMargin;
        float t     = playerCount > 1 ? (float)playerIndex / (playerCount - 1) : 0.5f;
        return new Vector2(Mathf.Lerp(start, end, t), PlayerSpawnY);
    }

    // -------------------------------------------------------------------------

    private void Generate()
    {
        // One guaranteed anchor platform under each player spawn
        for (int i = 0; i < PlayerCount; i++)
        {
            var pos = SpawnPositionFor(i, PlayerCount);
            SpawnPlatform(pos.X, AnchorY, AnchorWidth);
        }

        // Fill the gap between consecutive anchor pairs
        for (int i = 0; i < PlayerCount - 1; i++)
        {
            float gapStart = SpawnPositionFor(i,     PlayerCount).X + AnchorWidth / 2f;
            float gapEnd   = SpawnPositionFor(i + 1, PlayerCount).X - AnchorWidth / 2f;
            FillGap(gapStart, gapEnd, AnchorY);
        }
    }

    /// Fills [fromX, toX] with platforms, each pair guaranteed reachable.
    private void FillGap(float fromX, float toX, float startY)
    {
        float rightEdge = fromX;
        _lastY = startY;

        while (true)
        {
            float gap   = _rng.RandfRange(20f, MaxGap * 0.85f);
            float width = _rng.RandfRange(MinPlatWidth, MaxPlatWidth);
            float cx    = rightEdge + gap + width / 2f;

            if (cx + width / 2f > toX - 30f) break;

            float dy = _rng.RandfRange(-MaxHeightStep, MaxHeightStep);
            float cy = Mathf.Clamp(_lastY + dy, PlatMinY, PlatMaxY);

            SpawnPlatform(cx, cy, width);
            rightEdge = cx + width / 2f;
        }
    }

    private void SpawnPlatform(float cx, float cy, float width)
    {
        var td = _themes[(int)CurrentTheme];

        // Height-based shading: higher platforms are brighter (depth cue)
        float t    = 1f - (cy - PlatMinY) / (PlatMaxY - PlatMinY);
        Color base_ = td.Dark.Lerp(td.Bright, t);

        float hw   = width / 2f;
        var   body = new StaticBody2D { Position = new Vector2(cx, cy) };

        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(width, 20f) }
        });

        // Base rectangle
        body.AddChild(new Polygon2D
        {
            Color   = base_,
            Polygon = [new(-hw,-10), new(hw,-10), new(hw,10), new(-hw,10)]
        });

        // Theme-specific decorations
        switch (CurrentTheme)
        {
            case LevelTheme.Circuit:
                DecorateCircuit(body, hw, td.Accent);
                break;
            case LevelTheme.Pulse:
                DecoratePulse(body, hw, td.Accent);
                break;
            case LevelTheme.Hazard:
                DecorateHazard(body, hw, td.Accent);
                break;
        }

        AddChild(body);
        _lastY = cy;
    }

    // -------------------------------------------------------------------------
    // Decoration helpers
    // -------------------------------------------------------------------------

    /// Circuit board: top edge glow, horizontal trace, via pads.
    private static void DecorateCircuit(Node body, float hw, Color accent)
    {
        var dim = DimAccent(accent, 0.65f);

        // Top-edge highlight bar
        AddRect(body, -hw, -10f, hw * 2f, 2f, dim);

        if (hw < 24f) return;  // too narrow for trace detail

        // Horizontal trace
        AddRect(body, -hw + 8f, -6f, hw * 2f - 16f, 1.5f, dim);

        // Via pads at regular intervals
        float spacing = Mathf.Max(18f, (hw * 2f - 20f) / 4f);
        for (float vx = -hw + 10f; vx < hw - 6f; vx += spacing)
            AddRect(body, vx - 2.5f, -8f, 5f, 5f, accent);

        // Corner L-brackets (top-left and top-right)
        AddRect(body, -hw,      -10f, 5f,   2f, accent);
        AddRect(body, -hw,      -10f, 2f,   5f, accent);
        AddRect(body,  hw - 5f, -10f, 5f,   2f, accent);
        AddRect(body,  hw - 2f, -10f, 2f,   5f, accent);
    }

    /// Pulse / scan: evenly-spaced vertical bright lines.
    private static void DecoratePulse(Node body, float hw, Color accent)
    {
        // Top edge
        AddRect(body, -hw, -10f, hw * 2f, 2f, DimAccent(accent, 0.55f));

        // Vertical scan stripes every 14 px
        for (float sx = -hw + 7f; sx < hw - 3f; sx += 14f)
            AddRect(body, sx, -10f, 2f, 20f, DimAccent(accent, 0.25f));

        // Bright tick marks at top (every other stripe)
        int tick = 0;
        for (float sx = -hw + 7f; sx < hw - 3f; sx += 14f, tick++)
        {
            if (tick % 2 == 0)
                AddRect(body, sx, -10f, 2f, 4f, accent);
        }
    }

    /// Hazard: amber diagonal chevrons — classic warning stripe.
    private static void DecorateHazard(Node body, float hw, Color accent)
    {
        var dim = DimAccent(accent, 0.55f);

        // Top double stripe
        AddRect(body, -hw, -10f, hw * 2f, 2f, dim);
        AddRect(body, -hw,  -7f, hw * 2f, 1f, DimAccent(accent, 0.30f));

        // Diagonal parallelogram slashes (classic hazard chevron)
        // Each slash: parallelogram leaning right, width ~8px, height = full 20px
        const float SlashW = 6f;
        for (float sx = -hw + 4f; sx < hw - 2f; sx += 18f)
        {
            // Polygon: shifted-right at top, shifted-left at bottom
            body.AddChild(new Polygon2D
            {
                Color   = dim,
                Polygon =
                [
                    new(sx + 4f, -10f), new(sx + 4f + SlashW, -10f),
                    new(sx + SlashW,  10f), new(sx,            10f)
                ]
            });
        }
    }

    // =========================================================================
    // Utility
    // =========================================================================

    private static void AddRect(Node parent, float x, float y, float w, float h, Color col)
        => parent.AddChild(new Polygon2D
        {
            Color   = col,
            Polygon = [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)]
        });

    private static Color DimAccent(Color c, float factor)
        => new(c.R * factor, c.G * factor, c.B * factor);

    // =========================================================================
    // Arena boundary decoration — floor, walls, ceiling all get theme treatment
    // =========================================================================

    private void DecorateArena()
    {
        var td    = _themes[(int)CurrentTheme];
        var level = GetParent();
        var edge  = DimAccent(td.Accent, 0.7f);

        // ---- Ground ----
        // Re-colour the existing scene Visual and add surface decoration strips.
        // Ground node is at world (640, 700); Visual local coords: x −640…640, y −20…20.
        level.GetNode<Polygon2D>("Ground/Visual").Color = td.Dark;
        var ground = level.GetNode<Node2D>("Ground");

        // Top-edge glow line
        AddRect(ground, -640f, -10f, 1280f, 2f, edge);
        // Theme surface marks
        DecorateGroundSurface(ground, td);

        // ---- Left boundary strip ——————————————————————————————————————————
        // WallLeft sits at world (−2, 360); local x = world x + 2.
        // We paint a strip from local x=2 (world 0) inward to local x=20 (world 18).
        var wallL = level.GetNode<Node2D>("WallLeft");
        AddRect(wallL,   2f, -460f, 18f, 920f, td.Dark);   // fill  (world 0..18)
        AddRect(wallL,  18f, -460f,  2f, 920f, edge);       // inner edge line (world 16..18)
        DecorateWallSurface(wallL, td, 2f, 18f, 920f, -460f);

        // ---- Right boundary strip —————————————————————————————————————————
        // WallRight at world (1282, 360); local x = world x − 1282.
        // Strip: local x=−20 (world 1262) to local x=−2 (world 1280).
        var wallR = level.GetNode<Node2D>("WallRight");
        AddRect(wallR, -20f, -460f, 18f, 920f, td.Dark);   // fill  (world 1262..1280)
        AddRect(wallR, -20f, -460f,  2f, 920f, edge);       // inner edge line (world 1262..1264)
        DecorateWallSurface(wallR, td, -20f, 18f, 920f, -460f);

        // ---- Ceiling ——————————————————————————————————————————————————————
        // Ceiling at world (640, 34); local y = world y − 34.
        // Bottom face of collision shape at local y=20 = world y=54 (HUD bottom).
        // Paint a strip just below that, visible in game area: local y=20..42 = world 54..76.
        var ceil = level.GetNode<Node2D>("Ceiling");
        AddRect(ceil, -680f, 20f, 1360f, 22f, td.Dark);
        AddRect(ceil, -680f, 20f, 1360f,  2f, edge);
    }

    // Ground surface marks (Ground local space: x −640…640, y −20…20)
    private void DecorateGroundSurface(Node2D ground, ThemeData td)
    {
        switch (CurrentTheme)
        {
            case LevelTheme.Circuit:
                // Horizontal trace + via pads
                AddRect(ground, -620f, -7f, 1240f, 1.5f, DimAccent(td.Accent, 0.55f));
                for (float vx = -600f; vx < 600f; vx += 80f)
                    AddRect(ground, vx - 3f, -10f, 6f, 5f, td.Accent);
                break;

            case LevelTheme.Pulse:
                // Vertical scan ticks across the full floor
                for (float sx = -630f; sx < 630f; sx += 20f)
                    AddRect(ground, sx, -20f, 2f, 30f, DimAccent(td.Accent, 0.22f));
                // Bright ticks every 4th stripe
                for (float sx = -630f; sx < 630f; sx += 80f)
                    AddRect(ground, sx, -20f, 2f, 5f, DimAccent(td.Accent, 0.70f));
                break;

            case LevelTheme.Hazard:
                // Diagonal parallelogram slashes across the floor
                for (float sx = -640f; sx < 640f; sx += 32f)
                    ground.AddChild(new Polygon2D
                    {
                        Color   = DimAccent(td.Accent, 0.30f),
                        Polygon = [
                            new(sx + 6f, -20f), new(sx + 14f, -20f),
                            new(sx + 8f,  10f), new(sx,        10f)
                        ]
                    });
                break;
        }
    }

    // Wall surface marks (in the wall node's local space, x0 is the left edge of the strip)
    private void DecorateWallSurface(Node2D wall, ThemeData td,
                                      float x0, float stripW, float stripH, float y0)
    {
        switch (CurrentTheme)
        {
            case LevelTheme.Circuit:
                // Horizontal circuit traces at regular vertical intervals
                for (float ty = y0 + 30f; ty < y0 + stripH - 10f; ty += 60f)
                    AddRect(wall, x0 + 2f, ty, stripW - 4f, 1.5f, DimAccent(td.Accent, 0.55f));
                // Small via pads at every other trace
                for (float ty = y0 + 30f; ty < y0 + stripH - 10f; ty += 120f)
                    AddRect(wall, x0 + stripW / 2f - 3f, ty - 3f, 6f, 6f, td.Accent);
                break;

            case LevelTheme.Pulse:
                // Horizontal bright bars (scan lines)
                for (float ty = y0 + 20f; ty < y0 + stripH - 10f; ty += 28f)
                    AddRect(wall, x0 + 2f, ty, stripW - 4f, 2f, DimAccent(td.Accent, 0.40f));
                break;

            case LevelTheme.Hazard:
                // Diagonal slashes running down the wall strip
                for (float ty = y0 + 10f; ty < y0 + stripH - 20f; ty += 36f)
                    wall.AddChild(new Polygon2D
                    {
                        Color   = DimAccent(td.Accent, 0.32f),
                        Polygon = [
                            new(x0 + 2f,          ty),
                            new(x0 + stripW - 2f, ty + 10f),
                            new(x0 + stripW - 2f, ty + 14f),
                            new(x0 + 2f,          ty +  4f)
                        ]
                    });
                break;
        }
    }
}
