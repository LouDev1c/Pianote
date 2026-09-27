# Pianote Manual Setup

1. Put each performance in its song folder as `preview.wav` and `score.midi`. Godot imports WAV automatically; MIDI is read as raw bytes by the C# chart-converter project in `modules/pianote-chart-converter`.
2. In Godot, open `main.tscn` and select each `MusicRepo/Song_xxx` node.
3. Confirm that `preview_audio`, `cover_texture`, and `chart_file` point to that song folder.
4. Add sound effects to `assets/sfx`, then assign them on the `AudioCtrl` node.
5. For real piano input, connect your audio-to-MIDI module to `InputCtrl.PushDetectedNote(pitch, velocity)`. If you call `InputCtrl.PushDetectedNoteAtTime(pitch, velocity, timestampSec)`, pass the same absolute clock format used by `Time.GetTicksUsec() / 1_000_000.0`.
6. The debug keyboard is enabled by default: A W S E D F T G Y H U J K emits MIDI notes 60-72.
7. Resolution changes are handled by `SettingPage/SettingContainer/GraphicsGroup/ResolutionDropdown`. No extra binding is needed unless you rename the setting or song-list UI nodes in `main.tscn`.
8. Godot's embedded game view cannot resize or move its native window. To test real window resizing from the editor, disable the editor's game embedding option, then enable `resize_window_while_running_in_editor` on `UIRoot/SettingPage`. Exported builds resize normally.
9. For exported builds, add `*.midi` to the export preset's non-resource include filter. The editor does not generate `.midi.import` because Standard MIDI File is not a built-in Godot audio resource.

## Hand assignment tuning

`GameCtrl` uses contextual hand assignment by default. Notes within 45 ms are
treated as one humanized chord, then a beam-search dynamic program minimizes
single-hand span, rapid hand-position movement, and unnecessary crossings.

- `ComfortableHandSpanSemitones`: 12 by default. Try 10-11 for smaller hands.
- `MaximumHandSpanSemitones`: 17 by default. Values around 15-17 are practical.
- `ChordOnsetToleranceMs`: 45 by default. Try 30-60 for differently humanized MIDI.
- `HandAssignmentBeamWidth`: 64 by default. Raise to 96 for complex music if loading time is acceptable.
- `AllowHandCrossing`: keep enabled for repertoire with hand crossings.
- `UseContextualHandAssignment`: disable only to compare with the old fixed middle-C split.

MIDI-only hand assignment is an inference. For exact colors, use a score exported
with separate left/right-hand tracks or add explicit hand annotations before
conversion.
