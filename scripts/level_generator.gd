extends Node2D

## Generates a procedural platform layout guaranteed to be fully reachable with double-jump.

enum LevelTheme { CIRCUIT, PULSE, HAZARD }

@export var player_count: int = 2

# --- Arena bounds (must match wall/ground positions in Level.tscn) ---
const LEVEL_LEFT: float   =   30.0
const LEVEL_RIGHT: float  = 1220.0
const PLAT_MIN_Y: float   =  180.0
const PLAT_MAX_Y: float   =  520.0

# --- Spawn anchor specs ---
const ANCHOR_Y: float      = 480.0
const ANCHOR_WIDTH: float  = 160.0
const ANCHOR_MARGIN: float = 100.0
const PLAYER_SPAWN_Y: float = 446.0

# --- Reachability limits ---
const MAX_GAP: float        = 200.0
const MAX_HEIGHT_STEP: float = 160.0
const MIN_PLAT_WIDTH: float =  80.0
const MAX_PLAT_WIDTH: float = 150.0

# =========================================================================
# Level theme
# =========================================================================

static var current_theme: LevelTheme
static var theme_accent: Color
static var theme_dark: Color

# Theme palettes: [dark, bright, accent]
const THEMES: Array = [
	# Circuit — deep green, circuit-board traces
	[Color(0.08, 0.15, 0.08), Color(0.20, 0.38, 0.20), Color(0.20, 0.88, 0.28)],
	# Pulse — dark navy, cyan scan lines
	[Color(0.07, 0.09, 0.20), Color(0.15, 0.18, 0.38), Color(0.28, 0.62, 1.00)],
	# Hazard — amber rust, diagonal warning marks
	[Color(0.17, 0.11, 0.04), Color(0.32, 0.22, 0.08), Color(1.00, 0.62, 0.08)],
]

var _rng := RandomNumberGenerator.new()
var _last_y: float = ANCHOR_Y


func _ready() -> void:
	_rng.randomize()
	current_theme = _rng.randi_range(0, THEMES.size() - 1) as LevelTheme
	var td: Array = THEMES[current_theme]
	theme_accent = td[2]
	theme_dark   = td[0]
	_generate()
	_decorate_arena()


## World-space spawn position for a given player index.
static func spawn_position_for(player_index: int, p_count: int) -> Vector2:
	var start_x: float = LEVEL_LEFT + ANCHOR_MARGIN
	var end_x: float   = LEVEL_RIGHT - ANCHOR_MARGIN
	var t: float = float(player_index) / float(p_count - 1) if p_count > 1 else 0.5
	return Vector2(lerpf(start_x, end_x, t), PLAYER_SPAWN_Y)


func _generate() -> void:
	# One guaranteed anchor platform under each player spawn
	for i in range(player_count):
		var pos := spawn_position_for(i, player_count)
		_spawn_platform(pos.x, ANCHOR_Y, ANCHOR_WIDTH)

	# Fill the gap between consecutive anchor pairs
	for i in range(player_count - 1):
		var gap_start: float = spawn_position_for(i, player_count).x + ANCHOR_WIDTH / 2.0
		var gap_end: float   = spawn_position_for(i + 1, player_count).x - ANCHOR_WIDTH / 2.0
		_fill_gap(gap_start, gap_end, ANCHOR_Y)


func _fill_gap(from_x: float, to_x: float, start_y: float) -> void:
	var right_edge: float = from_x
	_last_y = start_y

	while true:
		var gap: float   = _rng.randf_range(20.0, MAX_GAP * 0.85)
		var w: float     = _rng.randf_range(MIN_PLAT_WIDTH, MAX_PLAT_WIDTH)
		var cx: float    = right_edge + gap + w / 2.0

		if cx + w / 2.0 > to_x - 30.0:
			break

		var dy: float = _rng.randf_range(-MAX_HEIGHT_STEP, MAX_HEIGHT_STEP)
		var cy: float = clampf(_last_y + dy, PLAT_MIN_Y, PLAT_MAX_Y)

		_spawn_platform(cx, cy, w)
		right_edge = cx + w / 2.0


func _spawn_platform(cx: float, cy: float, width: float) -> void:
	var td: Array = THEMES[current_theme]
	var td_dark: Color   = td[0]
	var td_bright: Color = td[1]
	var td_accent: Color = td[2]

	# Height-based shading: higher platforms are brighter (depth cue)
	var t: float = 1.0 - (cy - PLAT_MIN_Y) / (PLAT_MAX_Y - PLAT_MIN_Y)
	var base_col: Color = td_dark.lerp(td_bright, t)

	var hw: float = width / 2.0
	var body := StaticBody2D.new()
	body.position = Vector2(cx, cy)

	var col_shape := CollisionShape2D.new()
	var rect_shape := RectangleShape2D.new()
	rect_shape.size = Vector2(width, 20.0)
	col_shape.shape = rect_shape
	body.add_child(col_shape)

	# Base rectangle
	var base_poly := Polygon2D.new()
	base_poly.color = base_col
	base_poly.polygon = PackedVector2Array([
		Vector2(-hw, -10), Vector2(hw, -10), Vector2(hw, 10), Vector2(-hw, 10)])
	body.add_child(base_poly)

	# Theme-specific decorations
	match current_theme:
		LevelTheme.CIRCUIT:
			_decorate_circuit(body, hw, td_accent)
		LevelTheme.PULSE:
			_decorate_pulse(body, hw, td_accent)
		LevelTheme.HAZARD:
			_decorate_hazard(body, hw, td_accent)

	add_child(body)
	_last_y = cy


# =========================================================================
# Decoration helpers
# =========================================================================

## Circuit board: top edge glow, horizontal trace, via pads.
static func _decorate_circuit(body: Node, hw: float, accent: Color) -> void:
	var dim := _dim_accent(accent, 0.65)
	_add_rect(body, -hw, -10.0, hw * 2.0, 2.0, dim)

	if hw < 24.0:
		return

	_add_rect(body, -hw + 8.0, -6.0, hw * 2.0 - 16.0, 1.5, dim)

	var spacing: float = maxf(18.0, (hw * 2.0 - 20.0) / 4.0)
	var vx: float = -hw + 10.0
	while vx < hw - 6.0:
		_add_rect(body, vx - 2.5, -8.0, 5.0, 5.0, accent)
		vx += spacing

	# Corner L-brackets
	_add_rect(body, -hw,      -10.0, 5.0, 2.0, accent)
	_add_rect(body, -hw,      -10.0, 2.0, 5.0, accent)
	_add_rect(body,  hw - 5.0, -10.0, 5.0, 2.0, accent)
	_add_rect(body,  hw - 2.0, -10.0, 2.0, 5.0, accent)


## Pulse / scan: evenly-spaced vertical bright lines.
static func _decorate_pulse(body: Node, hw: float, accent: Color) -> void:
	_add_rect(body, -hw, -10.0, hw * 2.0, 2.0, _dim_accent(accent, 0.55))

	var sx: float = -hw + 7.0
	var tick: int = 0
	while sx < hw - 3.0:
		_add_rect(body, sx, -10.0, 2.0, 20.0, _dim_accent(accent, 0.25))
		if tick % 2 == 0:
			_add_rect(body, sx, -10.0, 2.0, 4.0, accent)
		sx += 14.0
		tick += 1


## Hazard: amber diagonal chevrons.
static func _decorate_hazard(body: Node, hw: float, accent: Color) -> void:
	var dim := _dim_accent(accent, 0.55)
	_add_rect(body, -hw, -10.0, hw * 2.0, 2.0, dim)
	_add_rect(body, -hw,  -7.0, hw * 2.0, 1.0, _dim_accent(accent, 0.30))

	const SLASH_W: float = 6.0
	var sx: float = -hw + 4.0
	while sx < hw - 2.0:
		var poly := Polygon2D.new()
		poly.color = dim
		poly.polygon = PackedVector2Array([
			Vector2(sx + 4.0, -10.0), Vector2(sx + 4.0 + SLASH_W, -10.0),
			Vector2(sx + SLASH_W,  10.0), Vector2(sx,               10.0)])
		body.add_child(poly)
		sx += 18.0


# =========================================================================
# Utility
# =========================================================================

static func _add_rect(parent: Node, x: float, y: float, w: float, h: float, col: Color) -> void:
	var poly := Polygon2D.new()
	poly.color = col
	poly.polygon = PackedVector2Array([
		Vector2(x, y), Vector2(x + w, y), Vector2(x + w, y + h), Vector2(x, y + h)])
	parent.add_child(poly)


static func _dim_accent(c: Color, factor: float) -> Color:
	return Color(c.r * factor, c.g * factor, c.b * factor)


# =========================================================================
# Arena boundary decoration
# =========================================================================

func _decorate_arena() -> void:
	var td: Array = THEMES[current_theme]
	var td_dark: Color   = td[0]
	var td_accent: Color = td[2]
	var level := get_parent()
	var edge := _dim_accent(td_accent, 0.7)

	# ---- Ground ----
	(level.get_node("Ground/Visual") as Polygon2D).color = td_dark
	var ground: Node2D = level.get_node("Ground")
	_add_rect(ground, -640.0, -10.0, 1280.0, 2.0, edge)
	_decorate_ground_surface(ground, td)

	# ---- Left boundary strip ----
	var wall_l: Node2D = level.get_node("WallLeft")
	_add_rect(wall_l,  2.0, -460.0, 18.0, 920.0, td_dark)
	_add_rect(wall_l, 18.0, -460.0,  2.0, 920.0, edge)
	_decorate_wall_surface(wall_l, td, 2.0, 18.0, 920.0, -460.0)

	# ---- Right boundary strip ----
	var wall_r: Node2D = level.get_node("WallRight")
	_add_rect(wall_r, -20.0, -460.0, 18.0, 920.0, td_dark)
	_add_rect(wall_r, -20.0, -460.0,  2.0, 920.0, edge)
	_decorate_wall_surface(wall_r, td, -20.0, 18.0, 920.0, -460.0)

	# ---- Ceiling ----
	var ceil: Node2D = level.get_node("Ceiling")
	_add_rect(ceil, -680.0, 20.0, 1360.0, 22.0, td_dark)
	_add_rect(ceil, -680.0, 20.0, 1360.0,  2.0, edge)


func _decorate_ground_surface(ground: Node2D, td: Array) -> void:
	var td_accent: Color = td[2]
	match current_theme:
		LevelTheme.CIRCUIT:
			_add_rect(ground, -620.0, -7.0, 1240.0, 1.5, _dim_accent(td_accent, 0.55))
			var vx: float = -600.0
			while vx < 600.0:
				_add_rect(ground, vx - 3.0, -10.0, 6.0, 5.0, td_accent)
				vx += 80.0

		LevelTheme.PULSE:
			var sx: float = -630.0
			while sx < 630.0:
				_add_rect(ground, sx, -20.0, 2.0, 30.0, _dim_accent(td_accent, 0.22))
				sx += 20.0
			sx = -630.0
			while sx < 630.0:
				_add_rect(ground, sx, -20.0, 2.0, 5.0, _dim_accent(td_accent, 0.70))
				sx += 80.0

		LevelTheme.HAZARD:
			var sx: float = -640.0
			while sx < 640.0:
				var poly := Polygon2D.new()
				poly.color = _dim_accent(td_accent, 0.30)
				poly.polygon = PackedVector2Array([
					Vector2(sx + 6.0, -20.0), Vector2(sx + 14.0, -20.0),
					Vector2(sx + 8.0,  10.0), Vector2(sx,         10.0)])
				ground.add_child(poly)
				sx += 32.0


func _decorate_wall_surface(wall: Node2D, td: Array,
		x0: float, strip_w: float, strip_h: float, y0: float) -> void:
	var td_accent: Color = td[2]
	match current_theme:
		LevelTheme.CIRCUIT:
			var ty: float = y0 + 30.0
			while ty < y0 + strip_h - 10.0:
				_add_rect(wall, x0 + 2.0, ty, strip_w - 4.0, 1.5, _dim_accent(td_accent, 0.55))
				ty += 60.0
			ty = y0 + 30.0
			while ty < y0 + strip_h - 10.0:
				_add_rect(wall, x0 + strip_w / 2.0 - 3.0, ty - 3.0, 6.0, 6.0, td_accent)
				ty += 120.0

		LevelTheme.PULSE:
			var ty: float = y0 + 20.0
			while ty < y0 + strip_h - 10.0:
				_add_rect(wall, x0 + 2.0, ty, strip_w - 4.0, 2.0, _dim_accent(td_accent, 0.40))
				ty += 28.0

		LevelTheme.HAZARD:
			var ty: float = y0 + 10.0
			while ty < y0 + strip_h - 20.0:
				var poly := Polygon2D.new()
				poly.color = _dim_accent(td_accent, 0.32)
				poly.polygon = PackedVector2Array([
					Vector2(x0 + 2.0,          ty),
					Vector2(x0 + strip_w - 2.0, ty + 10.0),
					Vector2(x0 + strip_w - 2.0, ty + 14.0),
					Vector2(x0 + 2.0,           ty +  4.0)])
				wall.add_child(poly)
				ty += 36.0
