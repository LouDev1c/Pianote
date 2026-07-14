extends Node
class_name MusicRepo


func get_song_count() -> int:
	return get_songs().size()


func get_song(index: int) -> SongData:
	var songs := get_songs()
	if index < 0 or index >= songs.size():
		return null

	return songs[index]


func get_songs() -> Array[SongData]:
	var songs: Array[SongData] = []
	for child in get_children():
		if child is SongData:
			songs.append(child)

	return songs
