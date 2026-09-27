class_name AudioCaptureClient
extends RefCounted

const MODULE_FOLDER := "pianote-audio-capture"
const PROJECT_FILE := "Pianote.AudioCapture.csproj"
const STATUS_POLL_SECONDS := 0.05

var module_path: String
var project_path: String
var sample_path: String


func _init() -> void:
	var godot_project_path := ProjectSettings.globalize_path("res://").trim_suffix("/").trim_suffix("\\")
	module_path = godot_project_path.get_base_dir().path_join(MODULE_FOLDER).simplify_path()
	project_path = module_path.path_join(PROJECT_FILE)
	sample_path = module_path.path_join("audio-sample")


func validate_module() -> Dictionary:
	if not FileAccess.file_exists(project_path):
		return {
			"ok": false,
			"error": "Audio capture module was not found: %s" % project_path,
		}
	return {"ok": true}


func get_sample_file(file_name: String) -> String:
	return sample_path.path_join(file_name.validate_filename())


func create_event_path(event_name: String) -> String:
	return ProjectSettings.globalize_path(
		"user://audio-capture-%s-%d-%d.json" % [
			event_name.validate_filename(),
			Time.get_ticks_msec(),
			randi(),
		]
	)


func start_action(action: String, arguments: PackedStringArray = PackedStringArray()) -> Dictionary:
	var validation := validate_module()
	if validation.get("ok", false) != true:
		return validation

	var status_path := create_event_path("complete")
	if FileAccess.file_exists(status_path):
		DirAccess.remove_absolute(status_path)

	var command_arguments := PackedStringArray(["run", "--no-restore", "--project", project_path, "--", action])
	command_arguments.append_array(arguments)
	command_arguments.append_array(PackedStringArray(["--status", status_path]))

	var configured_executable := str(ProjectSettings.get_setting(
		"pianote/audio_capture_dotnet_executable", "dotnet"
	))
	var candidates := PackedStringArray([configured_executable])
	if not candidates.has("dotnet"):
		candidates.append("dotnet")

	for executable in candidates:
		var process_id := OS.create_process(executable, command_arguments)
		if process_id > 0:
			return {
				"ok": true,
				"pid": process_id,
				"status_path": status_path,
				"executable": executable,
			}

	return {
		"ok": false,
		"error": "dotnet could not be started. Configure pianote/audio_capture_dotnet_executable.",
	}


func wait_for_action(tree: SceneTree, handle: Dictionary, timeout_seconds: float) -> Dictionary:
	if handle.get("ok", false) != true:
		return handle

	var process_id := int(handle.get("pid", -1))
	var status_path := str(handle.get("status_path", ""))
	var elapsed := 0.0
	var process_finished_without_status := false

	while elapsed < timeout_seconds:
		if FileAccess.file_exists(status_path):
			var status_text := FileAccess.get_file_as_string(status_path)
			DirAccess.remove_absolute(status_path)
			var parsed: Variant = JSON.parse_string(status_text)
			if parsed is Dictionary:
				return parsed
			return {"ok": false, "error": "The recorder returned an invalid status."}

		if process_id > 0 and not OS.is_process_running(process_id):
			if process_finished_without_status:
				break
			process_finished_without_status = true

		await tree.create_timer(STATUS_POLL_SECONDS).timeout
		elapsed += STATUS_POLL_SECONDS

	if process_id > 0 and OS.is_process_running(process_id):
		OS.kill(process_id)
	if FileAccess.file_exists(status_path):
		DirAccess.remove_absolute(status_path)
	return {
		"ok": false,
		"error": "The recorder did not finish successfully within %.1f seconds." % timeout_seconds,
	}


func run_action(
	tree: SceneTree,
	action: String,
	arguments: PackedStringArray = PackedStringArray(),
	timeout_seconds: float = 15.0
) -> Dictionary:
	var handle := start_action(action, arguments)
	return await wait_for_action(tree, handle, timeout_seconds)


func wait_for_event(
	tree: SceneTree,
	event_path: String,
	handle: Dictionary,
	timeout_seconds: float
) -> Dictionary:
	if handle.get("ok", false) != true:
		return handle

	var process_id := int(handle.get("pid", -1))
	var elapsed := 0.0
	while elapsed < timeout_seconds:
		if FileAccess.file_exists(event_path):
			var event_text := FileAccess.get_file_as_string(event_path)
			DirAccess.remove_absolute(event_path)
			var parsed: Variant = JSON.parse_string(event_text)
			if parsed is Dictionary:
				return parsed
			return {"ok": false, "error": "The recorder returned an invalid event."}
		if process_id > 0 and not OS.is_process_running(process_id):
			break
		await tree.create_timer(STATUS_POLL_SECONDS).timeout
		elapsed += STATUS_POLL_SECONDS

	return {
		"ok": false,
		"error": "The microphone stream did not become ready within %.1f seconds." % timeout_seconds,
	}


func discard_event(event_path: String) -> void:
	if not event_path.is_empty() and FileAccess.file_exists(event_path):
		DirAccess.remove_absolute(event_path)


func cancel_action(handle: Dictionary) -> void:
	var process_id := int(handle.get("pid", -1))
	if process_id > 0 and OS.is_process_running(process_id):
		OS.kill(process_id)
	var status_path := str(handle.get("status_path", ""))
	if not status_path.is_empty() and FileAccess.file_exists(status_path):
		DirAccess.remove_absolute(status_path)
