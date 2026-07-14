extends Node
class_name SongData

@export var song_id: String = ""
@export var song_name: String = "Untitled"
@export var composer: String = "Unknown"
@export var level: String = "1"
@export var difficulty_labels: Array[String] = ["Easy", "Normal", "Hard"]
@export var cover_texture: Texture2D
@export var preview_audio: AudioStream
@export_file("*.mid", "*.midi") var chart_file: String = ""
@export_multiline var description: String = ""


func get_display_title() -> String:
	if composer.is_empty():
		return song_name

	return "%s - %s" % [song_name, composer]
