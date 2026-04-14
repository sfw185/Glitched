using Godot;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GetNode<Button>("VBox/PlayButton").Pressed += OnPlayPressed;
        GetNode<Button>("VBox/ExitButton").Pressed += OnExitPressed;
    }

    private void OnPlayPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/Level.tscn");
    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }
}
