extends Node
class_name AudioCtrl

@export var eval_ctrl: Node
@export var loading_sfx: AudioStream
@export var perfect_sfx: AudioStream
@export var good_sfx: AudioStream
@export var miss_sfx: AudioStream

var _player: AudioStreamPlayer


func _ready() -> void:
	_player = AudioStreamPlayer.new()
	add_child(_player)

	if eval_ctrl != null and eval_ctrl.has_signal("Judgement"):
		eval_ctrl.connect("Judgement", Callable(self, "_on_judgement"))


func play_loading() -> void:
	play_sfx(loading_sfx)


func play_sfx(stream: AudioStream) -> void:
	if stream == null:
		return

	_player.stop()
	_player.stream = stream
	_player.play()


func _on_judgement(label: String, _offset_sec: float, _score_delta: int, _combo: int) -> void:
	match label:
		"Perfect":
			play_sfx(perfect_sfx)
		"Good", "Late":
			play_sfx(good_sfx)
		"Miss", "Bad":
			play_sfx(miss_sfx)
