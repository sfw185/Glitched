using Godot;
using System.Linq;

public partial class GameManager : Node
{
	public static GameManager? Instance { get; private set; }

	[Export] public int PlayerCount = 2;

	// --- Round timing (seconds) ---
	private const float PlayDuration   = 30f;
	private const float ShrinkDuration = 30f;
	private const float RoundEndPause  =  3f;

	// --- Arena geometry ---
	private const float ViewportWidth  = 1280f;
	private const float ViewportHeight =   720f;
	private const float ArenaCenterX   =   640f;

	// Minimum safe zone: proven reachable with double jump.
	// At this width (350 px) any two platforms within range satisfy MaxGap ≤ 200 px
	// and MaxHeightStep ≤ 160 px — see LevelGenerator for the physics proof.
	private const float MinArenaWidth  =  350f;

	// Width of the visible wall face that advances into the arena.
	// Must equal the static boundary strip width in LevelGenerator.DecorateArena
	// so the shrink wall starts exactly where the permanent strip ends — no jolt.
	private const float WallFaceWidth  =   18f;

	// Exposed so Player can soft-clamp during the non-shrink phase.
	public  const float StaticBoundaryInset = WallFaceWidth;
	public  const float ViewportWidthConst  = ViewportWidth;

	// Pixels reserved at the top of the viewport for the HUD bar.
	private const float HudHeight = 54f;

	// --- Match scoring ---
	private const int WinScore = 5;

	// Static fields survive ReloadCurrentScene so scores persist across rounds.
	private static int[]? _scores;
	private static int    _scoredPlayerCount;

	// --- Phase state machine ---
	private enum Phase { Playing, Shrinking, RoundEnd, MatchEnd }
	private Phase _phase        = Phase.Playing;
	private float _phaseTimer   = 0f;
	private float _endTimer     = 0f;
	private bool  _shrinkLocked = false;   // true once walls reach minimum size

	// --- Live arena bounds — read by Player.ClampToBounds each physics tick ---
	public float LeftBound    { get; private set; } = 0f;
	public float RightBound   { get; private set; } = ViewportWidth;
	public bool  IsShrinking  => _phase == Phase.Shrinking;

	// Inner edge of the wall face — the actual playable boundary players are clamped to.
	public float EffectiveLeft  => LeftBound  + WallFaceWidth;
	public float EffectiveRight => RightBound - WallFaceWidth;

	// --- HUD nodes (created at runtime) ---
	private Label[] _scoreLabels  = [];
	private Label   _timerLabel   = null!;
	private Label   _messageLabel = null!;

	// --- Danger zone visuals (HUD CanvasLayer ColorRects, created at runtime) ---
	// Rendered in the HUD CanvasLayer so they always appear above 2D world content.
	private ColorRect _leftOverlay  = null!;
	private ColorRect _rightOverlay = null!;
	private ColorRect _leftWall     = null!;
	private ColorRect _rightWall    = null!;
	private ColorRect _leftEdge     = null!;
	private ColorRect _rightEdge    = null!;

	// --- Audio ---
	private AudioStreamPlayer _audio = null!;

	private static AudioStreamWav? _sndRoundStart;
	private static AudioStreamWav? _sndEliminate;

	// -------------------------------------------------------------------------
	// Lifecycle
	// -------------------------------------------------------------------------

	public override void _Ready()
	{
		Instance = this;

		// Reuse scores if they exist for the same player count; otherwise reset.
		if (_scores == null || _scoredPlayerCount != PlayerCount)
		{
			_scores            = new int[PlayerCount];
			_scoredPlayerCount = PlayerCount;
		}

		LeftBound  = 0f;
		RightBound = ViewportWidth;

		SetupDangerZones();
		SetupHUD();

		_sndRoundStart ??= SynthRoundStart();
		_sndEliminate  ??= SynthEliminate();
		_audio = new AudioStreamPlayer { VolumeDb = -4f };
		AddChild(_audio);

		PlaySound(_sndRoundStart!);
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	// -------------------------------------------------------------------------
	// Public API (called by Player)
	// -------------------------------------------------------------------------

	/// Called by Player.Eliminate() when a stomp occurs.
	/// Victim's IsEliminated flag is already true when this is called.
	public void OnPlayerEliminated(int victimIndex, int _killerIndex)
	{
		if (_phase is Phase.RoundEnd or Phase.MatchEnd) return;

		PlaySound(_sndEliminate!);

		// Because victim.IsEliminated is already true, they are excluded here.
		var survivors = GetTree()
			.GetNodesInGroup("players")
			.OfType<Player>()
			.Where(p => !p.IsEliminated)
			.ToList();

		if (survivors.Count == 1)
			HandleRoundWin(survivors[0].PlayerIndex);
		else if (survivors.Count == 0)
			StartRoundEnd("Draw!");
		// else: 3+ player game, round continues
	}

	// -------------------------------------------------------------------------
	// Update loop
	// -------------------------------------------------------------------------

	public override void _Process(double delta)
	{
		switch (_phase)
		{
			case Phase.Playing:   UpdatePlaying(delta);   break;
			case Phase.Shrinking: UpdateShrinking(delta); break;
			case Phase.RoundEnd:  UpdateRoundEnd(delta);  break;
			case Phase.MatchEnd:  UpdateMatchEnd();       break;
		}
	}

	private void UpdatePlaying(double delta)
	{
		_phaseTimer += (float)delta;
		float remaining = PlayDuration - _phaseTimer;
		_timerLabel.Text     = Mathf.Max(1, Mathf.CeilToInt(remaining)).ToString();
		_timerLabel.Modulate = Colors.White;

		if (_phaseTimer >= PlayDuration)
			StartShrinking();
	}

	private void UpdateShrinking(double delta)
	{
		_phaseTimer += (float)delta;

		// Advance boundaries inward — lerp clamps at t=1 so bounds freeze naturally
		float t    = Mathf.Clamp(_phaseTimer / ShrinkDuration, 0f, 1f);
		float half = Mathf.Lerp(ViewportWidth / 2f, MinArenaWidth / 2f, t);
		LeftBound  = ArenaCenterX - half;
		RightBound = ArenaCenterX + half;

		if (_phaseTimer >= ShrinkDuration)
		{
			// Walls fully closed — freeze visuals solid on the first frame, then stop updating
			if (!_shrinkLocked)
			{
				_shrinkLocked        = true;
				_leftWall.Color      = LevelGenerator.ThemeAccent;
				_rightWall.Color     = LevelGenerator.ThemeAccent;
				UpdateDangerZoneVisuals(pulse: false);
			}
			_timerLabel.Visible = false;
			return;
		}

		UpdateDangerZoneVisuals(pulse: true);

		// Timer display — flash to signal urgency
		float remaining      = ShrinkDuration - _phaseTimer;
		_timerLabel.Text     = Mathf.CeilToInt(remaining).ToString();
		bool flashOn         = (_phaseTimer % 0.5f) < 0.25f;
		_timerLabel.Modulate = flashOn ? Colors.White : new Color(0.45f, 0.45f, 0.45f, 1f);
	}

	private void UpdateRoundEnd(double delta)
	{
		_endTimer -= (float)delta;
		if (_endTimer <= 0f)
			GetTree().ReloadCurrentScene();
	}

	private void UpdateMatchEnd()
	{
		for (int i = 0; i < PlayerCount; i++)
		{
			if (!Input.IsActionJustPressed($"p{i + 1}_jump")) continue;
			_scores = null; // reset for new match
			GetTree().ReloadCurrentScene();
			return;
		}
	}

	// -------------------------------------------------------------------------
	// Phase transitions
	// -------------------------------------------------------------------------

	private void StartShrinking()
	{
		_phase      = Phase.Shrinking;
		_phaseTimer = 0f;
	}

	private void HandleRoundWin(int winnerIndex)
	{
		_scores![winnerIndex]++;
		UpdateScoreUI(winnerIndex);

		if (_scores[winnerIndex] >= WinScore)
			StartMatchEnd(winnerIndex);
		else
			StartRoundEnd($"P{winnerIndex + 1} scores!  ({_scores[winnerIndex]}/{WinScore})");
	}

	private void StartRoundEnd(string message)
	{
		_phase                 = Phase.RoundEnd;
		_endTimer              = RoundEndPause;
		_messageLabel.Text     = message;
		_messageLabel.Visible  = true;
		_timerLabel.Visible    = false;
	}

	private void StartMatchEnd(int winnerIndex)
	{
		_phase = Phase.MatchEnd;
		_messageLabel.Text    = $"P{winnerIndex + 1} wins the match!\n\nPress jump to play again";
		_messageLabel.Visible = true;
		_timerLabel.Visible   = false;
	}

	// -------------------------------------------------------------------------
	// HUD setup (all labels created at runtime)
	// -------------------------------------------------------------------------

	private void SetupHUD()
	{
		var hud    = GetNode<CanvasLayer>("../HUD");
		var accent = LevelGenerator.ThemeAccent;

		// ---- HUD bar background ----
		var bg = new ColorRect
		{
			Color       = new Color(0.07f, 0.07f, 0.09f, 1f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Position    = Vector2.Zero,
			Size        = new Vector2(ViewportWidth, HudHeight)
		};
		hud.AddChild(bg);

		// ---- Separator line (themed accent colour) ----
		var sep = new ColorRect
		{
			Color       = new Color(accent.R, accent.G, accent.B, 0.80f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Position    = new Vector2(0f, HudHeight - 2f),
			Size        = new Vector2(ViewportWidth, 2f)
		};
		hud.AddChild(sep);

		// ---- Per-player score labels — spread evenly across the bar ----
		_scoreLabels = new Label[PlayerCount];
		for (int i = 0; i < PlayerCount; i++)
		{
			_scoreLabels[i]          = CreateLabel(hud, fontSize: 24);
			_scoreLabels[i].Modulate = Player.ColorFor(i);
			PositionLabelTopEdge(_scoreLabels[i], playerIndex: i);
			UpdateScoreUI(i);
		}

		// ---- Timer — centred in the bar ----
		_timerLabel = CreateLabel(hud, fontSize: 34);
		_timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_timerLabel.OffsetLeft   = ArenaCenterX - 50f;
		_timerLabel.OffsetTop    =  9f;
		_timerLabel.OffsetRight  = ArenaCenterX + 50f;
		_timerLabel.OffsetBottom = HudHeight - 8f;

		// ---- Round / match outcome message — centred over game area ----
		_messageLabel = CreateLabel(hud, fontSize: 30);
		_messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_messageLabel.AutowrapMode        = TextServer.AutowrapMode.Word;
		_messageLabel.AnchorLeft    = 0.5f;
		_messageLabel.AnchorRight   = 0.5f;
		_messageLabel.AnchorTop     = 0.5f;
		_messageLabel.AnchorBottom  = 0.5f;
		_messageLabel.OffsetLeft    = -280f;
		_messageLabel.OffsetRight   =  280f;
		_messageLabel.OffsetTop     =  -60f;
		_messageLabel.OffsetBottom  =   60f;
		_messageLabel.Visible       = false;
	}

	private void PositionLabelTopEdge(Label label, int playerIndex)
	{
		const float LabelWidth = 200f;
		const float Padding    =  20f;

		float t = PlayerCount > 1 ? (float)playerIndex / (PlayerCount - 1) : 0.5f;
		float cx = Mathf.Lerp(Padding + LabelWidth / 2f,
							  ViewportWidth - Padding - LabelWidth / 2f, t);

		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.OffsetLeft   = cx - LabelWidth / 2f;
		label.OffsetTop    = 10f;
		label.OffsetRight  = cx + LabelWidth / 2f;
		label.OffsetBottom = 40f;
	}

	private static Label CreateLabel(CanvasLayer parent, int fontSize)
	{
		var l = new Label();
		l.AddThemeFontSizeOverride("font_size", fontSize);
		parent.AddChild(l);
		return l;
	}

	private void UpdateScoreUI(int playerIndex) =>
		_scoreLabels[playerIndex].Text = $"P{playerIndex + 1}   {_scores![playerIndex]}";

	// -------------------------------------------------------------------------
	// Danger zone visuals
	// -------------------------------------------------------------------------

	private void SetupDangerZones()
	{
		var hud    = GetNode<CanvasLayer>("../HUD");
		var accent = LevelGenerator.ThemeAccent;
		var dark   = LevelGenerator.ThemeDark;
		var ovCol  = new Color(dark.R, dark.G, dark.B, 0.94f);

		// Back: themed void tint filling the dead zone
		_leftOverlay  = MakeDangerRect(hud, ovCol);
		_rightOverlay = MakeDangerRect(hud, ovCol);

		// Mid: pulsing themed wall face
		_leftWall  = MakeDangerRect(hud, Colors.Transparent);
		_rightWall = MakeDangerRect(hud, Colors.Transparent);

		// Front: theme-accent edge line
		_leftEdge  = MakeDangerRect(hud, accent);
		_rightEdge = MakeDangerRect(hud, accent);

		// Hidden until shrinking begins
		SetDangerZonesVisible(false);
	}

	private void SetDangerZonesVisible(bool visible)
	{
		_leftOverlay.Visible  = visible;
		_rightOverlay.Visible = visible;
		_leftWall.Visible     = visible;
		_rightWall.Visible    = visible;
		_leftEdge.Visible     = visible;
		_rightEdge.Visible    = visible;
	}

	private static ColorRect MakeDangerRect(CanvasLayer parent, Color color)
	{
		var r = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
		parent.AddChild(r);
		return r;
	}

	private void UpdateDangerZoneVisuals(bool pulse = true)
	{
		const float Top    = -60f;
		const float Height = ViewportHeight + 120f;
		const float EdgeW  =   6f;

		SetDangerZonesVisible(true);

		if (pulse)
		{
			// Wall face pulses at 2 Hz using the level theme accent colour
			var   a = LevelGenerator.ThemeAccent;
			float p = 0.50f + 0.50f * Mathf.Sin(_phaseTimer * Mathf.Pi * 4f);
			_leftWall.Color  = new Color(a.R * p, a.G * p, a.B * p, 1f);
			_rightWall.Color = _leftWall.Color;
		}
		// When pulse=false the caller has already committed the final solid colour.

		float eleft  = EffectiveLeft;   // LeftBound  + WallFaceWidth
		float eright = EffectiveRight;  // RightBound - WallFaceWidth

		// --- Left side ---
		_leftOverlay.Position = new Vector2(-60f, Top);
		_leftOverlay.Size     = new Vector2(60f + LeftBound, Height);

		_leftWall.Position = new Vector2(LeftBound, Top);
		_leftWall.Size     = new Vector2(WallFaceWidth, Height);

		_leftEdge.Position = new Vector2(eleft - EdgeW / 2f, Top);
		_leftEdge.Size     = new Vector2(EdgeW, Height);

		// --- Right side ---
		_rightOverlay.Position = new Vector2(RightBound, Top);
		_rightOverlay.Size     = new Vector2(ViewportWidth - RightBound + 60f, Height);

		_rightWall.Position = new Vector2(eright, Top);
		_rightWall.Size     = new Vector2(WallFaceWidth, Height);

		_rightEdge.Position = new Vector2(eright - EdgeW / 2f, Top);
		_rightEdge.Size     = new Vector2(EdgeW, Height);
	}

	// -------------------------------------------------------------------------
	// Audio
	// -------------------------------------------------------------------------

	private void PlaySound(AudioStreamWav stream)
	{
		_audio.Stream = stream;
		_audio.Play();
	}

	// Rising tri-tone arpeggio — signals round start.
	private static AudioStreamWav SynthRoundStart()
	{
		const int   Rate = 8000;
		const float Dur  = 0.45f;
		int         n    = (int)(Rate * Dur);
		var         data = new byte[n];

		// Three rising square blips: 220, 330, 440 Hz, each ~0.13 s
		float[] freqs   = [220f, 330f, 440f];
		int     segLen  = n / 3;

		for (int seg = 0; seg < 3; seg++)
		{
			float freq  = freqs[seg];
			float phase = 0f;
			int   start = seg * segLen;
			int   end   = (seg == 2) ? n : start + segLen;

			for (int i = start; i < end; i++)
			{
				float t   = (float)(i - start) / segLen;
				float env = (1f - t) * Mathf.Min(1f, t * 8f); // quick attack, decay
				phase    += freq / Rate;
				float sq  = (phase % 1f) < 0.5f ? 1f : -1f;
				data[i]   = (byte)(sbyte)(sq * env * 80f);
			}
		}

		return new AudioStreamWav
		{
			Data    = data,
			Format  = AudioStreamWav.FormatEnum.Format8Bits,
			MixRate = Rate,
			Stereo  = false
		};
	}

	// Descending chromatic buzz — signals elimination.
	private static AudioStreamWav SynthEliminate()
	{
		const int   Rate = 8000;
		const float Dur  = 0.30f;
		int         n    = (int)(Rate * Dur);
		var         data = new byte[n];
		float       phase = 0f;

		for (int i = 0; i < n; i++)
		{
			float t    = (float)i / n;
			float freq = 280f - 200f * t;           // pitch falls 280 → 80 Hz
			float env  = Mathf.Exp(-t * 8f);        // smooth decay
			phase     += freq / Rate;
			float sq   = (phase % 1f) < 0.5f ? 1f : -1f;
			// Mix square with noise for a gritty crunch
			float noise = (float)((i * 1664525 + 1013904223) & 0x7FFF) / 0x7FFF * 2f - 1f;
			float s     = (sq * 0.65f + noise * 0.35f) * env;
			data[i]     = (byte)(sbyte)(s * 90f);
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
