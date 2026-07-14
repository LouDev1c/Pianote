extends Control

@export var song_list_page: Control
@export var menu_page: Control
@export var setting_page: Control
@export var game_ctrl: Node
@export var eval_ctrl: Node
@export var judgement_label: Label
@export var score_label: Label
@export var combo_label: Label
@export var pause_button: Button
@export var pause_overlay: Control
@export var countdown_overlay: Control
@export var countdown_label: Label

@export_range(0.1, 2.0, 0.05) var countdown_step_seconds := 1.0

var _resume_in_progress := false


func _ready() -> void:
	visible = false
	if pause_overlay != null:
		pause_overlay.visible = false
	if countdown_overlay != null:
		countdown_overlay.visible = false

	if eval_ctrl != null:
		if eval_ctrl.has_signal("Judgement"):
			eval_ctrl.connect("Judgement", Callable(self, "_on_judgement"))
		if eval_ctrl.has_signal("ScoreChanged"):
			eval_ctrl.connect("ScoreChanged", Callable(self, "_on_score_changed"))

	resized.connect(Callable(self, "_apply_responsive_layout"))
	call_deferred("_apply_responsive_layout")


func _on_btn_pause_pressed() -> void:
	if _resume_in_progress or game_ctrl == null:
		return
	if game_ctrl.call("PauseSong") != true:
		return
	_show_pause_menu()


func _on_btn_continue_pressed() -> void:
	if _resume_in_progress or game_ctrl == null or game_ctrl.call("IsSongPaused") != true:
		return

	_resume_in_progress = true
	pause_overlay.visible = false
	countdown_overlay.visible = true
	if pause_button != null:
		pause_button.disabled = true

	for count in [3, 2, 1]:
		countdown_label.text = str(count)
		await get_tree().create_timer(countdown_step_seconds).timeout

	countdown_overlay.visible = false
	game_ctrl.call("ResumeSong")
	if pause_button != null:
		pause_button.disabled = false
	_resume_in_progress = false


func _on_btn_song_list_pressed() -> void:
	_stop_and_leave(song_list_page)


func _on_btn_main_menu_pressed() -> void:
	_stop_and_leave(menu_page)


func _on_btn_settings_pressed() -> void:
	if _resume_in_progress or setting_page == null:
		return

	pause_overlay.visible = false
	visible = false
	if setting_page.has_method("open_from"):
		setting_page.call("open_from", self)
	else:
		setting_page.visible = true


func on_return_from_settings() -> void:
	visible = true
	_show_pause_menu()


func _show_pause_menu() -> void:
	if countdown_overlay != null:
		countdown_overlay.visible = false
	if pause_overlay != null:
		pause_overlay.visible = true
	if pause_button != null:
		pause_button.disabled = false


func _stop_and_leave(target_page: Control) -> void:
	_resume_in_progress = false
	if game_ctrl != null:
		game_ctrl.call("StopSong")
	if pause_overlay != null:
		pause_overlay.visible = false
	if countdown_overlay != null:
		countdown_overlay.visible = false
	visible = false
	if target_page != null:
		target_page.visible = true


func _on_judgement(label: String, offset_sec: float, _score_delta: int, _combo: int) -> void:
	judgement_label.text = "%s  %+0.3fs" % [tr(label), offset_sec]


func _on_score_changed(score: int, combo: int) -> void:
	score_label.text = tr("Score: %d") % score
	combo_label.text = tr("Combo: %d") % combo


func _apply_responsive_layout() -> void:
	var page_size := size
	if page_size.x <= 1.0 or page_size.y <= 1.0:
		page_size = get_viewport_rect().size

	if pause_button != null:
		pause_button.position = Vector2(clampf(page_size.x * 0.025, 20.0, 42.0), clampf(page_size.y * 0.03, 18.0, 34.0))
		pause_button.size = Vector2(clampf(page_size.x * 0.09, 96.0, 140.0), clampf(page_size.y * 0.06, 40.0, 54.0))

	var hud := get_node_or_null("Hud") as VBoxContainer
	if hud != null:
		var hud_width := clampf(page_size.x * 0.21, 220.0, 320.0)
		hud.position = Vector2(page_size.x - hud_width - clampf(page_size.x * 0.025, 20.0, 42.0), clampf(page_size.y * 0.03, 18.0, 34.0))
		hud.size = Vector2(hud_width, clampf(page_size.y * 0.24, 150.0, 220.0))

	var pause_panel := get_node_or_null("PauseOverlay/PausePanel") as Panel
	if pause_panel != null:
		var panel_width := clampf(page_size.x * 0.34, 360.0, 500.0)
		var panel_height := clampf(page_size.y * 0.64, 390.0, 520.0)
		pause_panel.position = (page_size - Vector2(panel_width, panel_height)) * 0.5
		pause_panel.size = Vector2(panel_width, panel_height)
