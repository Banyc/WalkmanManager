﻿Imports System.Collections.ObjectModel
Imports WalkmanManager.Database

Public Class Searching
	Public Shared Function SearchSongs(lst As ObservableCollection(Of SongInfo), str As String) _
		As ObservableCollection(Of SongInfo)
		Dim result As New ObservableCollection(Of SongInfo)
		str = str.ToLower()
		'Full Match
		'Song Title First
		Dim matches = From itm In lst Where itm.Title.ToLower = str Select itm
		For Each songInfo As SongInfo In matches
			result.Add(songInfo)
		Next
		'Artist Second(Full Match At least One)
		matches = From itm In lst Where itm.Artists.Split("/").AllToLower().Contains(str) Select itm
		For Each songInfo As SongInfo In matches
			result.Add(songInfo)
		Next
		'WordMatch
		matches =
			From itm In lst Where itm.Title.Split(" ").AllToLower().Contains(str) And Not result.Contains(itm) Select itm
		For Each songInfo As SongInfo In matches
			result.Add(songInfo)
		Next
		matches =
			From itm In lst Where itm.Artists.Split(" ").AllToLower().Contains(str) And Not result.Contains(itm) Select itm
		For Each songInfo As SongInfo In matches
			result.Add(songInfo)
		Next
		'Partial Matches
		matches = From itm In lst Where itm.Title.ToLower.Contains(str) And Not result.Contains(itm) Select itm
		For Each songInfo As SongInfo In matches
			result.Add(songInfo)
		Next
		matches = From itm In lst Where itm.Artists.ToLower.Contains(str) And Not result.Contains(itm) Select itm
		For Each songInfo As SongInfo In matches
			result.Add(songInfo)
		Next

		If result.Count = 0 Then
			'Letter Matches
			matches =
				From itm In lst Where itm.Title.ToLower().Contains(str.ToStringArray()) And Not result.Contains(itm) Select itm
			For Each songInfo As SongInfo In matches
				result.Add(songInfo)
			Next
			matches =
				From itm In lst Where itm.Artists.ToLower().Contains(str.ToStringArray()) And Not result.Contains(itm) Select itm
			For Each songInfo As SongInfo In matches
				result.Add(songInfo)
			Next
		End If

		Return result
	End Function
End Class
