extends CharacterBody2D

enum PowerUpType { NONE, SPEED, HIGH_JUMP, FLOAT, SHIELD, GROUND_POUND, TINY }

# ---- Power-up gameplay constants (referenced by descriptions below) ----
const PWR_SPEED_MULT: float       = 1.7
const PWR_JUMP_MULT: float        = 1.6
const PWR_GRAVITY_MULT: float     = 0.3
const PWR_SLAM_SPEED: float       = 1100.0
const PWR_SLAM_KILL_RANGE: float  = 30.0
const PWR_SLAM_PUSH_RANGE: float  = 100.0
const PWR_TINY_SCALE: float       = 0.7

## Central registry for all power-ups.  The info screen, HUD codes,
## and indicator colours all read from this — adding a new entry here
## automatically updates every consumer.
static var POWER_UP_INFO: Dictionary = {
	PowerUpType.SPEED: {
		"name": "Speed",
		"code": "SPD",
		"color": Color(1.00, 1.00, 0.00),
		"desc": "Move %.0f%% faster." % ((PWR_SPEED_MULT - 1.0) * 100.0),
	},
	PowerUpType.HIGH_JUMP: {
		"name": "High Jump",
		"code": "HJP",
		"color": Color(0.10, 1.00, 0.30),
		"desc": "Jump %.0f%% higher." % ((PWR_JUMP_MULT - 1.0) * 100.0),
	},
	PowerUpType.FLOAT: {
		"name": "Float",
		"code": "FLT",
		"color": Color(0.28, 0.95, 1.00),
		"desc": "Gravity reduced to %.0f%%." % (PWR_GRAVITY_MULT * 100.0),
	},
	PowerUpType.SHIELD: {
		"name": "Shield",
		"code": "SLD",
		"color": Color.WHITE,
		"desc": "Absorbs one hit, then breaks.",
	},
	PowerUpType.GROUND_POUND: {
		"name": "Ground Pound",
		"code": "GND",
		"color": Color(1.00, 0.10, 0.10),
		"desc": "Press down in mid-air to slam at %.0f px/s. Eliminates players within %.0f px, pushes within %.0f px." \
			% [PWR_SLAM_SPEED, PWR_SLAM_KILL_RANGE, PWR_SLAM_PUSH_RANGE],
	},
	PowerUpType.TINY: {
		"name": "Tiny",
		"code": "TNY",
		"color": Color(0.80, 0.00, 1.00),
		"desc": "Shrink to %.0f%% size — harder to hit." % (PWR_TINY_SCALE * 100.0),
	},
}

@export var player_index: int  = 0
@export var speed: float       = 200.0
@export var jump_velocity: float = -450.0

var is_eliminated: bool = false

# ---- Sci-fi neon colour palette ----
const PALETTE: Array[Color] = [
	Color(0.00, 0.85, 1.00),  # cyan
	Color(1.00, 0.45, 0.00),  # orange
	Color(0.80, 0.00, 1.00),  # violet
	Color(0.10, 1.00, 0.30),  # lime
	Color(1.00, 0.10, 0.25),  # red
	Color(1.00, 0.88, 0.00),  # gold
	Color(0.10, 0.35, 1.00),  # blue
	Color(1.00, 0.30, 0.75),  # pink
]

# Shuffled once per match — stable across rounds, reshuffled at match restart.
static var _color_assignment: Array[int] = [0, 1, 2, 3, 4, 5, 6, 7]
static var _shape_assignment: Array[int] = [0, 1, 2, 3]
static var _pattern_choice: int = 0
static var _assignments_seeded: bool = false

# Static audio caches — survive scene reloads
static var _snd_jumps: Array       = []
static var _snd_double_jumps: Array = []
static var _snd_stomp: AudioStreamWAV
static var _snd_power_up: AudioStreamWAV
static var _snd_squish: AudioStreamWAV

const MAX_PLAYERS: int = 8
const PITCH_MULT: Array[float] = [1.000, 1.122, 0.891, 1.260, 0.794, 1.059, 0.891, 1.189]


static func color_for(index: int) -> Color:
	return PALETTE[_color_assignment[index % _color_assignment.size()]]


static func reset_assignment_seed() -> void:
	_assignments_seeded = false


static func _fisher_yates(arr: Array) -> void:
	for i in range(arr.size() - 1, 0, -1):
		var j: int = randi_range(0, i)
		var tmp = arr[i]
		arr[i] = arr[j]
		arr[j] = tmp


# ---- Physics ----
const GRAVITY: float         = 980.0
const SPAWN_PROTECTION: float = 1.5
const PLAYER_HALF_WIDTH: float = 14.0

var _spawn_timer: float    = SPAWN_PROTECTION
var _can_double_jump: bool = false
var _prefix: String        = "p1"

# ---- Body visuals ----
var _body_root: Node2D
var _stride_phase: float  = 0.0

# ---- Animated skin pattern ----
var _pattern_time: float     = 0.0
var _pattern_polys: Array[Polygon2D] = []
var _pattern_buffers: Array[PackedVector2Array] = []  # pre-allocated polygon buffers

# ---- Robot legs (wheels) ----
var _left_thigh: Polygon2D
var _left_shin: Polygon2D
var _right_thigh: Polygon2D
var _right_shin: Polygon2D
var _left_hip: Vector2
var _right_hip: Vector2

# ---- Power-up indicators ----
var _pwr_indicators: Array[Polygon2D] = []
var _shield_visual: Polygon2D
const PWR_ORDER: Array[int] = [
	PowerUpType.SPEED, PowerUpType.HIGH_JUMP, PowerUpType.FLOAT,
	PowerUpType.SHIELD, PowerUpType.GROUND_POUND, PowerUpType.TINY]

# ---- Power-up state ----
var _pwr_speed: bool        = false
var _pwr_high_jump: bool    = false
var _pwr_float: bool        = false
var _pwr_shield: bool       = false
var _pwr_ground_pound: bool = false
var _pwr_tiny: bool         = false
var _ground_pound_armed: bool = false

# Death animation
const DEATH_DURATION: float = 0.55
var _death_timer: float = 0.0

# Tiny resize
var _original_shape_size: Vector2
var _collision_shape: CollisionShape2D

# ---- Audio ----
var _audio: AudioStreamPlayer


func _ready() -> void:
	$Visual.polygon = PackedVector2Array()

	if player_index == 0 and not _assignments_seeded:
		_fisher_yates(_color_assignment)
		_fisher_yates(_shape_assignment)
		_pattern_choice = randi_range(0, 3)
		_assignments_seeded = true

	_prefix = "p%d" % (player_index + 1)
	add_to_group("players")

	_collision_shape = $CollisionShape2D as CollisionShape2D
	_original_shape_size = (_collision_shape.shape as RectangleShape2D).size

	_body_root = Node2D.new()
	add_child(_body_root)

	# Create leg Polygon2D nodes first so they render behind the body
	_left_thigh  = Polygon2D.new(); _body_root.add_child(_left_thigh)
	_left_shin   = Polygon2D.new(); _body_root.add_child(_left_shin)
	_right_thigh = Polygon2D.new(); _body_root.add_child(_right_thigh)
	_right_shin  = Polygon2D.new(); _body_root.add_child(_right_shin)

	_build_body(color_for(player_index))

	# Wheels: circle ring + rotating spoke per side
	const WHEEL_R: float = 6.0
	var wheel_poly := _circle_poly(WHEEL_R, 14)
	var spoke_poly := PackedVector2Array([
		Vector2(-WHEEL_R + 1, -1.5), Vector2(WHEEL_R - 1, -1.5),
		Vector2(WHEEL_R - 1, 1.5),   Vector2(-WHEEL_R + 1, 1.5)])
	var spoke_color := color_for(player_index).lerp(Color.WHITE, 0.55)

	_left_thigh.polygon  = wheel_poly;  _left_thigh.position  = _left_hip
	_right_thigh.polygon = wheel_poly;  _right_thigh.position = _right_hip

	_left_shin.polygon  = spoke_poly;  _left_shin.position  = _left_hip;  _left_shin.color  = spoke_color
	_right_shin.polygon = spoke_poly;  _right_shin.position = _right_hip; _right_shin.color = spoke_color

	_setup_pattern(color_for(player_index))

	# Power-up indicator diamonds
	_pwr_indicators = []
	for i in range(PWR_ORDER.size()):
		var ind := Polygon2D.new()
		ind.polygon = PackedVector2Array([
			Vector2(0, -6), Vector2(5, 0), Vector2(0, 6), Vector2(-5, 0)])
		ind.color    = _power_indicator_color(PWR_ORDER[i])
		ind.position = Vector2(0, -38)
		ind.visible  = false
		_pwr_indicators.append(ind)
		_body_root.add_child(ind)

	# Shield bubble
	_shield_visual = Polygon2D.new()
	_shield_visual.polygon  = _shield_poly(17.0, 27.0, 8)
	_shield_visual.color    = Color(0.55, 0.85, 1.00, 0.0)
	_shield_visual.visible  = false
	_shield_visual.position = Vector2(0, -2)
	_body_root.add_child(_shield_visual)

	# Audio — initialise static caches if needed
	if _snd_jumps.size() == 0:
		_snd_jumps.resize(MAX_PLAYERS)
		_snd_double_jumps.resize(MAX_PLAYERS)

	var pi: int = player_index
	var pm: float = PITCH_MULT[pi % PITCH_MULT.size()]
	if _snd_jumps[pi] == null:
		_snd_jumps[pi] = _synth_square(190.0 * pm, 380.0 * pm, 0.11)
	if _snd_double_jumps[pi] == null:
		_snd_double_jumps[pi] = _synth_square(320.0 * pm, 520.0 * pm, 0.09)
	if _snd_stomp == null:
		_snd_stomp = _synth_stomp()
	if _snd_power_up == null:
		_snd_power_up = _synth_power_up()
	if _snd_squish == null:
		_snd_squish = _synth_squish()

	_audio = AudioStreamPlayer.new()
	_audio.volume_db = -5.0
	add_child(_audio)


func _physics_process(delta: float) -> void:
	if is_eliminated:
		_tick_death_animation(delta)
		return

	_tick_spawn_protection(delta)

	var vel := velocity
	var was_on_floor := is_on_floor()

	# Gravity — Float makes it dreamy
	if not is_on_floor():
		var grav: float = GRAVITY * PWR_GRAVITY_MULT if _pwr_float else GRAVITY
		vel.y += grav * delta

	if is_on_floor():
		_can_double_jump = true

	# Jump — HighJump amplifies it
	var eff_jump: float = jump_velocity * PWR_JUMP_MULT if _pwr_high_jump else jump_velocity
	if Input.is_action_just_pressed(_prefix + "_jump"):
		if is_on_floor():
			vel.y = eff_jump
			_play(_snd_jumps[player_index])
		elif _can_double_jump:
			vel.y = eff_jump
			_can_double_jump = false
			_play(_snd_double_jumps[player_index])

	# Horizontal — Speed amplifies it
	var eff_speed: float = speed * PWR_SPEED_MULT if _pwr_speed else speed
	vel.x = Input.get_axis(_prefix + "_left", _prefix + "_right") * eff_speed

	# Down — fast-fall always; GroundPound arms the slam
	if Input.is_action_just_pressed(_prefix + "_down") and not is_on_floor():
		if _pwr_ground_pound:
			_ground_pound_armed = true
			vel.y = PWR_SLAM_SPEED
		else:
			if vel.y < 600.0:
				vel.y = 600.0

	# Pre-clamp: zero velocity pushing player past shrink bounds before movement
	var gm = _get_game_manager()
	if gm != null and gm.is_shrinking:
		var lb: float = gm.effective_left + PLAYER_HALF_WIDTH
		var rb: float = gm.effective_right - PLAYER_HALF_WIDTH
		if global_position.x <= lb and vel.x < 0.0:
			vel.x = 0.0
		if global_position.x >= rb and vel.x > 0.0:
			vel.x = 0.0

	velocity = vel
	move_and_slide()

	if not was_on_floor and is_on_floor() and _ground_pound_armed:
		_check_ground_pound_landing()

	_clamp_to_bounds()
	_check_stomp()
	_animate_legs(delta)


# =========================================================================
# Power-up system
# =========================================================================

func activate_power_up(type: int) -> void:
	var changed: bool = false
	match type:
		PowerUpType.SPEED:
			if not _pwr_speed:
				_pwr_speed = true; changed = true
		PowerUpType.HIGH_JUMP:
			if not _pwr_high_jump:
				_pwr_high_jump = true; changed = true
		PowerUpType.FLOAT:
			if not _pwr_float:
				_pwr_float = true; changed = true
		PowerUpType.SHIELD:
			if not _pwr_shield:
				_pwr_shield = true; changed = true
		PowerUpType.GROUND_POUND:
			if not _pwr_ground_pound:
				_pwr_ground_pound = true; changed = true
		PowerUpType.TINY:
			if not _pwr_tiny:
				_pwr_tiny = true; _apply_tiny_scale(true); changed = true
	if changed:
		_play(_snd_power_up)
	_refresh_indicators()


func has_power_up(type: int) -> bool:
	match type:
		PowerUpType.SPEED:        return _pwr_speed
		PowerUpType.HIGH_JUMP:    return _pwr_high_jump
		PowerUpType.FLOAT:        return _pwr_float
		PowerUpType.SHIELD:       return _pwr_shield
		PowerUpType.GROUND_POUND: return _pwr_ground_pound
		PowerUpType.TINY:         return _pwr_tiny
	return false


func _refresh_indicators() -> void:
	var active: Array[bool] = [_pwr_speed, _pwr_high_jump, _pwr_float,
		_pwr_shield, _pwr_ground_pound, _pwr_tiny]

	var slots: Array[int] = []
	for i in range(active.size()):
		if active[i]:
			slots.append(i)

	for i in range(_pwr_indicators.size()):
		_pwr_indicators[i].visible = false

	var count: int = slots.size()
	for j in range(count):
		var cx: float = (j - (count - 1) * 0.5) * 10.0 if count > 1 else 0.0
		_pwr_indicators[slots[j]].position = Vector2(cx, -38)
		_pwr_indicators[slots[j]].visible  = true

	var gm = _get_game_manager()
	if gm != null:
		gm.on_power_up_changed(player_index, _build_power_up_display())


func _power_indicator_color(type: int) -> Color:
	if POWER_UP_INFO.has(type):
		return POWER_UP_INFO[type]["color"]
	return Color.WHITE


func _apply_tiny_scale(shrink: bool) -> void:
	_body_root.scale = Vector2(PWR_TINY_SCALE, PWR_TINY_SCALE) if shrink else Vector2.ONE
	(_collision_shape.shape as RectangleShape2D).size = \
		_original_shape_size * PWR_TINY_SCALE if shrink else _original_shape_size


func _check_ground_pound_landing() -> void:
	_ground_pound_armed = false
	_play(_snd_stomp)
	var my_x: float = global_position.x

	for node in get_tree().get_nodes_in_group("players"):
		if not (node is CharacterBody2D):
			continue
		var other = node
		if other == self or other.is_eliminated or other._spawn_timer > 0.0:
			continue
		var dx: float = absf(other.global_position.x - my_x)
		if dx <= PWR_SLAM_KILL_RANGE:
			other.eliminate(player_index)
		elif dx <= PWR_SLAM_PUSH_RANGE:
			var v: Vector2 = other.velocity
			v.x += (1.0 if other.global_position.x > my_x else -1.0) * 350.0
			other.velocity = v


func _build_power_up_display() -> String:
	var s: String = ""
	for type in PWR_ORDER:
		if has_power_up(type):
			s += POWER_UP_INFO[type]["code"] + " "
	return s.strip_edges()


# =========================================================================
# Body construction
# =========================================================================

func _build_body(accent: Color) -> void:
	var body_c := Color(accent.r * 0.62, accent.g * 0.62, accent.b * 0.62)
	var head_c := Color(accent.r * 0.46, accent.g * 0.46, accent.b * 0.46)
	var panel  := Color(0.07, 0.07, 0.10)
	var eye_c  := accent.lerp(Color.WHITE, 0.55)
	var leg_c  := Color(accent.r * 0.50, accent.g * 0.50, accent.b * 0.50)

	_left_thigh.color  = leg_c;  _left_shin.color  = leg_c
	_right_thigh.color = leg_c;  _right_shin.color = leg_c

	match _shape_assignment[player_index % _shape_assignment.size()]:
		0: _build_chunky(body_c, head_c, panel, eye_c)
		1: _build_tall(body_c, head_c, panel, eye_c)
		2: _build_tank(body_c, head_c, panel, eye_c)
		3: _build_wedge(body_c, head_c, panel, eye_c)


func _build_chunky(body_c: Color, head_c: Color, panel: Color, eye_c: Color) -> void:
	_p([Vector2(-14,-2), Vector2(14,-2), Vector2(14,20), Vector2(-14,20)], body_c)
	_p([Vector2(-11,-22), Vector2(11,-22), Vector2(13,-2), Vector2(-13,-2)], head_c)
	_p([Vector2(-9,-20), Vector2(9,-20), Vector2(9,-4), Vector2(-9,-4)], panel)
	_p([Vector2(-8,-15), Vector2(8,-15), Vector2(8,-9), Vector2(-8,-9)], eye_c)
	_left_hip  = Vector2(-9, 20);  _right_hip = Vector2(9, 20)


func _build_tall(body_c: Color, head_c: Color, panel: Color, eye_c: Color) -> void:
	_p([Vector2(-9,-2), Vector2(9,-2), Vector2(9,20), Vector2(-9,20)], body_c)
	_p([Vector2(-10,-22), Vector2(10,-22), Vector2(11,-2), Vector2(-11,-2)], head_c)
	_p([Vector2(-8,-20), Vector2(8,-20), Vector2(8,-4), Vector2(-8,-4)], panel)
	_p([Vector2(-7,-17), Vector2(-2,-17), Vector2(-2,-11), Vector2(-7,-11)], eye_c)
	_p([Vector2(2,-17), Vector2(7,-17), Vector2(7,-11), Vector2(2,-11)], eye_c)
	_left_hip  = Vector2(-8, 20);  _right_hip = Vector2(8, 20)


func _build_tank(body_c: Color, head_c: Color, panel: Color, eye_c: Color) -> void:
	_p([Vector2(-14,4), Vector2(14,4), Vector2(14,22), Vector2(-14,22)], body_c)
	_p([Vector2(-14,-14), Vector2(14,-14), Vector2(14,4), Vector2(-14,4)], head_c)
	_p([Vector2(-12,-12), Vector2(12,-12), Vector2(12,2), Vector2(-12,2)], panel)
	_p([Vector2(-10,-9), Vector2(-5,-9), Vector2(-5,-4), Vector2(-10,-4)], eye_c)
	_p([Vector2(-2,-9), Vector2(2,-9), Vector2(2,-4), Vector2(-2,-4)], eye_c)
	_p([Vector2(5,-9), Vector2(10,-9), Vector2(10,-4), Vector2(5,-4)], eye_c)
	_left_hip  = Vector2(-9, 22);  _right_hip = Vector2(9, 22)


func _build_wedge(body_c: Color, head_c: Color, panel: Color, eye_c: Color) -> void:
	_p([Vector2(-9,-2), Vector2(9,-2), Vector2(14,22), Vector2(-14,22)], body_c)
	_p([Vector2(0,-24), Vector2(9,-2), Vector2(-9,-2)], head_c)
	_p([Vector2(0,-22), Vector2(6,-4), Vector2(-6,-4)], panel)
	_p([Vector2(-4,-14), Vector2(4,-14), Vector2(4,-9), Vector2(-4,-9)], eye_c)
	_left_hip  = Vector2(-10, 20);  _right_hip = Vector2(10, 20)


func _p(pts: Array[Vector2], col: Color) -> void:
	var poly := Polygon2D.new()
	poly.polygon = PackedVector2Array(pts)
	poly.color = col
	_body_root.add_child(poly)


# =========================================================================
# Wheel animation
# =========================================================================

func _animate_legs(delta: float) -> void:
	const WHEEL_R: float = 6.0
	_stride_phase += velocity.x * delta / WHEEL_R
	_left_shin.rotation  = _stride_phase
	_right_shin.rotation = _stride_phase


# =========================================================================
# Animated skin patterns
# =========================================================================

func _process(delta: float) -> void:
	_pattern_time += delta
	_update_pattern()
	_update_shield_visual()


func _update_shield_visual() -> void:
	if not _pwr_shield:
		_shield_visual.visible = false
		return
	_shield_visual.visible = true
	var pulse: float = sin(_pattern_time * 4.5) * 0.5 + 0.5
	_shield_visual.color = Color(0.55, 0.85, 1.00, 0.12 + pulse * 0.18)


func _setup_pattern(accent: Color) -> void:
	const BW: float = 12.0
	const BT: float = -20.0
	const BB: float = 20.0
	var tr := Color(0, 0, 0, 0)

	_pattern_polys = []
	match _pattern_choice:
		0:  # PULSE
			_pattern_polys.append(_overlay([Vector2(-BW,BT),Vector2(BW,BT),Vector2(BW,BB),Vector2(-BW,BB)], tr))
		1:  # SCANLINES
			for i in range(4):
				_pattern_polys.append(_overlay([Vector2(-BW,0),Vector2(BW,0),Vector2(BW,2.5),Vector2(-BW,2.5)],
					Color(0, 0, 0, 0.28)))
		2:  # SWEEP
			for i in range(2):
				_pattern_polys.append(_overlay([Vector2(0,BT),Vector2(3,BT),Vector2(3,BB),Vector2(0,BB)], tr))
		3:  # GRID
			var xs: Array[float] = [-8.0, 0.0, 8.0]
			var ys: Array[float] = [-13.0, -3.0, 7.0]
			for r in range(3):
				for c in range(3):
					var p := _overlay([Vector2(-2,-2),Vector2(2,-2),Vector2(2,2),Vector2(-2,2)], tr)
					p.position = Vector2(xs[c], ys[r])
					_pattern_polys.append(p)

	# Pre-allocate polygon buffers (4 verts each) so _update_pattern can mutate in-place
	_pattern_buffers = []
	for p in _pattern_polys:
		var buf := PackedVector2Array(p.polygon)  # copy initial polygon
		_pattern_buffers.append(buf)
		_body_root.add_child(p)


func _overlay(pts: Array[Vector2], col: Color) -> Polygon2D:
	var poly := Polygon2D.new()
	poly.polygon = PackedVector2Array(pts)
	poly.color = col
	return poly


func _update_pattern() -> void:
	var ac := color_for(player_index)
	const BW: float = 12.0
	const BT: float = -20.0
	const BB: float = 20.0
	const BH: float = 40.0

	match _pattern_choice:
		0:  # PULSE — colour-only, no polygon change
			var a: float = (sin(_pattern_time * 2.4) * 0.5 + 0.5) * 0.22
			_pattern_polys[0].color = Color(ac.r, ac.g, ac.b, a)
		1:  # SCANLINES — mutate cached buffers in-place
			var spacing: float = BH / _pattern_polys.size()
			for i in range(_pattern_polys.size()):
				var y: float = BT + fmod(_pattern_time * 20.0 + i * spacing, BH)
				var buf := _pattern_buffers[i]
				buf[0] = Vector2(-BW, y)
				buf[1] = Vector2( BW, y)
				buf[2] = Vector2( BW, y + 2.5)
				buf[3] = Vector2(-BW, y + 2.5)
				_pattern_polys[i].polygon = buf
		2:  # SWEEP — mutate cached buffers in-place
			for i in range(_pattern_polys.size()):
				var phase: float = fmod(_pattern_time * 0.65 + i * 0.5, 1.0)
				var x: float = lerpf(-BW, BW, phase)
				var a: float = sin(phase * PI) * 0.60
				var buf := _pattern_buffers[i]
				buf[0] = Vector2(x - 1.5, BT)
				buf[1] = Vector2(x + 1.5, BT)
				buf[2] = Vector2(x + 1.5, BB)
				buf[3] = Vector2(x - 1.5, BB)
				_pattern_polys[i].polygon = buf
				_pattern_polys[i].color = Color(ac.r, ac.g, ac.b, a)
		3:  # GRID — colour-only, no polygon change
			for i in range(_pattern_polys.size()):
				var a: float = (sin(_pattern_time * 2.1 + i * 0.72) * 0.5 + 0.5) * 0.75 + 0.05
				_pattern_polys[i].color = Color(ac.r, ac.g, ac.b, a)


func _shield_poly(rx: float, ry: float, segs: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	for i in range(segs):
		var a: float = i * TAU / segs
		pts.append(Vector2(cos(a) * rx, sin(a) * ry))
	return pts


func _circle_poly(r: float, segs: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	for i in range(segs):
		var a: float = i * TAU / segs
		pts.append(Vector2(cos(a) * r, sin(a) * r))
	return pts


# =========================================================================
# Boundary clamp
# =========================================================================

func _clamp_to_bounds() -> void:
	var gm = _get_game_manager()
	if gm == null or not gm.is_shrinking:
		return

	var lb: float = gm.effective_left + PLAYER_HALF_WIDTH
	var rb: float = gm.effective_right - PLAYER_HALF_WIDTH

	var pos := global_position
	var vel := velocity

	if pos.x < lb:
		pos.x = lb
		if vel.x < 0.0:
			vel.x = 0.0
	if pos.x > rb:
		pos.x = rb
		if vel.x > 0.0:
			vel.x = 0.0

	global_position = pos
	velocity = vel


func _tick_spawn_protection(delta: float) -> void:
	if _spawn_timer <= 0.0:
		return
	_spawn_timer -= delta
	_body_root.visible = int(_spawn_timer * 10) % 2 == 0
	if _spawn_timer <= 0.0:
		_body_root.visible = true


func _check_stomp() -> void:
	if _spawn_timer > 0.0:
		return

	for i in range(get_slide_collision_count()):
		var col := get_slide_collision(i)
		var other = col.get_collider()
		if not (other is CharacterBody2D):
			continue
		if other.is_eliminated or other._spawn_timer > 0.0:
			continue

		var landing_on_top: bool = col.get_normal().y < -0.7 and velocity.y >= 0.0
		if not landing_on_top:
			continue

		_play(_snd_stomp)
		other.eliminate(player_index)

		var v := velocity
		v.y = jump_velocity * 0.65
		velocity = v


func eliminate(killer_index: int = -1) -> void:
	if is_eliminated:
		return

	if _pwr_shield:
		_pwr_shield = false
		_refresh_indicators()
		return

	is_eliminated = true
	_death_timer = DEATH_DURATION
	_collision_shape.disabled = true
	velocity = Vector2.ZERO
	_play(_snd_squish)
	var gm = _get_game_manager()
	if gm != null:
		gm.on_player_eliminated(player_index, killer_index)


func _tick_death_animation(delta: float) -> void:
	_death_timer -= delta
	var t: float = 1.0 - maxf(_death_timer / DEATH_DURATION, 0.0)

	var scale_x: float
	var scale_y: float
	if t < 0.18:
		var p: float = t / 0.18
		scale_x = lerpf(1.00, 0.70, p)
		scale_y = lerpf(1.00, 1.45, p)
	else:
		var p: float = (t - 0.18) / 0.82
		var ease_val: float = 1.0 - pow(1.0 - p, 2.5)
		scale_x = lerpf(0.70, 3.20, ease_val)
		scale_y = lerpf(1.45, 0.00, ease_val)

	_body_root.scale = Vector2(scale_x, scale_y)

	if _death_timer <= 0.0:
		queue_free()


func eliminate_quietly() -> void:
	if is_eliminated:
		return
	is_eliminated = true
	queue_free()


# =========================================================================
# Utility
# =========================================================================

func _get_game_manager():
	return get_tree().get_first_node_in_group("game_manager")


# =========================================================================
# Audio helpers
# =========================================================================

func _play(stream: AudioStreamWAV) -> void:
	_audio.stream = stream
	_audio.play()


static func _synth_square(freq_a: float, freq_b: float, dur: float) -> AudioStreamWAV:
	const RATE: int = 8000
	var n: int = int(RATE * dur)
	var data := PackedByteArray()
	data.resize(n)
	var phase: float = 0.0

	for i in range(n):
		var t: float = float(i) / n
		var freq: float = freq_a + (freq_b - freq_a) * t
		phase += freq / RATE
		var env: float = 1.0 - t
		var square: float = 1.0 if fmod(phase, 1.0) < 0.5 else -1.0
		data[i] = int(square * env * 85.0) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.data     = data
	wav.format   = AudioStreamWAV.FORMAT_8_BITS
	wav.mix_rate = RATE
	wav.stereo   = false
	return wav


static func _synth_squish() -> AudioStreamWAV:
	const RATE: int = 8000
	const DUR: float = 0.45
	var n: int = int(RATE * DUR)
	var data := PackedByteArray()
	data.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.seed = 99
	var phase: float = 0.0

	for i in range(n):
		var t: float = float(i) / n

		var freq: float = 180.0 * exp(-t * 7.0) + 35.0
		phase += freq / RATE
		var thump: float = sin(phase * PI * 2.0) * exp(-t * 6.0)

		var noise: float = rng.randf_range(-1.0, 1.0)
		var splat: float = noise * exp(-t * 28.0)

		var chirp_freq: float = 1200.0 - 900.0 * t
		var chirp: float = sin(2.0 * PI * chirp_freq * t) * exp(-t * 40.0) * 0.5

		var s: float = thump * 0.50 + splat * 0.30 + chirp * 0.20
		data[i] = int(clampf(s * 115.0, -127.0, 127.0)) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.data     = data
	wav.format   = AudioStreamWAV.FORMAT_8_BITS
	wav.mix_rate = RATE
	wav.stereo   = false
	return wav


static func _synth_power_up() -> AudioStreamWAV:
	const RATE: int = 8000
	const DUR: float = 0.22
	var n: int = int(RATE * DUR)
	var data := PackedByteArray()
	data.resize(n)
	var phase: float = 0.0

	for i in range(n):
		var t: float = float(i) / n
		var freq: float = 480.0 + 620.0 * t
		phase += freq / RATE
		var env: float = t / 0.08 if t < 0.08 else exp(-(t - 0.08) * 9.0)
		var wave: float = sin(phase * PI * 2.0)
		data[i] = int(wave * env * 95.0) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.data     = data
	wav.format   = AudioStreamWAV.FORMAT_8_BITS
	wav.mix_rate = RATE
	wav.stereo   = false
	return wav


static func _synth_stomp() -> AudioStreamWAV:
	const RATE: int = 8000
	const DUR: float = 0.14
	var n: int = int(RATE * DUR)
	var data := PackedByteArray()
	data.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.seed = 7

	for i in range(n):
		var t: float = float(i) / n
		var env: float = exp(-t * 22.0)
		var noise: float = rng.randf_range(-1.0, 1.0)
		var freq: float = 110.0 - 70.0 * t
		var sine: float = sin(2.0 * PI * freq * t)
		var s: float = (noise * 0.45 + sine * 0.55) * env
		data[i] = int(s * 105.0) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.data     = data
	wav.format   = AudioStreamWAV.FORMAT_8_BITS
	wav.mix_rate = RATE
	wav.stereo   = false
	return wav
