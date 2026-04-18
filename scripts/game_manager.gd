extends Node

@export var player_count: int = 2

# --- Round timing (seconds) ---
const PLAY_DURATION: float   = 30.0
const SHRINK_DURATION: float = 30.0
const ROUND_END_PAUSE: float =  3.0

# --- Arena geometry ---
const VIEWPORT_WIDTH: float  = 1280.0
const VIEWPORT_HEIGHT: float =  720.0
const ARENA_CENTER_X: float  =  640.0
const MIN_ARENA_WIDTH: float =  350.0
const WALL_FACE_WIDTH: float =   18.0
const STATIC_BOUNDARY_INSET: float = WALL_FACE_WIDTH
const HUD_HEIGHT: float = 54.0

# --- Match scoring ---
const WIN_SCORE: int = 5

# Static fields survive ReloadCurrentScene so scores persist across rounds.
static var _scores: Array[int] = []
static var _scored_player_count: int = 0

# --- Phase state machine ---
enum Phase { PLAYING, SHRINKING, ROUND_END, MATCH_END }
var _phase: int      = Phase.PLAYING
var _phase_timer: float = 0.0
var _end_timer: float   = 0.0
var _shrink_locked: bool = false

# --- Live arena bounds ---
var left_bound: float    = 0.0
var right_bound: float   = VIEWPORT_WIDTH
var is_shrinking: bool:
	get: return _phase == Phase.SHRINKING

var effective_left: float:
	get: return left_bound + WALL_FACE_WIDTH
var effective_right: float:
	get: return right_bound - WALL_FACE_WIDTH

# --- HUD nodes ---
var _score_labels: Array[Label]    = []
var _timer_label: Label
var _message_label: Label
var _power_up_labels: Array[Label] = []

# --- Power-up grant state ---
const POWER_UP_GRANT_TIMES: Array[float] = [10.0, 20.0, 30.0, 40.0, 50.0]
var _power_up_grant_index: int = 0
var _global_timer: float       = 0.0

# --- Danger zone visuals ---
var _left_overlay: ColorRect
var _right_overlay: ColorRect
var _left_wall: ColorRect
var _right_wall: ColorRect
var _left_edge: ColorRect
var _right_edge: ColorRect

# --- Audio ---
var _audio: AudioStreamPlayer
static var _snd_round_start: AudioStreamWAV
static var _snd_eliminate: AudioStreamWAV

# Preload player script for enum/static access
const PlayerScript = preload("res://scripts/player.gd")

# Theme colours — cached from LevelGenerator in _ready() to avoid cyclic preload
var _theme_accent: Color
var _theme_dark: Color


func _ready() -> void:
	add_to_group("game_manager")

	# Cache theme colours from LevelGenerator (avoids cyclic preload)
	var lg_script = load("res://scripts/level_generator.gd")
	_theme_accent = lg_script.theme_accent
	_theme_dark   = lg_script.theme_dark

	if _scores.size() == 0 or _scored_player_count != player_count:
		_scores = []
		_scores.resize(player_count)
		_scores.fill(0)
		_scored_player_count = player_count

	left_bound  = 0.0
	right_bound = VIEWPORT_WIDTH

	_setup_danger_zones()
	_setup_hud()

	if _snd_round_start == null:
		_snd_round_start = _synth_round_start()
	if _snd_eliminate == null:
		_snd_eliminate = _synth_eliminate()
	_audio = AudioStreamPlayer.new()
	_audio.volume_db = -4.0
	add_child(_audio)

	_play_sound(_snd_round_start)

	# Pause menu — lives as a child, handles its own ESC input
	var pause_menu_script = preload("res://scripts/pause_menu.gd")
	var pause_menu := CanvasLayer.new()
	pause_menu.set_script(pause_menu_script)
	add_child(pause_menu)


func _exit_tree() -> void:
	pass  # group membership auto-removed


# =========================================================================
# Public API (called by Player)
# =========================================================================

func on_player_eliminated(victim_index: int, _killer_index: int) -> void:
	if _phase == Phase.ROUND_END or _phase == Phase.MATCH_END:
		return

	_play_sound(_snd_eliminate)

	var survivors: Array = []
	for node in get_tree().get_nodes_in_group("players"):
		if node is CharacterBody2D and not node.is_eliminated:
			survivors.append(node)

	if survivors.size() == 1:
		_handle_round_win(survivors[0].player_index)
	elif survivors.size() == 0:
		_start_round_end("Draw!")


func on_power_up_changed(player_index: int, display: String) -> void:
	if player_index >= _power_up_labels.size():
		return
	_power_up_labels[player_index].text     = display
	_power_up_labels[player_index].modulate = Color.WHITE


# =========================================================================
# Update loop
# =========================================================================

func _process(delta: float) -> void:
	if _phase == Phase.PLAYING or _phase == Phase.SHRINKING:
		_global_timer += delta
		_check_power_up_grants()

	match _phase:
		Phase.PLAYING:   _update_playing(delta)
		Phase.SHRINKING: _update_shrinking(delta)
		Phase.ROUND_END: _update_round_end(delta)
		Phase.MATCH_END: _update_match_end()


func _check_power_up_grants() -> void:
	if _power_up_grant_index >= POWER_UP_GRANT_TIMES.size():
		return
	if _global_timer < POWER_UP_GRANT_TIMES[_power_up_grant_index]:
		return
	_power_up_grant_index += 1
	_grant_random_power_ups()


func _grant_random_power_ups() -> void:
	var types: Array[int] = [
		PlayerScript.PowerUpType.SPEED, PlayerScript.PowerUpType.HIGH_JUMP,
		PlayerScript.PowerUpType.FLOAT, PlayerScript.PowerUpType.SHIELD,
		PlayerScript.PowerUpType.GROUND_POUND, PlayerScript.PowerUpType.TINY]

	for node in get_tree().get_nodes_in_group("players"):
		if not (node is CharacterBody2D) or node.is_eliminated:
			continue
		# Pick a type the player doesn't already have
		var available: Array[int] = []
		for t in types:
			if not node.has_power_up(t):
				available.append(t)
		var pool: Array[int] = available if available.size() > 0 else types
		node.activate_power_up(pool[randi_range(0, pool.size() - 1)])


func _update_playing(delta: float) -> void:
	_phase_timer += delta
	var remaining: float = PLAY_DURATION - _phase_timer
	_timer_label.text     = str(maxi(1, ceili(remaining)))
	_timer_label.modulate = Color.WHITE

	if _phase_timer >= PLAY_DURATION:
		_start_shrinking()


func _update_shrinking(delta: float) -> void:
	_phase_timer += delta

	var t: float = clampf(_phase_timer / SHRINK_DURATION, 0.0, 1.0)
	var half: float = lerpf(VIEWPORT_WIDTH / 2.0, MIN_ARENA_WIDTH / 2.0, t)
	left_bound  = ARENA_CENTER_X - half
	right_bound = ARENA_CENTER_X + half

	if _phase_timer >= SHRINK_DURATION:
		if not _shrink_locked:
			_shrink_locked = true
			var accent: Color = _theme_accent
			_left_wall.color  = accent
			_right_wall.color = accent
			_update_danger_zone_visuals(false)
		_timer_label.visible = false
		return

	_update_danger_zone_visuals(true)

	var remaining: float = SHRINK_DURATION - _phase_timer
	_timer_label.text     = str(ceili(remaining))
	var flash_on: bool    = fmod(_phase_timer, 0.5) < 0.25
	_timer_label.modulate = Color.WHITE if flash_on else Color(0.45, 0.45, 0.45, 1.0)


func _update_round_end(delta: float) -> void:
	_end_timer -= delta
	if _end_timer <= 0.0:
		get_tree().reload_current_scene()


func _update_match_end() -> void:
	for i in range(player_count):
		if Input.is_action_just_pressed("p%d_jump" % (i + 1)):
			_scores = []
			PlayerScript.reset_assignment_seed()
			get_tree().reload_current_scene()
			return


# =========================================================================
# Phase transitions
# =========================================================================

func _start_shrinking() -> void:
	_phase = Phase.SHRINKING
	_phase_timer = 0.0


func _handle_round_win(winner_index: int) -> void:
	_scores[winner_index] += 1
	_update_score_ui(winner_index)

	if _scores[winner_index] >= WIN_SCORE:
		_start_match_end(winner_index)
	else:
		_start_round_end("P%d scores!  (%d/%d)" % [winner_index + 1, _scores[winner_index], WIN_SCORE])


func _start_round_end(message: String) -> void:
	_phase = Phase.ROUND_END
	_end_timer = ROUND_END_PAUSE
	_message_label.text    = message
	_message_label.visible = true
	_timer_label.visible   = false


func _start_match_end(winner_index: int) -> void:
	_phase = Phase.MATCH_END
	_message_label.text    = "P%d wins the match!\n\nPress jump to play again" % (winner_index + 1)
	_message_label.visible = true
	_timer_label.visible   = false


# =========================================================================
# HUD setup
# =========================================================================

func _setup_hud() -> void:
	var hud: CanvasLayer = get_node("../HUD")
	var accent: Color = _theme_accent

	# HUD bar background
	var bg := ColorRect.new()
	bg.color        = Color(0.07, 0.07, 0.09, 1.0)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bg.position     = Vector2.ZERO
	bg.size         = Vector2(VIEWPORT_WIDTH, HUD_HEIGHT)
	hud.add_child(bg)

	# Separator line
	var sep := ColorRect.new()
	sep.color        = Color(accent.r, accent.g, accent.b, 0.80)
	sep.mouse_filter = Control.MOUSE_FILTER_IGNORE
	sep.position     = Vector2(0, HUD_HEIGHT - 2)
	sep.size         = Vector2(VIEWPORT_WIDTH, 2)
	hud.add_child(sep)

	# Per-player score labels
	_score_labels = []
	for i in range(player_count):
		var lbl := _create_label(hud, 24)
		lbl.modulate = PlayerScript.color_for(i)
		_position_label_top_edge(lbl, i)
		_score_labels.append(lbl)
		_update_score_ui(i)

	# Per-player power-up indicator labels
	_power_up_labels = []
	for i in range(player_count):
		var lbl := _create_label(hud, 13)
		lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		lbl.text = ""
		_position_power_up_label(lbl, i)
		_power_up_labels.append(lbl)

	# Timer
	_timer_label = _create_label(hud, 34)
	_timer_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_timer_label.offset_left   = ARENA_CENTER_X - 50
	_timer_label.offset_top    = 9
	_timer_label.offset_right  = ARENA_CENTER_X + 50
	_timer_label.offset_bottom = HUD_HEIGHT - 8

	# Message label
	_message_label = _create_label(hud, 30)
	_message_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_message_label.autowrap_mode   = TextServer.AUTOWRAP_WORD
	_message_label.anchor_left     = 0.5
	_message_label.anchor_right    = 0.5
	_message_label.anchor_top      = 0.5
	_message_label.anchor_bottom   = 0.5
	_message_label.offset_left     = -280
	_message_label.offset_right    =  280
	_message_label.offset_top      =  -60
	_message_label.offset_bottom   =   60
	_message_label.visible         = false


func _position_label_top_edge(label: Label, player_index: int) -> void:
	const LABEL_WIDTH: float = 200.0
	const PADDING: float     =  20.0
	var t: float = float(player_index) / float(player_count - 1) if player_count > 1 else 0.5
	var cx: float = lerpf(PADDING + LABEL_WIDTH / 2.0, VIEWPORT_WIDTH - PADDING - LABEL_WIDTH / 2.0, t)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.offset_left   = cx - LABEL_WIDTH / 2.0
	label.offset_top    = 10
	label.offset_right  = cx + LABEL_WIDTH / 2.0
	label.offset_bottom = 40


func _position_power_up_label(label: Label, player_index: int) -> void:
	const LABEL_WIDTH: float = 200.0
	const PADDING: float     =  20.0
	var t: float = float(player_index) / float(player_count - 1) if player_count > 1 else 0.5
	var cx: float = lerpf(PADDING + LABEL_WIDTH / 2.0, VIEWPORT_WIDTH - PADDING - LABEL_WIDTH / 2.0, t)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.offset_left   = cx - LABEL_WIDTH / 2.0
	label.offset_top    = 38
	label.offset_right  = cx + LABEL_WIDTH / 2.0
	label.offset_bottom = 52


func _create_label(parent: CanvasLayer, font_size: int) -> Label:
	var l := Label.new()
	l.add_theme_font_size_override("font_size", font_size)
	parent.add_child(l)
	return l


func _update_score_ui(player_index: int) -> void:
	_score_labels[player_index].text = "P%d   %d" % [player_index + 1, _scores[player_index]]


# =========================================================================
# Danger zone visuals
# =========================================================================

func _setup_danger_zones() -> void:
	var hud: CanvasLayer = get_node("../HUD")
	var accent: Color = _theme_accent
	var dark: Color   = _theme_dark
	var ov_col := Color(dark.r, dark.g, dark.b, 0.94)

	_left_overlay  = _make_danger_rect(hud, ov_col)
	_right_overlay = _make_danger_rect(hud, ov_col)
	_left_wall     = _make_danger_rect(hud, Color.TRANSPARENT)
	_right_wall    = _make_danger_rect(hud, Color.TRANSPARENT)
	_left_edge     = _make_danger_rect(hud, accent)
	_right_edge    = _make_danger_rect(hud, accent)
	_set_danger_zones_visible(false)


func _set_danger_zones_visible(vis: bool) -> void:
	_left_overlay.visible  = vis
	_right_overlay.visible = vis
	_left_wall.visible     = vis
	_right_wall.visible    = vis
	_left_edge.visible     = vis
	_right_edge.visible    = vis


func _make_danger_rect(parent: CanvasLayer, color: Color) -> ColorRect:
	var r := ColorRect.new()
	r.color        = color
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(r)
	return r


func _update_danger_zone_visuals(pulse: bool) -> void:
	const TOP: float    = -60.0
	const HEIGHT: float = VIEWPORT_HEIGHT + 120.0
	const EDGE_W: float = 6.0

	_set_danger_zones_visible(true)

	if pulse:
		var a: Color = _theme_accent
		var p: float = 0.50 + 0.50 * sin(_phase_timer * PI * 4.0)
		_left_wall.color  = Color(a.r * p, a.g * p, a.b * p, 1.0)
		_right_wall.color = _left_wall.color

	var eleft: float  = effective_left
	var eright: float = effective_right

	_left_overlay.position = Vector2(-60, TOP)
	_left_overlay.size     = Vector2(60 + left_bound, HEIGHT)
	_left_wall.position    = Vector2(left_bound, TOP)
	_left_wall.size        = Vector2(WALL_FACE_WIDTH, HEIGHT)
	_left_edge.position    = Vector2(eleft - EDGE_W / 2.0, TOP)
	_left_edge.size        = Vector2(EDGE_W, HEIGHT)

	_right_overlay.position = Vector2(right_bound, TOP)
	_right_overlay.size     = Vector2(VIEWPORT_WIDTH - right_bound + 60, HEIGHT)
	_right_wall.position    = Vector2(eright, TOP)
	_right_wall.size        = Vector2(WALL_FACE_WIDTH, HEIGHT)
	_right_edge.position    = Vector2(eright - EDGE_W / 2.0, TOP)
	_right_edge.size        = Vector2(EDGE_W, HEIGHT)


# =========================================================================
# Audio
# =========================================================================

func _play_sound(stream: AudioStreamWAV) -> void:
	_audio.stream = stream
	_audio.play()


static func _synth_round_start() -> AudioStreamWAV:
	const RATE: int = 8000
	const DUR: float = 0.45
	var n: int = int(RATE * DUR)
	var data := PackedByteArray()
	data.resize(n)

	var freqs: Array[float] = [220.0, 330.0, 440.0]
	var seg_len: int = n / 3

	for seg in range(3):
		var freq: float = freqs[seg]
		var phase: float = 0.0
		var start: int = seg * seg_len
		var end: int = n if seg == 2 else start + seg_len

		for i in range(start, end):
			var t: float = float(i - start) / seg_len
			var env: float = (1.0 - t) * minf(1.0, t * 8.0)
			phase += freq / RATE
			var sq: float = 1.0 if fmod(phase, 1.0) < 0.5 else -1.0
			data[i] = int(sq * env * 80.0) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.data     = data
	wav.format   = AudioStreamWAV.FORMAT_8_BITS
	wav.mix_rate = RATE
	wav.stereo   = false
	return wav


static func _synth_eliminate() -> AudioStreamWAV:
	const RATE: int = 8000
	const DUR: float = 0.30
	var n: int = int(RATE * DUR)
	var data := PackedByteArray()
	data.resize(n)
	var phase: float = 0.0

	for i in range(n):
		var t: float = float(i) / n
		var freq: float = 280.0 - 200.0 * t
		var env: float  = exp(-t * 8.0)
		phase += freq / RATE
		var sq: float = 1.0 if fmod(phase, 1.0) < 0.5 else -1.0
		var noise: float = float((i * 1664525 + 1013904223) & 0x7FFF) / 0x7FFF * 2.0 - 1.0
		var s: float = (sq * 0.65 + noise * 0.35) * env
		data[i] = int(s * 90.0) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.data     = data
	wav.format   = AudioStreamWAV.FORMAT_8_BITS
	wav.mix_rate = RATE
	wav.stereo   = false
	return wav
