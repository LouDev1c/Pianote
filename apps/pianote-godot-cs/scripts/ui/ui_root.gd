extends CanvasLayer
class_name UIRoot

@export var initial_page: Control
const SETTINGS_PATH := "user://settings.cfg"


func _enter_tree() -> void:
	_load_saved_locale()


func _ready() -> void:
	if initial_page != null:
		show_page(initial_page)


func _load_saved_locale() -> void:
	var config := ConfigFile.new()
	if config.load(SETTINGS_PATH) != OK:
		TranslationServer.set_locale("en")
		return
	var locale := str(config.get_value("display", "language", "en"))
	TranslationServer.set_locale("zh_CN" if locale.begins_with("zh") else "en")


func show_page(page: Control) -> void:
	for child in get_children():
		if child is Control:
			child.visible = child == page


func show_page_by_name(page_name: StringName) -> void:
	var page := get_node_or_null(NodePath(str(page_name)))
	if page is Control:
		show_page(page)
