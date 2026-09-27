# Pianote Chart Converter

This is a pure .NET 8 class library. It has no Godot dependency and converts a
Standard MIDI File into a timeline of individual piano-note objects.

Each note contains pitch, channel, track, start time, duration, velocity,
left/right hand assignment, and black/white key information. The default hand
assignment groups humanized chord onsets and uses beam-search dynamic
programming to minimize hand span, hand-position movement, and unnecessary hand
crossing. MIDI pitch 60 is now only a weak prior and can be changed through
`ChartConversionOptions.HandSplitPitch`. `FixedSplit` remains available as a
fallback strategy.

The Godot project references this project directly. `GameCtrl` runs conversion
when the loading page opens, keeps the chart in memory, and writes a JSON cache
under Godot's `user://chart_cache` directory. Runtime note bars are spawned only
inside the visible look-ahead window, so every bar remains an independent game
object that can later receive judgement colors and animations.

For standalone inspection, run:

```powershell
dotnet run --project .\cli -- <score.midi> [output.json] [hand-split-pitch]
```
