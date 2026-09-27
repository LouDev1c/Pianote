extends Control

@export var song_list_page: Control
@export var loading_page: Control
@export var title_label: Label
@export var status_label: Label
@export var detail_label: Label
@export var calibration_background: ColorRect
@export var recording_indicator: Label
@export var btn_retry: Button
@export var btn_back: Button
@export var countdown_label: Label
@export var waterfall_area: Control
@export var falling_note: ColorRect
@export var hit_line: ColorRect
@export var key_label: Label

@export_range(0.1, 2.0, 0.05) var countdown_step_seconds := 1.0

const ENVIRONMENT_INSTRUCTION_SECONDS := 3
const ENVIRONMENT_PREPARE_SECONDS := 3
const ENVIRONMENT_RECORD_SECONDS := 5
const PIANO_INSTRUCTION_SECONDS := 3
const NOTE_PREPARE_SECONDS := 2
const NOTE_RECORD_SECONDS := 7
const COMPLETION_SECONDS := 3
const CALIBRATION_RECORD_SECONDS := 3.0
const NOTE_HIT_SECONDS := 1.0
const CALIBRATION_TOLERANCE_SECONDS := 0.08
const MAX_CALIBRATION_ATTEMPTS := 6
const NOTE_SAMPLES := [
	{"note": "C4", "file": "c4.wav"},
	{"note": "E4", "file": "e4.wav"},
	{"note": "G4", "file": "g4.wav"},
	{"note": "C5", "file": "c5.wav"},
]
const INSTRUCTION_BACKGROUND := Color(0.025, 0.04, 0.075, 1.0)
const RECORDING_BACKGROUND := Color(0.16, 0.16, 0.16, 1.0)

var _audio_capture := AudioCaptureClient.new()
var _song: SongData
var _difficulty := "Normal"
var _running := false
var _cancelled := false
var _active_handle: Dictionary = {}
var _active_event_path := ""
var _recording_delay_seconds := 0.0
var _animation_tween: Tween


func _ready() -> void:
	visible = false
	if countdown_label != null:
		countdown_label.visible = false
	if recording_indicator != null:
		recording_indicator.visible = false
	if btn_retry != null:
		btn_retry.visible = false
	_set_waterfall_visible(false)
	resized.connect(Callable(self, "_apply_responsive_layout"))
	call_deferred("_apply_responsive_layout")


func begin_calibration(song: SongData, difficulty: String) -> void:
	if song == null:
		_show_error(tr("The selected song is unavailable."))
		return

	_song = song
	_difficulty = difficulty
	_cancelled = false
	_running = true
	_active_handle = {}
	_active_event_path = ""
	_recording_delay_seconds = 0.0
	visible = true
	title_label.text = tr("Audio Calibration")
	status_label.text = tr("Requesting microphone access")
	detail_label.text = tr("Allow microphone access when your operating system asks.")
	btn_retry.visible = false
	btn_back.visible = true
	btn_back.text = tr("Cancel")
	_set_instruction_mode()
	_set_waterfall_visible(false)
	countdown_label.visible = false
	call_deferred("_run_calibration")


func _run_calibration() -> void:
	_active_handle = _audio_capture.start_action("probe")
	if _active_handle.get("ok", false) != true:
		_show_error(str(_active_handle.get("error", tr("Unable to start the recorder."))))
		return
	var probe_result := await _audio_capture.wait_for_action(get_tree(), _active_handle, 12.0)
	_active_handle = {}
	if _stop_if_cancelled():
		return
	if probe_result.get("ok", false) != true:
		_show_error(tr("Microphone access failed: %s") % str(probe_result.get("error", tr("Unknown error"))))
		return

	status_label.text = tr("Microphone ready")
	detail_label.text = tr("Microphone access was granted.")
	await _show_timed_message(
		tr("Environment sample"),
		tr("Next, 5 seconds of ambient sound will be recorded. Do not play any piano keys."),
		ENVIRONMENT_INSTRUCTION_SECONDS
	)
	if _stop_if_cancelled():
		return

	await _show_countdown(
		ENVIRONMENT_PREPARE_SECONDS,
		tr("Ready to record ambient sound"),
		tr("Recording starts when the countdown reaches zero.")
	)
	if _stop_if_cancelled():
		return

	var environment_saved := await _record_plain_sample(
		"env.wav",
		ENVIRONMENT_RECORD_SECONDS,
		tr("Recording ambient sound")
	)
	if not environment_saved or _stop_if_cancelled():
		return

	var calibrated := await _run_latency_calibration()
	if not calibrated or _stop_if_cancelled():
		return

	await _show_timed_message(
		tr("Piano samples"),
		tr("Next, piano audio will be recorded. Play each key when instructed."),
		PIANO_INSTRUCTION_SECONDS
	)
	if _stop_if_cancelled():
		return

	for sample in NOTE_SAMPLES:
		var note := str(sample["note"])
		await _show_note_prepare(note)
		if _stop_if_cancelled():
			return

		var note_saved := await _record_guided_note_sample(note, str(sample["file"]))
		if not note_saved or _stop_if_cancelled():
			return

	await _show_timed_message(
		tr("Calibration complete"),
		tr("Calibration is complete. The game will start shortly."),
		COMPLETION_SECONDS
	)
	if _stop_if_cancelled():
		return

	_running = false
	visible = false
	if loading_page != null:
		loading_page.call("begin_loading", _song, _difficulty)


func _run_latency_calibration() -> bool:
	var attempts := 0
	var previous_delay := -1.0
	while attempts < MAX_CALIBRATION_ATTEMPTS:
		attempts += 1
		var result := await _record_latency_attempt()
		if result.get("ok", false) != true:
			_show_error(tr("Latency calibration failed: %s") % str(result.get("error", tr("Unknown error"))))
			return false

		var detected_delay := float(result.get("delay_seconds", 0.0))
		_recording_delay_seconds = detected_delay

		status_label.text = tr("Calibrating")
		detail_label.text = tr("Detected microphone delay: %.3f seconds.") % detected_delay
		await get_tree().create_timer(1.0).timeout
		if _stop_if_cancelled():
			return false

		if previous_delay >= 0.0 and absf(detected_delay - previous_delay) <= CALIBRATION_TOLERANCE_SECONDS:
			await _show_timed_message(
				tr("Calibration successful"),
				tr("The microphone delay is stable. Piano sample recording will begin."),
				COMPLETION_SECONDS
			)
			return true

		previous_delay = detected_delay
		await _show_timed_message(
			tr("Calibration check"),
			tr("Calibration was attempted. Repeat once to verify accuracy."),
			2
		)
		if _stop_if_cancelled():
			return false

	await _show_timed_message(
		tr("Calibration check"),
		tr("More calibration is needed. Please try the falling note again."),
		2
	)
	return await _run_latency_calibration()


func _record_latency_attempt() -> Dictionary:
	_set_instruction_mode()
	_set_waterfall_visible(true)
	_highlight_key("")
	status_label.text = tr("Press any key when the note touches the hit line")
	detail_label.text = tr("The falling note shows the expected key press moment.")
	countdown_label.visible = false

	var ready_path := _audio_capture.create_event_path("calibration-ready")
	var output_path := _audio_capture.get_sample_file("latency-check.wav")
	var arguments := PackedStringArray([
		"--duration", str(CALIBRATION_RECORD_SECONDS),
		"--expected", str(NOTE_HIT_SECONDS),
		"--output", output_path,
		"--ready-status", ready_path,
	])
	_active_event_path = ready_path
	_active_handle = _audio_capture.start_action("calibrate", arguments)
	if _active_handle.get("ok", false) != true:
		return _active_handle

	var ready_result := await _audio_capture.wait_for_event(get_tree(), ready_path, _active_handle, 6.0)
	_audio_capture.discard_event(ready_path)
	_active_event_path = ""
	if ready_result.get("ok", false) != true:
		_audio_capture.cancel_action(_active_handle)
		_active_handle = {}
		return ready_result

	_set_recording_mode()
	_start_falling_note_animation(CALIBRATION_RECORD_SECONDS, NOTE_HIT_SECONDS)
	status_label.text = tr("Press any key when the note touches the hit line")
	detail_label.text = tr("Recording calibration audio")
	await _show_countdown(int(ceil(CALIBRATION_RECORD_SECONDS)), status_label.text, detail_label.text, true, false)

	_set_instruction_mode()
	status_label.text = tr("Recording finished")
	detail_label.text = tr("Calibrating")
	var result := await _audio_capture.wait_for_action(get_tree(), _active_handle, 4.0)
	_active_handle = {}
	_stop_falling_note_animation()
	return result


func _record_guided_note_sample(note: String, file_name: String) -> bool:
	_set_instruction_mode()
	_set_waterfall_visible(true)
	_highlight_key(note)
	status_label.text = tr("Press the %s key when the note touches the hit line") % note
	detail_label.text = tr("Recording will follow the calibrated microphone delay.")
	countdown_label.visible = false
	_active_event_path = _audio_capture.create_event_path("note-recording-ready")
	var arguments := PackedStringArray([
		"--output", _audio_capture.get_sample_file(file_name),
		"--duration", str(NOTE_RECORD_SECONDS),
		"--start-delay", str(_recording_delay_seconds),
		"--ready-status", _active_event_path,
	])
	_active_handle = _audio_capture.start_action("record", arguments)
	if _active_handle.get("ok", false) != true:
		_show_error(str(_active_handle.get("error", tr("Unable to start the recorder."))))
		return false

	_start_falling_note_animation(float(NOTE_RECORD_SECONDS) + _recording_delay_seconds, NOTE_HIT_SECONDS)
	status_label.text = tr("Recording the %s key") % note
	detail_label.text = tr("Get ready. Recording starts after the calibrated delay.")
	var ready_event_path := _active_event_path
	var ready_result := await _audio_capture.wait_for_event(get_tree(), ready_event_path, _active_handle, 6.0)
	_audio_capture.discard_event(ready_event_path)
	_active_event_path = ""
	if ready_result.get("ok", false) != true:
		_audio_capture.cancel_action(_active_handle)
		_active_handle = {}
		_show_error(tr("Recording failed: %s") % str(ready_result.get("error", tr("Unknown error"))))
		return false

	_set_recording_mode()
	detail_label.text = tr("Keep the %s key sounding until the countdown ends.") % note
	await _show_countdown(NOTE_RECORD_SECONDS, status_label.text, detail_label.text, true, false)
	if _cancelled:
		_audio_capture.cancel_action(_active_handle)
		_active_handle = {}
		return false

	_set_instruction_mode()
	status_label.text = tr("Recording finished")
	detail_label.text = tr("Saving the audio sample...")
	var result := await _audio_capture.wait_for_action(get_tree(), _active_handle, 4.0)
	_active_handle = {}
	_stop_falling_note_animation()
	if result.get("ok", false) != true:
		_show_error(tr("Recording failed: %s") % str(result.get("error", tr("Unknown error"))))
		return false

	detail_label.text = tr("Saved %s") % str(result.get("output", file_name))
	return true


func _record_plain_sample(file_name: String, seconds: int, prompt: String) -> bool:
	_set_instruction_mode()
	_set_waterfall_visible(false)
	status_label.text = tr("Preparing the recorder")
	detail_label.text = tr("The countdown will start when the microphone is ready.")
	countdown_label.visible = false
	_active_event_path = _audio_capture.create_event_path("recording-ready")
	var arguments := PackedStringArray([
		"--output", _audio_capture.get_sample_file(file_name),
		"--duration", str(seconds),
		"--ready-status", _active_event_path,
	])
	_active_handle = _audio_capture.start_action("record", arguments)
	if _active_handle.get("ok", false) != true:
		_show_error(str(_active_handle.get("error", tr("Unable to start the recorder."))))
		return false

	var ready_event_path := _active_event_path
	var ready_result := await _audio_capture.wait_for_event(get_tree(), ready_event_path, _active_handle, 6.0)
	_audio_capture.discard_event(ready_event_path)
	_active_event_path = ""
	if _cancelled:
		_audio_capture.cancel_action(_active_handle)
		_active_handle = {}
		return false
	if ready_result.get("ok", false) != true:
		var process_result := await _audio_capture.wait_for_action(get_tree(), _active_handle, 0.5)
		_active_handle = {}
		var error_message := str(process_result.get("error", ready_result.get("error", tr("Unknown error"))))
		_show_error(tr("Recording failed: %s") % error_message)
		return false

	await _show_countdown(seconds, prompt, tr("The microphone is recording now."), true)
	if _cancelled:
		_audio_capture.cancel_action(_active_handle)
		_active_handle = {}
		return false

	_set_instruction_mode()
	status_label.text = tr("Recording finished")
	detail_label.text = tr("Saving the audio sample...")
	var result := await _audio_capture.wait_for_action(get_tree(), _active_handle, 4.0)
	_active_handle = {}
	if result.get("ok", false) != true:
		_show_error(tr("Recording failed: %s") % str(result.get("error", tr("Unknown error"))))
		return false

	detail_label.text = tr("Saved %s") % str(result.get("output", file_name))
	return true


func _show_note_prepare(note: String) -> void:
	_set_instruction_mode()
	_set_waterfall_visible(true)
	_highlight_key(note)
	await _show_countdown(
		NOTE_PREPARE_SECONDS,
		tr("Press the %s key when the note touches the hit line") % note,
		tr("Recording starts after the calibrated delay.")
	)


func _show_timed_message(heading: String, message: String, seconds: int) -> void:
	_set_waterfall_visible(false)
	await _show_countdown(seconds, heading, message)


func _show_countdown(seconds: int, prompt: String, detail: String = "", recording: bool = false, update_text: bool = true) -> void:
	if recording:
		_set_recording_mode()
	else:
		_set_instruction_mode()
	if update_text:
		status_label.text = prompt
		detail_label.text = detail
	countdown_label.visible = true
	for count in range(seconds, 0, -1):
		if _cancelled:
			break
		countdown_label.text = str(count)
		await get_tree().create_timer(countdown_step_seconds).timeout
	countdown_label.visible = false


func _set_instruction_mode() -> void:
	if calibration_background != null:
		calibration_background.color = INSTRUCTION_BACKGROUND
	if recording_indicator != null:
		recording_indicator.visible = false


func _set_recording_mode() -> void:
	if calibration_background != null:
		calibration_background.color = RECORDING_BACKGROUND
	if recording_indicator != null:
		recording_indicator.text = "● %s" % tr("RECORDING")
		recording_indicator.visible = true


func _set_waterfall_visible(show: bool) -> void:
	if waterfall_area != null:
		waterfall_area.visible = show
	if key_label != null:
		key_label.visible = show
	if not show:
		_stop_falling_note_animation()


func _highlight_key(note: String) -> void:
	if key_label == null:
		return
	key_label.text = note
	key_label.visible = not note.is_empty()


func _start_falling_note_animation(duration_seconds: float, hit_seconds: float) -> void:
	if falling_note == null or hit_line == null or waterfall_area == null:
		return
	_stop_falling_note_animation()
	var top_y := 12.0
	var hit_y := hit_line.position.y - falling_note.size.y
	var end_y := waterfall_area.size.y - falling_note.size.y - 12.0
	var pre_hit_distance := maxf(1.0, hit_y - top_y)
	var total_distance := pre_hit_distance * duration_seconds / hit_seconds
	end_y = minf(end_y, top_y + total_distance)
	falling_note.position = Vector2((waterfall_area.size.x - falling_note.size.x) * 0.5, top_y)
	_animation_tween = create_tween()
	_animation_tween.tween_property(falling_note, "position:y", end_y, duration_seconds)


func _stop_falling_note_animation() -> void:
	if _animation_tween != null:
		_animation_tween.kill()
	_animation_tween = null
	if falling_note != null:
		falling_note.position.y = 12.0


func _show_error(message: String) -> void:
	_running = false
	_active_handle = {}
	_audio_capture.discard_event(_active_event_path)
	_active_event_path = ""
	_set_instruction_mode()
	_set_waterfall_visible(false)
	countdown_label.visible = false
	visible = true
	title_label.text = tr("Audio Calibration")
	status_label.text = tr("Calibration could not continue")
	detail_label.text = message
	btn_retry.visible = _song != null
	btn_back.visible = true
	btn_back.text = tr("Back to Song List")


func _stop_if_cancelled() -> bool:
	return _cancelled or not is_inside_tree()


func _on_btn_retry_pressed() -> void:
	if _running or _song == null:
		return
	begin_calibration(_song, _difficulty)


func _on_btn_back_pressed() -> void:
	_cancelled = true
	_running = false
	if not _active_handle.is_empty():
		_audio_capture.cancel_action(_active_handle)
		_active_handle = {}
	_audio_capture.discard_event(_active_event_path)
	_active_event_path = ""
	countdown_label.visible = false
	_set_instruction_mode()
	_set_waterfall_visible(false)
	visible = false
	if song_list_page != null:
		song_list_page.visible = true


func _apply_responsive_layout() -> void:
	var page_size := size
	if page_size.x <= 1.0 or page_size.y <= 1.0:
		page_size = get_viewport_rect().size

	var content := get_node_or_null("CalibrationContent") as VBoxContainer
	if content != null:
		var content_size := Vector2(
			clampf(page_size.x * 0.62, 520.0, 820.0),
			clampf(page_size.y * 0.78, 540.0, 680.0)
		)
		content.position = (page_size - content_size) * 0.5
		content.size = content_size

	if waterfall_area != null:
		waterfall_area.custom_minimum_size = Vector2(0.0, clampf(page_size.y * 0.26, 150.0, 220.0))
		falling_note.size = Vector2(clampf(page_size.x * 0.07, 58.0, 86.0), 34.0)
		falling_note.position.x = (waterfall_area.size.x - falling_note.size.x) * 0.5
		if hit_line != null:
			hit_line.position = Vector2(0.0, waterfall_area.custom_minimum_size.y * 0.68)
			hit_line.size = Vector2(maxf(1.0, waterfall_area.size.x), 5.0)
