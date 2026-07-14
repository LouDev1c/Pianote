extends Control

@export var menu_page: Control
@export var chart_adjust_page: Control
@export var resolution_dropdown: OptionButton
@export var volume_slider: HSlider
@export var language_dropdown: OptionButton
@export var btn_chart_adjust: Button
@export var resize_window_while_running_in_editor := false

const RESOLUTION_OPTIONS: Array[Vector2i] = [
	Vector2i(1280, 720),
	Vector2i(1366, 768),
	Vector2i(1600, 900),
	Vector2i(1920, 720),
	Vector2i(1920, 1080),
	Vector2i(2560, 1440)
]
const SETTINGS_PATH := "user://settings.cfg"
const SUPPORTED_LOCALES := ["en", "zh_CN"]

var _setting_container: VBoxContainer
var _title_label: Label
var _row_nodes: Array[HBoxContainer] = []
var _back_button: Button
var _return_page: Control


func _ready() -> void:
	visible = false
	_cache_layout_nodes()
	_populate_resolution_dropdown()
	if chart_adjust_page != null:
		chart_adjust_page.visible = false
	if resolution_dropdown != null:
		resolution_dropdown.selected = _find_current_resolution_index()
	if language_dropdown != null:
		language_dropdown.selected = 1 if TranslationServer.get_locale().begins_with("zh") else 0
	resized.connect(Callable(self, "_apply_responsive_layout"))
	call_deferred("_apply_responsive_layout")


func _cache_layout_nodes() -> void:
	_setting_container = get_node_or_null("SettingContainer") as VBoxContainer
	_title_label = get_node_or_null("SettingContainer/TitleText") as Label
	_back_button = get_node_or_null("SettingContainer/Btn_Back") as Button
	_row_nodes = []

	var row_paths := [
		"SettingContainer/GraphicsGroup",
		"SettingContainer/AudioGroup",
		"SettingContainer/LanguageGroup",
		"SettingContainer/ChartAdjustGroup"
	]
	for path in row_paths:
		var row := get_node_or_null(path) as HBoxContainer
		if row != null:
			_row_nodes.append(row)


func _populate_resolution_dropdown() -> void:
	if resolution_dropdown == null:
		return

	resolution_dropdown.clear()
	for i in range(RESOLUTION_OPTIONS.size()):
		var resolution := RESOLUTION_OPTIONS[i]
		resolution_dropdown.add_item("%dx%d" % [resolution.x, resolution.y], i)


func _find_current_resolution_index() -> int:
	var current_size := DisplayServer.window_get_size()
	for i in range(RESOLUTION_OPTIONS.size()):
		if RESOLUTION_OPTIONS[i] == current_size:
			return i

	return 0


func _apply_responsive_layout() -> void:
	if _setting_container == null:
		return

	var page_size := size
	if page_size.x <= 1.0 or page_size.y <= 1.0:
		page_size = get_viewport_rect().size

	var page_width := maxf(page_size.x, 640.0)
	var page_height := maxf(page_size.y, 360.0)
	var panel_width := clampf(page_width * 0.52, 520.0, 760.0)
	var panel_height := minf(page_height * 0.78, 520.0)
	var row_height := clampf(page_height * 0.065, 44.0, 58.0)
	var label_width := clampf(panel_width * 0.30, 140.0, 190.0)
	var control_width := maxf(260.0, panel_width - label_width - 28.0)

	_setting_container.position = Vector2((page_width - panel_width) * 0.5, (page_height - panel_height) * 0.5)
	_setting_container.size = Vector2(panel_width, panel_height)
	_setting_container.custom_minimum_size = Vector2(panel_width, panel_height)

	if _title_label != null:
		_title_label.custom_minimum_size = Vector2(panel_width, clampf(page_height * 0.10, 58.0, 82.0))

	for row in _row_nodes:
		row.custom_minimum_size = Vector2(panel_width, row_height)
		if row.get_child_count() >= 2:
			var label := row.get_child(0) as Control
			var control := row.get_child(1) as Control
			if label != null:
				label.custom_minimum_size = Vector2(label_width, row_height)
			if control != null:
				control.custom_minimum_size = Vector2(control_width, row_height)

	if _back_button != null:
		_back_button.custom_minimum_size = Vector2(panel_width, clampf(page_height * 0.072, 48.0, 64.0))


func open_from(return_page: Control) -> void:
	_return_page = return_page
	visible = true
	call_deferred("_apply_responsive_layout")


func _on_btn_back_pressed() -> void:
	var target := _return_page
	_return_page = null
	visible = false
	if target != null:
		target.visible = true
		if target.has_method("on_return_from_settings"):
			target.call("on_return_from_settings")
	else:
		menu_page.visible = true


func _on_btn_chart_adjust_pressed() -> void:
	visible = false
	chart_adjust_page.visible = true


func _on_volume_slider_value_changed(value: float) -> void:
	AudioServer.set_bus_volume_db(AudioServer.get_bus_index("Master"), linear_to_db(value / 100.0))


func _on_resolution_dropdown_item_selected(index: int) -> void:
	if index < 0 or index >= RESOLUTION_OPTIONS.size():
		return

	var resolution := RESOLUTION_OPTIONS[index]
	_store_resolution_override(resolution)
	_apply_virtual_resolution(resolution)

	if OS.has_feature("editor") and not resize_window_while_running_in_editor:
		push_warning("Godot editor embedded game windows cannot be resized. Disable game embedding, then enable resize_window_while_running_in_editor on SettingPage if you want editor-run windows to resize.")
		call_deferred("_apply_responsive_layout")
		return

	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
	DisplayServer.window_set_size(resolution)
	_center_window(resolution)
	call_deferred("_apply_responsive_layout")


func _store_resolution_override(resolution: Vector2i) -> void:
	ProjectSettings.set_setting("display/window/size/window_width_override", resolution.x)
	ProjectSettings.set_setting("display/window/size/window_height_override", resolution.y)


func _apply_virtual_resolution(resolution: Vector2i) -> void:
	var window := get_window()
	if window == null:
		return

	for property in window.get_property_list():
		if property.get("name", "") == "content_scale_size":
			window.set("content_scale_size", resolution)
			return


func _center_window(resolution: Vector2i) -> void:
	var screen := DisplayServer.window_get_current_screen()
	var screen_position := DisplayServer.screen_get_position(screen)
	var screen_size := DisplayServer.screen_get_size(screen)
	var centered_position := screen_position + (screen_size - resolution) / 2
	DisplayServer.window_set_position(centered_position)


func _on_language_dropdown_item_selected(index: int) -> void:
	if index < 0 or index >= SUPPORTED_LOCALES.size():
		return
	var locale: String = SUPPORTED_LOCALES[index]
	TranslationServer.set_locale(locale)
	var config := ConfigFile.new()
	config.load(SETTINGS_PATH)
	config.set_value("display", "language", locale)
	config.save(SETTINGS_PATH)
