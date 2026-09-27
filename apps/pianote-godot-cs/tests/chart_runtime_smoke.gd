extends SceneTree

var _prepared := false
var _prepare_success := false
var _prepare_error := ""
var _note_count := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed_scene := load("res://main.tscn") as PackedScene
	if packed_scene == null:
		_fail("main.tscn could not be loaded")
		return

	var scene := packed_scene.instantiate()
	root.add_child(scene)
	await process_frame

	var game_ctrl := scene.get_node_or_null("GameCtrl") as Node2D
	var song := scene.get_node_or_null("MusicRepo/Song_002")
	var menu_page := scene.get_node_or_null("UIRoot/MenuPage") as Control
	var song_list_page := scene.get_node_or_null("UIRoot/SongListPage") as Control
	var loading_page := scene.get_node_or_null("UIRoot/LoadingPage") as Control
	var game_page := scene.get_node_or_null("UIRoot/GamePage") as Control
	var setting_page := scene.get_node_or_null("UIRoot/SettingPage") as Control
	if game_ctrl == null or song == null or loading_page == null or game_page == null or setting_page == null:
		_fail("GameCtrl, Song_002, LoadingPage, GamePage, or SettingPage is missing")
		return

	TranslationServer.set_locale("zh_CN")
	await process_frame
	if str(TranslationServer.translate("Pause")) != "暂停":
		_fail("Chinese pause translation was not imported")
		return
	TranslationServer.set_locale("en")
	if song_list_page != null:
		song_list_page.visible = true
		await process_frame
		song_list_page.call("_select_song", 1, false)
		await process_frame
		var cover := song_list_page.get_node("InfoContainer/CoverImage") as TextureRect
		var title := song_list_page.get_node("InfoContainer/TitleLabel") as Label
		var info := song_list_page.get_node("InfoContainer/InfoLabel") as Label
		if info.text == "SONG_INFO" or not info.text.contains("Composer:"):
			_fail("song information translation was not resolved: %s" % info.text)
			return
		if cover.visible and cover.position.y + cover.size.y > title.position.y + 0.5:
			_fail("song cover overlaps the title")
			return
		if title.position.y + title.size.y > info.position.y + 0.5:
			_fail("song title overlaps the information text")
			return

	game_ctrl.connect("ChartPrepared", Callable(self, "_on_chart_prepared"))
	if menu_page != null:
		menu_page.visible = false
	if song_list_page != null:
		song_list_page.visible = false
	loading_page.call("begin_loading", song, "Normal")

	var deadline := Time.get_ticks_msec() + 10000
	while not game_page.visible and Time.get_ticks_msec() < deadline:
		await process_frame

	if not _prepared:
		_fail("chart preparation timed out")
		return
	if not _prepare_success:
		_fail("chart preparation failed: %s" % _prepare_error)
		return
	if _note_count != 1181:
		_fail("unexpected Song_002 note count: %d" % _note_count)
		return
	for _frame in range(4):
		await process_frame

	var keyboard_root := scene.get_node("GameCtrl/KeyboardRoot")
	var note_root := scene.get_node("GameCtrl/NoteLaneRoot")
	if keyboard_root.get_child_count() != 88:
		_fail("expected 88 piano keys, got %d" % keyboard_root.get_child_count())
		return
	if note_root.get_child_count() == 0:
		_fail("no individual note bars were spawned")
		return

	var pause_overlay := game_page.get_node("PauseOverlay") as Control
	game_page.call("_on_btn_pause_pressed")
	await process_frame
	if game_ctrl.call("IsSongPaused") != true or not pause_overlay.visible:
		_fail("pause menu did not freeze the game")
		return
	var paused_playhead: float = game_ctrl.call("GetPlayheadSec")
	await create_timer(0.2).timeout
	if absf(float(game_ctrl.call("GetPlayheadSec")) - paused_playhead) > 0.01:
		_fail("playhead advanced while paused")
		return

	game_page.call("_on_btn_settings_pressed")
	await process_frame
	if not setting_page.visible or game_page.visible:
		_fail("settings did not open from the pause menu")
		return
	await create_timer(0.1).timeout
	setting_page.call("_on_btn_back_pressed")
	await process_frame
	if not game_page.visible or not pause_overlay.visible or game_ctrl.call("IsSongPaused") != true:
		_fail("settings did not return to the paused game")
		return
	if absf(float(game_ctrl.call("GetPlayheadSec")) - paused_playhead) > 0.01:
		_fail("settings changed the paused playhead")
		return

	game_page.call("_on_btn_continue_pressed")
	await process_frame
	var resume_deadline := Time.get_ticks_msec() + 5000
	while game_ctrl.call("IsSongPaused") == true and Time.get_ticks_msec() < resume_deadline:
		await process_frame
	if game_ctrl.call("IsSongPaused") == true:
		_fail("countdown did not resume the game")
		return
	await create_timer(0.1).timeout
	if float(game_ctrl.call("GetPlayheadSec")) <= paused_playhead:
		_fail("playhead did not continue after countdown")
		return

	game_page.call("_on_btn_pause_pressed")
	game_page.call("_on_btn_song_list_pressed")
	await process_frame
	if song_list_page == null or not song_list_page.visible or game_ctrl.visible:
		_fail("return-to-song-list did not stop the game")
		return

	song_list_page.visible = false
	game_page.visible = true
	if game_ctrl.call("StartPreparedSong") != true:
		_fail("prepared chart could not restart for main-menu test")
		return
	await process_frame
	game_page.call("_on_btn_pause_pressed")
	game_page.call("_on_btn_main_menu_pressed")
	await process_frame
	if menu_page == null or not menu_page.visible or game_ctrl.visible:
		_fail("return-to-main-menu did not stop the game")
		return

	var result := "CHART_RUNTIME_SMOKE_OK notes=%d keys=88 pause_flow=ok" % _note_count
	_write_result(result)
	print(result)
	quit(0)


func _on_chart_prepared(success: bool, error: String, note_count: int, _duration_sec: float) -> void:
	_prepared = true
	_prepare_success = success
	_prepare_error = error
	_note_count = note_count


func _fail(message: String) -> void:
	_write_result("CHART_RUNTIME_SMOKE_FAILED: %s" % message)
	push_error("CHART_RUNTIME_SMOKE_FAILED: %s" % message)
	quit(1)


func _write_result(message: String) -> void:
	var file := FileAccess.open("res://tests/chart_runtime_smoke.result", FileAccess.WRITE)
	if file != null:
		file.store_string(message)
