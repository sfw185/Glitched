extends Control


func _ready() -> void:
	$VBox/PlayButton.pressed.connect(_on_play_pressed)
	$VBox/PowerUpsButton.pressed.connect(_on_power_ups_pressed)
	$VBox/ExitButton.pressed.connect(_on_exit_pressed)


func _on_play_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/Level.tscn")


func _on_power_ups_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/PowerUpInfo.tscn")


func _on_exit_pressed() -> void:
	get_tree().quit()
