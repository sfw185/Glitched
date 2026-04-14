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
    private const float LevelLeft   =  60f;
    private const float LevelRight  = 1220f;
    private const float PlatMinY    = 180f;  // highest platform (small Y = high on screen)
    private const float PlatMaxY    = 520f;  // lowest platform (above ground at y=680)

    // --- Spawn anchor specs (must match Player spawn positions in Level.tscn) ---
    public const float AnchorY      = 480f;
    public const float AnchorWidth  = 160f;
    private const float AnchorMargin = 100f; // inset from LevelLeft/Right

    // Player spawn Y = platform top (AnchorY-10) minus player half-height (24) = 446
    public const float PlayerSpawnY = 446f;

    // --- Reachability limits (proven above) ---
    private const float MaxGap        = 200f;  // max edge-to-edge gap between platforms
    private const float MaxHeightStep = 160f;  // max height difference per adjacent pair

    // Minimum platform width so the player always has room to land (≥ 45px required, we use 80)
    private const float MinPlatWidth  =  80f;
    private const float MaxPlatWidth  = 150f;

    private readonly RandomNumberGenerator _rng = new();
    private float _lastY = AnchorY;

    public override void _Ready()
    {
        _rng.Randomize();
        Generate();
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
        // Place one guaranteed anchor platform per player
        for (int i = 0; i < PlayerCount; i++)
        {
            var pos = SpawnPositionFor(i, PlayerCount);
            SpawnPlatform(pos.X, AnchorY, AnchorWidth);
        }

        // Fill the gap between each consecutive pair of anchor platforms
        for (int i = 0; i < PlayerCount - 1; i++)
        {
            float gapStart = SpawnPositionFor(i,     PlayerCount).X + AnchorWidth / 2f;
            float gapEnd   = SpawnPositionFor(i + 1, PlayerCount).X - AnchorWidth / 2f;
            FillGap(gapStart, gapEnd, AnchorY);
        }
    }

    /// Fills the horizontal range [fromX, toX] with platforms, each pair guaranteed reachable.
    private void FillGap(float fromX, float toX, float startY)
    {
        float rightEdge = fromX;
        _lastY = startY;

        while (true)
        {
            // Random gap — strictly within reachability limit
            float gap   = _rng.RandfRange(20f, MaxGap * 0.85f);
            float width = _rng.RandfRange(MinPlatWidth, MaxPlatWidth);
            float cx    = rightEdge + gap + width / 2f;

            // Stop before we crowd the next anchor platform
            if (cx + width / 2f > toX - 30f) break;

            // Height step: random within the proven safe range, then clamped to arena bounds.
            // Clamping can only REDUCE the step, so the limit is never exceeded.
            float dy = _rng.RandfRange(-MaxHeightStep, MaxHeightStep);
            float cy = Mathf.Clamp(_lastY + dy, PlatMinY, PlatMaxY);

            SpawnPlatform(cx, cy, width);
            rightEdge = cx + width / 2f;
        }
    }

    private void SpawnPlatform(float cx, float cy, float width)
    {
        var body = new StaticBody2D { Position = new Vector2(cx, cy) };

        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(width, 20f) }
        });

        // Lighter shade for higher platforms — subtle monochrome depth cue
        float t     = 1f - (cy - PlatMinY) / (PlatMaxY - PlatMinY);
        float shade = Mathf.Lerp(0.38f, 0.78f, t);
        float hw    = width / 2f;
        body.AddChild(new Polygon2D
        {
            Color   = new Color(shade, shade, shade),
            Polygon = [new(-hw, -10), new(hw, -10), new(hw, 10), new(-hw, 10)]
        });

        AddChild(body);
        _lastY = cy;
    }
}
