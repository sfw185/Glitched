using Godot;

public partial class GameManager : Node
{
    public static GameManager? Instance { get; private set; }

    [Export] public int PlayerCount = 2;

    private const int StartingLives = 3;

    private int[] _lives = [];
    private Label[] _livesLabels = [];
    private Label _winLabel = null!;
    private bool _gameOver = false;

    public override void _Ready()
    {
        Instance = this;
        _lives = new int[PlayerCount];
        _livesLabels = new Label[PlayerCount];

        var hud = GetNode<CanvasLayer>("../HUD");
        _winLabel = hud.GetNode<Label>("WinLabel");

        for (int i = 0; i < PlayerCount; i++)
        {
            _lives[i] = StartingLives;
            _livesLabels[i] = CreateLivesLabel(i, hud);
            UpdateLivesUI(i, StartingLives);
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void OnPlayerHit(int playerIndex, int remainingLives)
    {
        _lives[playerIndex] = remainingLives;
        UpdateLivesUI(playerIndex, remainingLives);
    }

    public void OnPlayerEliminated(int playerIndex)
    {
        _lives[playerIndex] = 0;
        UpdateLivesUI(playerIndex, 0);

        // Count survivors
        int survivorIndex = -1;
        int survivors = 0;
        for (int i = 0; i < PlayerCount; i++)
        {
            if (i != playerIndex && _lives[i] > 0)
            {
                survivorIndex = i;
                survivors++;
            }
        }

        if (survivors == 1)
            ShowWinner(survivorIndex + 1); // 1-indexed display
    }

    public override void _Process(double delta)
    {
        if (!_gameOver) return;

        for (int i = 0; i < PlayerCount; i++)
        {
            if (Input.IsActionJustPressed($"p{i + 1}_jump"))
            {
                GetTree().ReloadCurrentScene();
                return;
            }
        }
    }

    // --- Private helpers ---

    private Label CreateLivesLabel(int playerIndex, CanvasLayer hud)
    {
        const float LabelWidth = 220f;
        const float ViewportWidth = 1280f;
        const float Padding = 20f;

        float t = PlayerCount > 1 ? (float)playerIndex / (PlayerCount - 1) : 0.5f;
        float centerX = Mathf.Lerp(Padding + LabelWidth / 2f,
                                    ViewportWidth - Padding - LabelWidth / 2f, t);

        var label = new Label
        {
            OffsetLeft   = centerX - LabelWidth / 2f,
            OffsetTop    = 16f,
            OffsetRight  = centerX + LabelWidth / 2f,
            OffsetBottom = 48f,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 22);

        hud.AddChild(label);
        return label;
    }

    private void UpdateLivesUI(int playerIndex, int lives)
    {
        string pips = new string('●', lives) + new string('○', StartingLives - lives);
        _livesLabels[playerIndex].Text = $"P{playerIndex + 1}  {pips}";
    }

    private void ShowWinner(int winnerDisplay)
    {
        _gameOver = true;
        _winLabel.Text = $"Player {winnerDisplay} Wins!\n\nPress jump to play again";
        _winLabel.Visible = true;
    }
}
