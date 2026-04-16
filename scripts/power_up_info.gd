extends Control

## Dynamically-generated power-up reference screen.
## Reads entirely from Player.POWER_UP_INFO — adding a new entry there
## automatically adds a row here.

const PlayerScript = preload("res://scripts/player.gd")


func _ready() -> void:
	_build_ui()


func _build_ui() -> void:
	# ---- Dark background ----
	var bg := ColorRect.new()
	bg.color = Color(0.05, 0.05, 0.05, 1.0)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	# ---- Scrollable container (in case many power-ups overflow) ----
	var scroll := ScrollContainer.new()
	scroll.set_anchors_preset(Control.PRESET_FULL_RECT)
	scroll.offset_left   = 80
	scroll.offset_right  = -80
	scroll.offset_top    = 40
	scroll.offset_bottom = -40
	add_child(scroll)

	var vbox := VBoxContainer.new()
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_theme_constant_override("separation", 6)
	scroll.add_child(vbox)

	# ---- Title ----
	var title := Label.new()
	title.text = "POWER-UPS"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 32)
	title.add_theme_color_override("font_color", Color(0.85, 0.85, 0.85))
	vbox.add_child(title)

	# ---- Subtitle ----
	var subtitle := Label.new()
	subtitle.text = "Granted every 10 seconds during a round. All stack and persist until round end."
	subtitle.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	subtitle.add_theme_font_size_override("font_size", 14)
	subtitle.add_theme_color_override("font_color", Color(0.50, 0.50, 0.55))
	subtitle.autowrap_mode = TextServer.AUTOWRAP_WORD
	vbox.add_child(subtitle)

	_add_spacer(vbox, 16)

	# ---- One card per power-up, iterated from the registry ----
	for type in PlayerScript.PWR_ORDER:
		if not PlayerScript.POWER_UP_INFO.has(type):
			continue
		var info: Dictionary = PlayerScript.POWER_UP_INFO[type]
		_add_power_up_card(vbox, info)

	_add_spacer(vbox, 20)

	# ---- Back button ----
	var back := Button.new()
	back.text = "Back"
	back.custom_minimum_size = Vector2(160, 40)
	back.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	back.pressed.connect(_on_back_pressed)
	vbox.add_child(back)


func _add_power_up_card(parent: VBoxContainer, info: Dictionary) -> void:
	var card := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.10, 0.10, 0.13, 1.0)
	style.corner_radius_top_left     = 6
	style.corner_radius_top_right    = 6
	style.corner_radius_bottom_left  = 6
	style.corner_radius_bottom_right = 6
	style.content_margin_left   = 16
	style.content_margin_right  = 16
	style.content_margin_top    = 12
	style.content_margin_bottom = 12
	card.add_theme_stylebox_override("panel", style)
	parent.add_child(card)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 16)
	card.add_child(hbox)

	# Diamond indicator (matches in-game indicator)
	var diamond_container := Control.new()
	diamond_container.custom_minimum_size = Vector2(28, 28)
	hbox.add_child(diamond_container)

	var diamond := Polygon2D.new()
	diamond.polygon = PackedVector2Array([
		Vector2(14, 2), Vector2(26, 14), Vector2(14, 26), Vector2(2, 14)])
	diamond.color = info["color"]
	diamond_container.add_child(diamond)

	# Text column
	var text_vbox := VBoxContainer.new()
	text_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text_vbox.add_theme_constant_override("separation", 2)
	hbox.add_child(text_vbox)

	# Name + HUD code
	var name_label := Label.new()
	name_label.text = "%s  [%s]" % [info["name"], info["code"]]
	name_label.add_theme_font_size_override("font_size", 20)
	name_label.add_theme_color_override("font_color", info["color"])
	text_vbox.add_child(name_label)

	# Description
	var desc_label := Label.new()
	desc_label.text = info["desc"]
	desc_label.add_theme_font_size_override("font_size", 14)
	desc_label.add_theme_color_override("font_color", Color(0.70, 0.70, 0.75))
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD
	text_vbox.add_child(desc_label)


func _add_spacer(parent: VBoxContainer, height: float) -> void:
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, height)
	parent.add_child(spacer)


func _on_back_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/MainMenu.tscn")
