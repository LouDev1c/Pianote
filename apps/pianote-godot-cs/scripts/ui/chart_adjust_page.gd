extends Control

@export var setting_page: Control


func _on_btn_back_pressed() -> void:
	visible = false
	setting_page.visible = true

