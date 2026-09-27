extends Control

@export var menu_page: Control
@export var game_page: Control
@export var loading_page: Control
@export var music_repo: MusicRepo
@export var game_ctrl: Node
@export var cover_image: TextureRect
@export var title_label: Label
@export var info_label: Label
@export var btn_difficulty: Button
@export var btn_start: Button
@export var preview_player: AudioStreamPlayer
@export var start_confirmation: Control
@export var song_items: Array[Button] = []

var current_song: SongData = null
var _difficulty_index := 1
var _song_list_container: VBoxContainer
var _scroll_song_list: ScrollContainer
var _list_container: VBoxContainer
var _info_container: Control
var _back_button: Button


func _ready() -> void:
	visible = false
	if start_confirmation != null:
		start_confirmation.visible = false
	_cache_layout_nodes()
	_configure_static_layout()

	for i in range(song_items.size()):
		var index := i
		song_items[i].pressed.connect(Callable(self, "_select_song").bind(index))

	_refresh_song_buttons()
	_show_empty_song_info()

	resized.connect(Callable(self, "_apply_responsive_layout"))
	call_deferred("_apply_responsive_layout")


func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSLATION_CHANGED and is_node_ready():
		if current_song == null:
			_show_empty_song_info()
		else:
			_select_song(music_repo.get_songs().find(current_song), false)
	if what == NOTIFICATION_VISIBILITY_CHANGED and visible:
		_show_empty_song_info()
		if start_confirmation != null:
			start_confirmation.visible = false
		call_deferred("_apply_responsive_layout")


func _cache_layout_nodes() -> void:
	_song_list_container = get_node_or_null("SongListContainer") as VBoxContainer
	_scroll_song_list = get_node_or_null("SongListContainer/ScrollSongList") as ScrollContainer
	_list_container = get_node_or_null("SongListContainer/ScrollSongList/ListContainer") as VBoxContainer
	_info_container = get_node_or_null("InfoContainer") as Control
	_back_button = get_node_or_null("Btn_Back") as Button


func _configure_static_layout() -> void:
	if _scroll_song_list != null:
		_scroll_song_list.horizontal_scroll_mode = 0
		_scroll_song_list.vertical_scroll_mode = 2
		_scroll_song_list.follow_focus = true
		_scroll_song_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_scroll_song_list.size_flags_vertical = Control.SIZE_EXPAND_FILL

	if _list_container != null:
		_list_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	if title_label != null:
		title_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		title_label.clip_text = true

	if info_label != null:
		info_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART

	if cover_image != null:
		cover_image.size_flags_horizontal = Control.SIZE_SHRINK_CENTER

	if btn_difficulty != null:
		btn_difficulty.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	if btn_start != null:
		btn_start.size_flags_horizontal = Control.SIZE_EXPAND_FILL


func _apply_responsive_layout() -> void:
	if _song_list_container == null or _info_container == null:
		return

	var page_size := size
	if page_size.x <= 1.0 or page_size.y <= 1.0:
		page_size = get_viewport_rect().size

	var page_width := maxf(page_size.x, 640.0)
	var page_height := maxf(page_size.y, 360.0)
	var short_side := minf(page_width, page_height)
	var margin := clampf(short_side * 0.045, 24.0, 64.0)
	var gap := clampf(page_width * 0.024, 18.0, 48.0)
	var back_width := clampf(page_width * 0.078, 88.0, 128.0)
	var back_height := clampf(page_height * 0.06, 40.0, 54.0)
	var content_top := margin + back_height + clampf(page_height * 0.035, 18.0, 36.0)
	var content_height := maxf(220.0, page_height - content_top - margin)

	if _back_button != null:
		_back_button.position = Vector2(margin, margin * 0.75)
		_back_button.size = Vector2(back_width, back_height)

	if page_width < 980.0:
		_apply_stacked_layout(page_width, content_top, content_height, margin, gap)
	else:
		_apply_wide_layout(page_width, content_top, content_height, margin, gap)

	_apply_child_sizes(page_width, page_height)
	_layout_start_confirmation(page_width, page_height)


func _layout_start_confirmation(page_width: float, page_height: float) -> void:
	if start_confirmation == null:
		return
	var panel := start_confirmation.get_node_or_null("DialogPanel") as Panel
	if panel == null:
		return
	var panel_size := Vector2(
		clampf(page_width * 0.58, 560.0, 780.0),
		clampf(page_height * 0.34, 230.0, 310.0)
	)
	panel.position = (Vector2(page_width, page_height) - panel_size) * 0.5
	panel.size = panel_size


func _apply_wide_layout(page_width: float, content_top: float, content_height: float, margin: float, gap: float) -> void:
	var available_width := page_width - margin * 2.0 - gap
	var list_width := clampf(available_width * 0.56, 360.0, available_width - 320.0)
	var info_width := available_width - list_width

	if info_width < 300.0:
		list_width = maxf(300.0, available_width * 0.5)
		info_width = available_width - list_width

	_song_list_container.position = Vector2(margin, content_top)
	_song_list_container.size = Vector2(list_width, content_height)
	_info_container.position = Vector2(margin + list_width + gap, content_top)
	_info_container.size = Vector2(info_width, content_height)


func _apply_stacked_layout(page_width: float, content_top: float, content_height: float, margin: float, gap: float) -> void:
	var full_width := page_width - margin * 2.0
	var list_height := maxf(170.0, content_height * 0.44)
	var info_height := maxf(170.0, content_height - list_height - gap)

	_song_list_container.position = Vector2(margin, content_top)
	_song_list_container.size = Vector2(full_width, list_height)
	_info_container.position = Vector2(margin, content_top + list_height + gap)
	_info_container.size = Vector2(full_width, info_height)


func _apply_child_sizes(page_width: float, page_height: float) -> void:
	var list_width := maxf(220.0, _song_list_container.size.x)
	var list_height := maxf(120.0, _song_list_container.size.y)
	var item_height := clampf(page_height * 0.105, 58.0, 96.0)

	_song_list_container.custom_minimum_size = Vector2.ZERO
	if _scroll_song_list != null:
		_scroll_song_list.custom_minimum_size = Vector2(list_width, list_height)
	if _list_container != null:
		_list_container.custom_minimum_size = Vector2(maxf(180.0, list_width - 24.0), 0.0)

	for button in song_items:
		if button != null:
			button.custom_minimum_size = Vector2(maxf(180.0, list_width - 32.0), item_height)

	var info_width := maxf(240.0, _info_container.size.x)
	var info_height := maxf(220.0, _info_container.size.y)

	_info_container.custom_minimum_size = Vector2.ZERO
	_layout_info_children(info_width, info_height, page_height)


func _layout_info_children(info_width: float, info_height: float, page_height: float) -> void:
	var gap := clampf(page_height * 0.018, 10.0, 16.0)
	var button_width := minf(info_width, 420.0)
	var button_x := (info_width - button_width) * 0.5
	var difficulty_height := clampf(page_height * 0.058, 38.0, 46.0)
	var start_height := clampf(page_height * 0.067, 44.0, 56.0)
	var start_y := info_height - start_height
	var difficulty_y := start_y - gap - difficulty_height
	var content_bottom := maxf(96.0, difficulty_y - gap)

	if btn_difficulty != null:
		btn_difficulty.custom_minimum_size = Vector2.ZERO
		btn_difficulty.position = Vector2(button_x, difficulty_y)
		btn_difficulty.size = Vector2(button_width, difficulty_height)
	if btn_start != null:
		btn_start.custom_minimum_size = Vector2.ZERO
		btn_start.position = Vector2(button_x, start_y)
		btn_start.size = Vector2(button_width, start_height)

	if current_song == null:
		_layout_empty_info(info_width, content_bottom, page_height)
	else:
		_layout_selected_info(info_width, content_bottom, page_height)


func _layout_empty_info(info_width: float, content_bottom: float, page_height: float) -> void:
	if cover_image != null:
		cover_image.visible = false

	var title_height := clampf(page_height * 0.06, 34.0, 46.0)
	var info_height := clampf(page_height * 0.04, 24.0, 32.0)
	var block_height := title_height + info_height + 8.0
	var start_y := maxf(0.0, (content_bottom - block_height) * 0.58)

	if title_label != null:
		title_label.custom_minimum_size = Vector2.ZERO
		title_label.position = Vector2(0.0, start_y)
		title_label.size = Vector2(info_width, title_height)
	if info_label != null:
		info_label.custom_minimum_size = Vector2.ZERO
		info_label.position = Vector2(0.0, start_y + title_height + 8.0)
		info_label.size = Vector2(info_width, info_height)


func _layout_selected_info(info_width: float, content_bottom: float, page_height: float) -> void:
	var gap := clampf(page_height * 0.018, 10.0, 16.0)
	var title_height := clampf(page_height * 0.05, 30.0, 38.0)
	var minimum_info_height := clampf(page_height * 0.14, 86.0, 120.0)
	var max_cover_height := maxf(90.0, content_bottom - title_height - minimum_info_height - gap * 2.0)
	var cover_size := clampf(minf(info_width, minf(max_cover_height, content_bottom * 0.48)), 110.0, 240.0)
	var text_y := cover_size + gap + title_height + gap
	var text_height := maxf(56.0, content_bottom - text_y)

	if cover_image != null:
		cover_image.custom_minimum_size = Vector2.ZERO
		cover_image.visible = true
		cover_image.position = Vector2((info_width - cover_size) * 0.5, 0.0)
		cover_image.size = Vector2(cover_size, cover_size)
	if title_label != null:
		title_label.custom_minimum_size = Vector2.ZERO
		title_label.position = Vector2(0.0, cover_size + gap)
		title_label.size = Vector2(info_width, title_height)
	if info_label != null:
		info_label.custom_minimum_size = Vector2.ZERO
		info_label.position = Vector2(0.0, text_y)
		info_label.size = Vector2(info_width, text_height)


func _refresh_song_buttons() -> void:
	if music_repo == null:
		return

	var songs := music_repo.get_songs()
	_ensure_song_buttons(songs.size())
	for i in range(song_items.size()):
		var button := song_items[i]
		if i < songs.size():
			button.visible = true
			button.disabled = false
			button.text = songs[i].get_display_title()
		else:
			button.visible = false
			button.disabled = true
			button.text = ""


func _ensure_song_buttons(song_count: int) -> void:
	if _list_container == null:
		return

	while song_items.size() < song_count:
		var button := Button.new()
		var index := song_items.size()
		button.name = "SongItem_%03d" % [index + 1]
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.pressed.connect(Callable(self, "_select_song").bind(index))
		_list_container.add_child(button)
		song_items.append(button)


func _show_empty_song_info() -> void:
	current_song = null
	if cover_image != null:
		cover_image.texture = null
		cover_image.visible = false
	if title_label != null:
		title_label.text = tr("Select a Song")
	if info_label != null:
		info_label.text = tr("No song selected.")
	if btn_difficulty != null:
		btn_difficulty.text = tr("Difficulty: Normal")
		btn_difficulty.disabled = true
	if btn_start != null:
		btn_start.text = tr("Start Song")
		btn_start.disabled = true
	if preview_player != null:
		preview_player.stop()


func _select_song(index: int, play_preview: bool = true) -> void:
	if music_repo == null:
		return

	current_song = music_repo.get_song(index)
	if current_song == null:
		return

	if cover_image != null:
		cover_image.texture = current_song.cover_texture
		cover_image.visible = current_song.cover_texture != null
	if title_label != null:
		title_label.text = current_song.song_name
	if info_label != null:
		var info_template := tr("SONG_INFO")
		if info_template == "SONG_INFO":
			info_template = "Composer: %s\nLevel: %s\nChart: %s\n%s"
		info_label.text = info_template % [
			current_song.composer,
			current_song.level,
			current_song.chart_file,
			tr(current_song.description)
		]

	if not current_song.difficulty_labels.is_empty():
		_difficulty_index = clampi(_difficulty_index, 0, current_song.difficulty_labels.size() - 1)
	_update_difficulty_button()
	if btn_difficulty != null:
		btn_difficulty.disabled = false
	if btn_start != null:
		btn_start.disabled = false
	_apply_responsive_layout()
	if play_preview:
		_play_preview()
	elif preview_player != null:
		preview_player.stop()


func _play_preview() -> void:
	if preview_player == null:
		return

	preview_player.stop()
	if current_song != null and current_song.preview_audio != null:
		preview_player.stream = current_song.preview_audio
		preview_player.play()


func _on_btn_difficulty_pressed() -> void:
	if current_song == null or current_song.difficulty_labels.is_empty():
		return

	_difficulty_index = (_difficulty_index + 1) % current_song.difficulty_labels.size()
	_update_difficulty_button()


func _update_difficulty_button() -> void:
	if current_song == null or current_song.difficulty_labels.is_empty():
		btn_difficulty.text = tr("Difficulty: -")
		return

	btn_difficulty.text = tr("Difficulty: %s") % tr(current_song.difficulty_labels[_difficulty_index])


func _on_btn_start_pressed() -> void:
	if current_song == null:
		return

	if preview_player != null:
		preview_player.stop()
	if start_confirmation != null:
		start_confirmation.visible = true


func _on_calibrate_pressed() -> void:
	if current_song == null:
		return
	if start_confirmation != null:
		start_confirmation.visible = false
	var difficulty := _get_current_difficulty()
	if loading_page != null and loading_page.has_method("begin_calibration_loading"):
		visible = false
		loading_page.call("begin_calibration_loading", current_song, difficulty)
		return
	push_warning("Calibration loading is unavailable.")


func _on_start_directly_pressed() -> void:
	if start_confirmation != null:
		start_confirmation.visible = false
	_begin_selected_song()


func _on_exit_start_confirmation_pressed() -> void:
	if start_confirmation != null:
		start_confirmation.visible = false


func _begin_selected_song() -> void:
	if current_song == null:
		return

	var difficulty := _get_current_difficulty()
	if loading_page != null:
		visible = false
		loading_page.call("begin_loading", current_song, difficulty)
		return

	var started: bool = game_ctrl != null and game_ctrl.call("StartSong", current_song, difficulty) == true
	if started:
		visible = false
		game_page.visible = true
	else:
		push_warning("Cannot start song. Check that score.midi exists and is valid.")


func _on_back_pressed() -> void:
	if preview_player != null:
		preview_player.stop()

	visible = false
	menu_page.visible = true


func _get_current_difficulty() -> String:
	if current_song == null or current_song.difficulty_labels.is_empty():
		return "Normal"

	_difficulty_index = clampi(_difficulty_index, 0, current_song.difficulty_labels.size() - 1)
	return current_song.difficulty_labels[_difficulty_index]
