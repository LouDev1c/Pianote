extends Control

@export var song_list_page: Control
@export var game_page: Control
@export var result_page: Control
@export var setting_page: Control


func _ready() -> void:
	visible = true
	if song_list_page != null:
		song_list_page.visible = false
	if game_page != null:
		game_page.visible = false
	if result_page != null:
		result_page.visible = false
	if setting_page != null:
		setting_page.visible = false


func _btn_start_click() -> void:
	visible = false
	song_list_page.visible = true


func _btn_setting_click() -> void:
	visible = false
	if setting_page.has_method("open_from"):
		setting_page.call("open_from", self)
	else:
		setting_page.visible = true


func _btn_exit_click() -> void:
	get_tree().quit()
