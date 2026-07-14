extends Control

@export var menu_page: Control


func _on_btn_back_pressed() -> void:
	visible = false
	menu_page.visible = true

