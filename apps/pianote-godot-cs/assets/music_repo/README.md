# Music Repository

Each song should live in its own folder.

```text
assets/music_repo/Song_001/
  preview.wav
  score.midi
  cover.jpg
```

`preview.wav` is used by the song selection page. `score.midi` is converted by
the sibling `pianote-chart-converter` project when the loading page opens.

Godot creates `preview.wav.import` automatically. Standard MIDI files are read
as raw data by the C# converter and intentionally do not use a fabricated
`.midi.import` file. When creating an export preset, include `*.midi` in the
non-resource export filter so the raw score is packed with the game.
