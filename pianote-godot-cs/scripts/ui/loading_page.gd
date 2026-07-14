extends Control

@export var song_list_page: Control
@export var game_page: Control
@export var game_ctrl: Node
@export var title_label: Label
@export var status_label: Label
@export var detail_label: Label
@export var progress_bar: ProgressBar
@export var btn_back: Button

var _loading := false
var _pulse := 0.0


func _ready() -> void:
	visible = false
	set_process(false)
	if game_ctrl != null:
		if game_ctrl.has_signal("ChartPreparationStarted"):
			game_ctrl.connect("ChartPreparationStarted", Callable(self, "_on_chart_preparation_started"))
		if game_ctrl.has_signal("ChartPrepared"):
			game_ctrl.connect("ChartPrepared", Callable(self, "_on_chart_prepared"))
	resized.connect(Callable(self, "_apply_responsive_layout"))
	call_deferred("_apply_responsive_layout")


func begin_loading(song: SongData, difficulty: String) -> void:
	if song == null or game_ctrl == null:
		_show_error(tr("The selected song or game controller is unavailable."))
		return

	_loading = true
	_pulse = 0.0
	visible = true
	set_process(true)
	title_label.text = song.song_name
	status_label.text = tr("Preparing chart")
	detail_label.text = tr("Reading score.midi")
	progress_bar.value = 12.0
	btn_back.visible = false
	game_ctrl.call_deferred("PrepareSongAsync", song, difficulty)


func _process(delta: float) -> void:
	if not _loading:
		return

	_pulse = fmod(_pulse + delta * 42.0, 58.0)
	progress_bar.value = 18.0 + _pulse


func _on_chart_preparation_started(song_name: String) -> void:
	if not _loading:
		return

	title_label.text = song_name
	status_label.text = tr("Converting MIDI")
	detail_label.text = tr("Building individual piano notes")


func _on_chart_prepared(success: bool, error: String, note_count: int, duration_sec: float) -> void:
	if not _loading:
		return

	if not success:
		_show_error(error if not error.is_empty() else tr("The MIDI chart contains no notes."))
		return

	_loading = false
	set_process(false)
	progress_bar.value = 100.0
	status_label.text = tr("Chart ready")
	detail_label.text = tr("%d notes  |  %s") % [note_count, _format_duration(duration_sec)]
	await get_tree().process_frame

	if game_ctrl.call("StartPreparedSong") != true:
		_show_error(tr("The chart was converted but the game could not start."))
		return

	visible = false
	game_page.visible = true


func _show_error(message: String) -> void:
	_loading = false
	set_process(false)
	visible = true
	status_label.text = tr("Unable to prepare chart")
	detail_label.text = message
	progress_bar.value = 0.0
	btn_back.visible = true


func _on_btn_back_pressed() -> void:
	_loading = false
	set_process(false)
	if game_ctrl != null:
		game_ctrl.call("StopSong")
	visible = false
	song_list_page.visible = true


func _apply_responsive_layout() -> void:
	var content := get_node_or_null("LoadingContent") as VBoxContainer
	if content == null:
		return

	var viewport_size := size
	if viewport_size.x <= 1.0 or viewport_size.y <= 1.0:
		viewport_size = get_viewport_rect().size
	var width := clampf(viewport_size.x * 0.44, 360.0, 640.0)
	var height := clampf(viewport_size.y * 0.42, 260.0, 390.0)
	content.position = (viewport_size - Vector2(width, height)) * 0.5
	content.size = Vector2(width, height)


func _format_duration(duration_sec: float) -> String:
	var total_seconds := maxi(0, roundi(duration_sec))
	return "%d:%02d" % [total_seconds / 60, total_seconds % 60]
