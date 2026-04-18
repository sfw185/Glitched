extends CanvasLayer

## In-game pause menu — ESC to toggle.
## Builds its own UI at runtime so there's no separate .tscn to maintain.
## process_mode = ALWAYS so input works while the tree is paused.

var _panel: Control


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	layer = 100  # render above everything
	_build_ui()
	hide_menu()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("pause"):
		if _panel.visible:
			hide_menu()
		else:
			show_menu()
		get_viewport().set_input_as_handled()


func show_menu() -> void:
	_panel.visible = true
	get_tree().paused = true


func hide_menu() -> void:
	_panel.visible = false
	get_tree().paused = false


# =========================================================================
# UI construction
# =========================================================================

func _build_ui() -> void:
	_panel = Control.new()
	_panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_panel)

	# Semi-transparent overlay
	var overlay := ColorRect.new()
	overlay.color = Color(0.0, 0.0, 0.0, 0.70)
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.mouse_filter = Control.MOUSE_FILTER_STOP  # block clicks through
	_panel.add_child(overlay)

	# Centred content box
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	_panel.add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 10)
	center.add_child(vbox)

	# ---- Title ----
	_add_label(vbox, "PAUSED", 36, Color(0.85, 0.85, 0.85), HORIZONTAL_ALIGNMENT_CENTER)
	_add_spacer(vbox, 8)

	# ---- Controls section ----
	_add_label(vbox, "CONTROLS", 20, Color(0.55, 0.80, 1.00), HORIZONTAL_ALIGNMENT_CENTER)
	_add_spacer(vbox, 2)

	var controls_grid := GridContainer.new()
	controls_grid.columns = 2
	controls_grid.add_theme_constant_override("h_separation", 40)
	controls_grid.add_theme_constant_override("v_separation", 4)
	vbox.add_child(controls_grid)

	# Player 1
	_add_label(controls_grid, "Player 1", 16, Color(0.60, 0.60, 0.65))
	_add_label(controls_grid, "Player 2", 16, Color(0.60, 0.60, 0.65))

	_add_label(controls_grid, "A / D   Move", 14, Color(0.80, 0.80, 0.85))
	_add_label(controls_grid, "Arrow Left / Right   Move", 14, Color(0.80, 0.80, 0.85))

	_add_label(controls_grid, "W  or  Space   Jump", 14, Color(0.80, 0.80, 0.85))
	_add_label(controls_grid, "Arrow Up   Jump", 14, Color(0.80, 0.80, 0.85))

	_add_label(controls_grid, "S   Fast-fall / Ground Pound", 14, Color(0.80, 0.80, 0.85))
	_add_label(controls_grid, "Arrow Down   Fast-fall / Ground Pound", 14, Color(0.80, 0.80, 0.85))

	_add_spacer(vbox, 6)

	# ---- Tips section ----
	_add_label(vbox, "HOW TO PLAY", 20, Color(0.55, 0.80, 1.00), HORIZONTAL_ALIGNMENT_CENTER)
	_add_spacer(vbox, 2)

	var tips: Array[String] = [
		"Land on your opponent's head to eliminate them.",
		"Double-jump: press jump again in mid-air.",
		"After 30s the arena starts shrinking — stay inside!",
		"Power-ups are granted every 10s and stack permanently.",
		"Shield absorbs one hit. Ground Pound slams down with the down key.",
		"First to 5 round wins takes the match.",
	]
	for tip in tips:
		_add_label(vbox, tip, 14, Color(0.65, 0.65, 0.70), HORIZONTAL_ALIGNMENT_CENTER)

	_add_spacer(vbox, 10)

	# ---- Buttons ----
	var btn_row := HBoxContainer.new()
	btn_row.alignment = BoxContainer.ALIGNMENT_CENTER
	btn_row.add_theme_constant_override("separation", 20)
	vbox.add_child(btn_row)

	var resume_btn := Button.new()
	resume_btn.text = "Resume"
	resume_btn.custom_minimum_size = Vector2(140, 40)
	resume_btn.pressed.connect(hide_menu)
	btn_row.add_child(resume_btn)

	var quit_btn := Button.new()
	quit_btn.text = "Quit to Menu"
	quit_btn.custom_minimum_size = Vector2(140, 40)
	quit_btn.pressed.connect(_on_quit_pressed)
	btn_row.add_child(quit_btn)

	_add_spacer(vbox, 4)
	_add_label(vbox, "ESC to resume", 13, Color(0.40, 0.40, 0.45), HORIZONTAL_ALIGNMENT_CENTER)


func _on_quit_pressed() -> void:
	get_tree().paused = false
	get_tree().change_scene_to_file("res://scenes/MainMenu.tscn")


func _add_label(parent: Node, text: String, size: int, color: Color,
		align: int = HORIZONTAL_ALIGNMENT_LEFT) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.horizontal_alignment = align
	parent.add_child(l)
	return l


func _add_spacer(parent: Node, height: float) -> void:
	var s := Control.new()
	s.custom_minimum_size = Vector2(0, height)
	parent.add_child(s)
