﻿Imports System.Collections.ObjectModel
Imports System.Threading
Imports System.Windows.Shell
Imports MaterialDesignThemes.Wpf
Imports WalkmanManager.Database
Imports ATL
Imports GongSolutions.Wpf.DragDrop

Class MainWindow
	ReadOnly NETEASE_RED = Media.Color.FromRgb(198, 47, 47)
	ReadOnly DEFAULT_COLOR = Media.Color.FromRgb(103, 58, 183)

	Dim _lstSongs As ObservableCollection(Of SongInfo)
	Dim _isRightClickSelect As Boolean = False
	Dim _isCloudMusicLoggedIn As Boolean = False
	Dim _cloudMusic As New CloudMusic
	Dim _syncRemoteDeviceContent As Object
	Dim _flgSyncStop As Boolean
	Dim _flgUSBRefreshPause As Boolean = False
	Dim _encryptKey As String

	Private Sub czTitle_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) _
		Handles CzTitle.MouseLeftButtonDown
		Try
			DragMove()
		Catch ex As Exception

		End Try
	End Sub

	Private Sub btn_window_minimize_Click(sender As Object, e As RoutedEventArgs) Handles BtnWindowMinimize.Click
		SystemCommands.MinimizeWindow(Me)
	End Sub

	Private Sub btn_window_close_Click(sender As Object, e As RoutedEventArgs) Handles BtnWindowClose.Click
		Environment.Exit(0)
	End Sub

	Private Async Sub PageSwitcher(sender As Object, e As RoutedEventArgs) Handles LstTopbar.SelectionChanged
		If IsNothing(GridLocal) Then
			Exit Sub
		End If
		If TabCloudMusic.IsSelected Then
			CzTitle.Background = New SolidColorBrush(NETEASE_RED)
		Else
			CzTitle.Background = New SolidColorBrush(DEFAULT_COLOR)
		End If
		If TabLocal.IsSelected Then
			GridLocal.Visibility = Visibility.Visible
			RefreshPlaylists()
		Else
			GridLocal.Visibility = Visibility.Hidden
		End If
		If TabRemote.IsSelected Then
			GridRemote.Visibility = Visibility.Visible
		Else
			GridRemote.Visibility = Visibility.Hidden
		End If
		If TabCloudMusic.IsSelected Then
			If _isCloudMusicLoggedIn Then
				GridCloudMusic.Visibility = Visibility.Visible
			Else

				Dim dlgLogin As New DlgCloudMusicLogin()
				Dim cmPhone = DecryptString(_encryptKey, GetSetting("CloudMusicPhone", ""))
				Dim cmPassword = DecryptString(_encryptKey, GetSetting("CloudMusicPwd", ""))
				Dim flgAutoLogin = Boolean.Parse(GetSetting("CloudMusicAutoLogin", "False"))

				dlgLogin.Phone = cmPhone
				dlgLogin.Password = cmPassword
				Dim login = True

				While True
					If Not flgAutoLogin Or cmPhone = "" Or cmPassword = "" Then
						login = Await DlgWindowRoot.ShowDialog(dlgLogin)
					End If

					If login Then
						Dim dlgProg = New dlg_progress
						dlgProg.ChangeColorTheme(New SolidColorBrush(NETEASE_RED))
						DlgWindowRoot.ShowDialog(dlgProg)
						Dim result = Await UiCloudMusicLogin(dlgLogin.Phone, dlgLogin.Password, dlgProg)
						If result = "" Then
							If dlgLogin.ChkRememberPwd.IsChecked Then
								SaveSetting("CloudMusicPhone", EncryptString(_encryptKey, dlgLogin.Phone))
								SaveSetting("CloudMusicPwd", EncryptString(_encryptKey, dlgLogin.Password))
							End If
							If dlgLogin.ChkAutoLogin.IsChecked Then
								SaveSetting("CloudMusicAutoLogin", "True")
							Else
								SaveSetting("CloudMusicAutoLogin", "False")
							End If

							For Each p In _cloudMusic.Playlists
								ListBoxCloudMusicPlaylists.Items.Add(
									New ListBoxItem() With {.Content = p("name"), .Padding = New Thickness(20, 10, 0, 10)})
							Next
							Try
								Dim img As New BitmapImage
								img.BeginInit()
								Console.WriteLine(_cloudMusic.UserInfo("avatarUrl"))
								img.UriSource = New Uri(_cloudMusic.UserInfo("avatarUrl"))
								img.CacheOption = BitmapCacheOption.OnLoad
								img.EndInit()
								ImageCloudMusicAvatar.Source = img
								LabelCloudMusicNickName.Content = _cloudMusic.UserInfo("nickname")
							Catch ex As Exception

							End Try

							GridCloudMusic.Visibility = Visibility.Visible
							_isCloudMusicLoggedIn = True
							DlgWindowRoot.IsOpen = False
							If ListBoxCloudMusicPlaylists.Items.Count > 0 Then
								ListBoxCloudMusicPlaylists.SelectedIndex = 0
							End If
							Exit While
						Else
							Dim dlgFail As New DlgMessageDialog("登录失败", result)
							dlgFail.ChangeColorTheme(New SolidColorBrush(NETEASE_RED))
							_isCloudMusicLoggedIn = False
							DlgWindowRoot.IsOpen = False
							Await DlgWindowRoot.ShowDialog(dlgFail)
							flgAutoLogin = False
						End If
					Else
						TabCloudMusic.IsSelected = False
						TabLocal.IsSelected = True
						Exit While
					End If
				End While
			End If
		Else
			GridCloudMusic.Visibility = Visibility.Hidden
		End If
	End Sub

	''' <summary>
	''' UI login method, return an empty string when success, error message when failed
	''' </summary>
	''' <param name="dlgLogin">login dialog reference, for user phone and password</param>
	''' <param name="dlgProg">progress dialog ref, for changing prompt</param>
	''' <returns></returns>
	Private Async Function UiCloudMusicLogin(phone As String, pwd As String, dlgProg As dlg_progress) As Task(Of String)
		Dim result = Await Task.Run(Function()
										Try
											dlgProg.Text = "登录中"
											Dim loginResult = _cloudMusic.Login(phone, pwd)
											If loginResult("success") Then
												dlgProg.Text = "获取歌单"
												_cloudMusic.GetPlaylists()
												Return ""
											Else
												Return loginResult("msg")
											End If
										Catch
											Return "Unknown Error"
										End Try
									End Function)
		Return result
	End Function

	Private Async Sub MainWindow_ContentRendered(sender As Object, e As EventArgs) Handles Me.ContentRendered
		If Not My.Computer.FileSystem.FileExists("data.db") Then
			CreateDatabase()
		End If
		Dim dlg As New dlg_progress
		DialogHost.Show(dlg, "window-root")
		Dim upd As New DbUpdater
		Dim songDir = GetSetting("song_dir")
		If IsNothing(songDir) Then
			My.Computer.FileSystem.CreateDirectory("SongLib")
			SaveSetting("song_dir", "SongLib")
		End If
		_encryptKey = GetSetting("enc_key")

		PageSwitcher(Nothing, Nothing)

		Dim newLost = Await Task.Run(Async Function()
										 Dim lstNew = Await upd.FindNew(GetSetting("song_dir"))
										 Dim lstLost = Await upd.FindLost()
										 _lstSongs = GetSongs()
										 Dim syncResult As String = ""
										 If lstNew.Count > 0 Then
											 syncResult = "=========================发现以下新项目=========================" & vbNewLine
											 For Each NewItem In lstNew
												 syncResult += NewItem & vbNewLine
											 Next
										 End If
										 If lstLost.Count > 0 Then
											 syncResult += "=========================发现已删除项目=========================" & vbNewLine
											 For Each LostItem In lstLost
												 syncResult += LostItem & vbNewLine
											 Next
										 End If
										 Return syncResult
									 End Function)
		RefreshPlaylists()
		DatSongList.ItemsSource = _lstSongs
		DlgWindowRoot.IsOpen = False
		If newLost <> "" Then
			Dim dlgSyncResult = New dlgDirSyncResult(newLost)
			Await DialogHost.Show(dlgSyncResult, "window-root")
		End If
		_syncRemoteDeviceContent = ButtonRemoteSync.Content
		Dim t As New Thread(Sub()
								Do
									If Not _flgUSBRefreshPause Then
										ButtonRefreshDeviceList_Click(Nothing, Nothing)
										Thread.Sleep(1000)
									End If
								Loop
							End Sub)
		t.IsBackground = True
		t.Start()
	End Sub

	Private Async Sub RefreshPlaylists()
		Dim lstPlaylist = Await Task.Run(Function()
											 Dim r = GetPlaylists()
											 Return r
										 End Function)
		Dim addNew = ListBoxPlaylist.Items(ListBoxPlaylist.Items.Count - 1)
		ListBoxPlaylist.Items.Clear()

		For Each itm In lstPlaylist
			Dim lbitm = New ListBoxItem() With {.Content = itm, .AllowDrop = True, .Padding = New Thickness(15, 7, 0, 7)}
			AddHandler lbitm.Drop, AddressOf ListBoxItem_Drop
			ListBoxPlaylist.Items.Add(lbitm)
		Next
		ListBoxPlaylist.Items.Add(addNew)
	End Sub

	Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
		WindowChrome.SetWindowChrome(Me,
									 New WindowChrome() _
										With {.GlassFrameThickness = New Thickness(1),
										.UseAeroCaptionButtons = False, .ResizeBorderThickness = New Thickness(5),
										.CornerRadius = New CornerRadius(10),
										.CaptionHeight = 34})
	End Sub

	Private Async Sub DatSongList_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs) _
		Handles DatSongList.MouseDoubleClick
		If DatSongList.SelectedIndex <> -1 Then
			DlgWindowRoot.CloseOnClickAway = True
			Dim detailDialog As New DlgSongDetail(DatSongList.SelectedItem.path)
			Await DlgWindowRoot.ShowDialog(detailDialog)

			If Not detailDialog.IsDeleted Then
				If detailDialog.IsChanged Then
					UpdateInfo(DatSongList.SelectedItem.path)
					Dim t As New Track(DatSongList.SelectedItem.path)

					Dim index = _lstSongs.IndexOf(DatSongList.SelectedItem)
					_lstSongs(index) = New SongInfo _
						With {.Id = DatSongList.SelectedItem.Id, .Title = t.Title, .Artists = t.Artist, .Path =
							DatSongList.SelectedItem.path}
				End If
			Else
				_lstSongs.RemoveAt(DatSongList.SelectedIndex)
			End If
		End If
		DlgWindowRoot.CloseOnClickAway = False
	End Sub

	Private Sub ListBoxItem_Drop(sender As Object, e As DragEventArgs)
		If sender.Content.GetType.ToString = "System.Windows.Controls.WrapPanel" Then
			Exit Sub
		End If
		Try
			Dim songInf As SongInfo = e.Data.GetData(DragDrop.DataFormat.Name)
			'if it is 0(initial number of integer), it is probably not dragged from the list
			If songInf.Id = 0 Then
				Exit Sub
			End If
			Dim playlistId = GetPlaylistIdByName(sender.Content)
			AddSongToPlaylist(playlistId, songInf.Id)
		Catch ex As Exception
			If ex.Message = "Already Exist" Then
				Exit Sub
			End If
			Try
				e.Data.GetData("GongSolutions.Wpf.DragDrop")
			Catch
			End Try
			Dim a = e.Data.GetData("GongSolutions.Wpf.DragDrop")
			Dim playlistId = GetPlaylistIdByName(sender.Content)
			Dim conn = Connect()
			Dim trans = conn.BeginTransaction()
			Dim cmd = conn.CreateCommand()
			cmd.Transaction = trans
			For Each itm As SongInfo In a
				Try
					AddSongToPlaylist(playlistId, itm.Id, cmd)
				Catch exc As Exception
					If exc.Message <> "Already Exist" Then
						Console.WriteLine("Unexpected Error : " & exc.Message)
					End If
				End Try
			Next
			trans.Commit()
			conn.Close()
		End Try
	End Sub

	'Playlist Selected
	Private Async Sub ListItem_SelectionChange(sender As ListBox, e As EventArgs)
		If sender.SelectedIndex <> -1 And Not _isRightClickSelect Then
			If sender.SelectedItem.Tag = "NewPlaylist" Then
				'if is button "NewPlaylist"
				Dim dlg As New DlgNewPlaylist
				Dim result = Await DlgWindowRoot.ShowDialog(dlg)
				If result Then
					AddPlaylist(dlg.PlaylistName)
					Dim lbitm = New ListBoxItem() With {.Content = dlg.PlaylistName, .AllowDrop = True}
					AddHandler lbitm.Drop, AddressOf ListBoxItem_Drop
					ListBoxPlaylist.Items.Insert(ListBoxPlaylist.Items.Count - 1, lbitm)
				End If
				sender.SelectedIndex = -1
			Else
				'if not editing
				If ListBoxPlaylist.SelectedItem.Content.GetType = GetType(String) Then
					ButtonMusic.IsSelected = False
					DatSongList.CanUserSortColumns = False
					ButtonSaveSorting.Visibility = Visibility.Visible
					PanelSearchLocalMusic.Visibility = Visibility.Collapsed
					Dim dlg As New dlg_progress
					DatSongList.ItemsSource = Nothing
					DialogHost.Show(dlg, "window-root")
					Dim playlistName = sender.SelectedItem.Content
					Dim lstSongs = Await Task.Run(Function()
													  Dim songIds = GetSongsFromPlaylist(GetPlaylistIdByName(playlistName))
													  Dim conn = Connect()
													  Dim trans = conn.BeginTransaction()
													  Dim cmd = conn.CreateCommand()
													  cmd.Transaction = trans
													  Dim songs As New ObservableCollection(Of SongInfo)
													  For Each itm In songIds
														  songs.Add(GetSongById(itm, cmd))
													  Next
													  trans.Commit()
													  conn.Close()
													  Return songs
												  End Function)
					DatSongList.ItemsSource = lstSongs
					DlgWindowRoot.IsOpen = False
					ListBoxPlaylist.Focus()
				End If
			End If
		End If
		_isRightClickSelect = False
	End Sub

	Private Sub BlockRightClickSelection(sender As Object, e As MouseButtonEventArgs)
		_isRightClickSelect = True
	End Sub

	Private Sub ButtonMusic_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) _
		Handles ButtonMusic.MouseLeftButtonUp
		DatSongList.ItemsSource = Nothing
		DatSongList.ItemsSource = _lstSongs
		ListBoxPlaylist.SelectedIndex = -1
		DatSongList.CanUserSortColumns = False
		PanelSearchLocalMusic.Visibility = Visibility.Visible
		ButtonSaveSorting.Visibility = Visibility.Collapsed
	End Sub

	'Private Sub ButtonMusic_Selected(sender As Object, e As RoutedEventArgs) Handles ButtonMusic.Selected
	'	On Error Resume Next
	'	ListBoxPlaylist.SelectedIndex = -1
	'	DatSongList.CanUserSortColumns = True
	'End Sub

	Private Async Sub DeletePlaylist_Click(sender As Object, e As EventArgs) Handles MenuDeletePlaylist.Click
		If ListBoxPlaylist.SelectedIndex <> -1 And ListBoxPlaylist.SelectedIndex <> ListBoxPlaylist.Items.Count - 1 Then
			Dim dlg As New DlgYesNoDialog("删除播放列表", "要删除播放列表 """ & ListBoxPlaylist.SelectedItem.Content & """ 吗")
			Dim r = Await DlgWindowRoot.ShowDialog(dlg)
			If r = True Then
				Dim id = GetPlaylistIdByName(ListBoxPlaylist.SelectedItem.Content)
				RemovePlaylist(id)
				ListBoxPlaylist.Items.Remove(ListBoxPlaylist.SelectedItem)
				ButtonMusic_MouseLeftButtonUp(Nothing, Nothing)
			End If
		End If
	End Sub

	Private Sub ButtonDelete_Click(sender As Object, e As RoutedEventArgs) Handles ButtonDelete.Click
		If Not ButtonMusic.IsSelected Then
			If DatSongList.SelectedItems.Count <> 0 Then
				Dim conn = Connect()
				Dim trans = conn.BeginTransaction()
				Dim cmd = conn.CreateCommand()
				cmd.Transaction = trans
				Dim removeList As New List(Of Object)
				Dim playlistId = GetPlaylistIdByName(ListBoxPlaylist.SelectedItem.Content, cmd)
				For Each itm In DatSongList.SelectedItems
					Dim itmLocal As Object = itm
					RemoveSongFromPlaylist(playlistId, itm.id, cmd)
					removeList.Add(itmLocal)
				Next
				cmd.Transaction.Commit()
				conn.Close()
				For Each o As Object In removeList
					Dim source = CType(DatSongList.ItemsSource, ObservableCollection(Of SongInfo))
					source.Remove(o)
				Next
			End If
		Else
			'Remove from whole library
			If DatSongList.SelectedIndex <> -1 Then
				Dim conn = Connect()
				Dim cmd = conn.CreateCommand()
				Dim trans = conn.BeginTransaction()
				cmd.Transaction = trans
				Dim removeList As New List(Of Object)
				For Each itm In DatSongList.SelectedItems
					If My.Computer.FileSystem.FileExists(itm.path) Then
						My.Computer.FileSystem.DeleteFile(itm.path)
					End If
					removeList.Add(itm)
					RemoveSongFromLib(itm.id, cmd)
				Next
				trans.Commit()
				conn.Close()
				For Each o As Object In removeList
					Dim source = CType(DatSongList.ItemsSource, ObservableCollection(Of SongInfo))
					source.Remove(o)
				Next
			End If
		End If
	End Sub

	Private Sub LocalMusicTabKeyHandler(sender As Object, e As KeyEventArgs) Handles DatSongList.KeyUp
		If e.Key = Key.Delete Then
			ButtonDelete_Click(Nothing, Nothing)
		End If
	End Sub

	Private Async Sub SavePlaylistSorting(sender As Object, e As EventArgs) Handles ButtonSaveSorting.Click
		If ListBoxPlaylist.SelectedIndex <> -1 Then
			Dim conn = Connect()
			Dim cmd = conn.CreateCommand()
			Dim id = GetPlaylistIdByName(ListBoxPlaylist.SelectedItem.Content)
			ClearPlaylist(id)

			Dim trans = conn.BeginTransaction()
			cmd.Transaction = trans
			Dim lst As ObservableCollection(Of SongInfo) = DatSongList.ItemsSource
			DatSongList.ItemsSource = Nothing
			Dim dlg As New dlg_progress
			DlgWindowRoot.Show(dlg)
			Await Task.Run(Sub()
							   For Each songInfo As SongInfo In lst
								   AddSongToPlaylist(id, songInfo.Id, cmd)
							   Next
						   End Sub)
			trans.Commit()
			conn.Close()
			DatSongList.ItemsSource = lst
			DlgWindowRoot.IsOpen = False
		End If
	End Sub

	Private Sub ButtonCloudMusicLogout_Click(sender As Object, e As RoutedEventArgs) Handles ButtonCloudMusicLogout.Click
		SaveSetting("CloudMusicAutoLogin", "False")
		_cloudMusic = Nothing
		_cloudMusic = New CloudMusic
		_isCloudMusicLoggedIn = False
		ImageCloudMusicAvatar.Source = Nothing
		TabCloudMusic.IsSelected = False
		TabLocal.IsSelected = True
	End Sub

	Private Async Sub ListBoxCloudMusicPlaylists_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) _
		Handles ListBoxCloudMusicPlaylists.SelectionChanged
		Try
			If ListBoxCloudMusicPlaylists.SelectedIndex <> -1 Then
				Dim id = _cloudMusic.Playlists(ListBoxCloudMusicPlaylists.SelectedIndex)("id")
				Dim dlg As New dlg_progress
				dlg.ChangeColorTheme(New SolidColorBrush(NETEASE_RED))
				DlgCloudMusic.ShowDialog(dlg)

				Dim detail = Await Task.Run(Function()
												Dim r = _cloudMusic.GetPlaylistDetail(id)
												Return r
											End Function)
				DataGridCloudMusic.ItemsSource = detail("tracks")
				Dim img As New BitmapImage
				img.BeginInit()
				img.UriSource = New Uri(detail("coverImgUrl"))
				img.CacheOption = BitmapCacheOption.OnLoad
				img.EndInit()
				LabelCloudMusicPlaylistName.Content = detail("name")
				ImagePlaylistCover.Source = img
				DlgCloudMusic.IsOpen = False
			End If
			GC.Collect()
		Catch ex As Exception
			If DlgWindowRoot.IsOpen Then
				DlgWindowRoot.IsOpen = False
				Dim dlg As New DlgMessageDialog("错误", ex.Message)
				DlgWindowRoot.ShowDialog(dlg)
			End If
		End Try
	End Sub

	Private Async Sub RefreshCloudMusicPlaylists(sender As Object, e As EventArgs) Handles ButtonCloudMusicRefresh.Click
		Dim dlg As New dlg_progress
		dlg.ChangeColorTheme(New SolidColorBrush(NETEASE_RED))
		DlgCloudMusic.ShowDialog(dlg)
		Await Task.Run(Sub()
						   _cloudMusic.GetPlaylists()
					   End Sub)
		ListBoxCloudMusicPlaylists.Items.Clear()
		For Each p In _cloudMusic.Playlists
			ListBoxCloudMusicPlaylists.Items.Add(
				New ListBoxItem() With {.Content = p("name"), .Padding = New Thickness(20, 10, 0, 10)})
		Next
		DlgCloudMusic.IsOpen = False
	End Sub

	Private Sub BtnSearchSong_Click(sender As Object, e As RoutedEventArgs) Handles BtnSearchSong.Click
		If TextBoxSearchLocal.Text = "" Then
			DatSongList.ItemsSource = _lstSongs
		Else
			Dim r = Searching.SearchSongs(_lstSongs, TextBoxSearchLocal.Text)
			DatSongList.ItemsSource = r
		End If
	End Sub

	Private Sub TextBoxSearchLocal_TextChanged(sender As Object, e As TextChangedEventArgs) _
		Handles TextBoxSearchLocal.TextChanged
		If _lstSongs.Count < 1300 Then
			BtnSearchSong_Click(sender, Nothing)
		End If
	End Sub

	Private Sub TextBoxSearchLocal_KeyDown(sender As Object, e As KeyEventArgs) Handles TextBoxSearchLocal.KeyDown
		If e.Key = Key.Enter Or Key.Return Then
			BtnSearchSong_Click(sender, Nothing)
		End If
	End Sub

	Private Async Sub ButtonSyncCurrent_Click(sender As Object, e As RoutedEventArgs) Handles ButtonSyncCurrent.Click
		Dim dlg As New dlg_progress
		dlg.ChangeColorTheme(New SolidColorBrush(NETEASE_RED))
		DlgWindowRoot.ShowDialog(dlg)
		Dim plName = ListBoxCloudMusicPlaylists.SelectedItem.Content
		Dim lst = DataGridCloudMusic.ItemsSource
		Dim failed = Await Task.Run(Function()
										Return SyncAnalyzer.SyncPlaylist(plName, lst, dlg)
									End Function)
		Dim rString As String
		rString = "同步 """ & plName & """ 完成" & vbTab & "失败 " & failed.Count & " 个"
		rString += vbNewLine & StrDup(rString.Length, "=")
		For Each itmCloudMusicTracks As CloudMusic.CloudMusicTracks In failed
			rString += vbNewLine & itmCloudMusicTracks.Title & " - " & itmCloudMusicTracks.Artists
		Next
		Dim dlgResult As New dlgPlaylistSyncResult(rString)
		DlgWindowRoot.DialogContent = dlgResult
	End Sub

	Private Async Sub ButtonCloudMusicSyncAll_Click(sender As Object, e As RoutedEventArgs) _
		Handles ButtonCloudMusicSyncAll.Click
		Dim dlg As New dlg_progress
		dlg.ChangeColorTheme(New SolidColorBrush(NETEASE_RED))
		DlgWindowRoot.ShowDialog(dlg)
		Dim failed = Await Task.Run(Function()
										Return SyncAnalyzer.SyncAllPlaylists(_cloudMusic, dlg)
									End Function)
		Dim dlgResult As New dlgPlaylistSyncResult(failed)
		DlgWindowRoot.DialogContent = dlgResult
	End Sub

	Private Sub ButtonRefreshDeviceList_Click(sender As Object, e As RoutedEventArgs) Handles ButtonRefreshDeviceList.Click
		Dispatcher.Invoke(Sub() ComboBoxDevices.Items.Clear())
		For Each dev In My.Computer.FileSystem.Drives
			If dev.DriveType = IO.DriveType.Removable And dev.IsReady Then
				If dev.VolumeLabel = "" Then
					Dispatcher.Invoke(Sub() ComboBoxDevices.Items.Add(dev.Name & " (没有卷标)"))
				Else
					Dispatcher.Invoke(Sub() ComboBoxDevices.Items.Add(dev.Name & " (" & dev.VolumeLabel & ")"))
				End If

			End If
		Next

		If ComboBoxDevices.Items.Count > 0 Then
			Dispatcher.Invoke(Sub() ComboBoxDevices.SelectedIndex = 0)
		End If
	End Sub

	Private Sub ComboBoxDevices_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) _
		Handles ComboBoxDevices.SelectionChanged
		If sender.SelectedIndex = -1 Then
			Exit Sub
		End If
		Dim dir = ComboBoxDevices.SelectedItem.ToString.Substring(0, 3)
		For Each dev In My.Computer.FileSystem.Drives
			If dev.Name.ToUpper = dir.ToUpper Then
				LabelDeviceTotalVolume.Content = "总容量：" & dev.TotalSize / 1024 ^ 2 & " MB"
				LabelDeviceFreeVolume.Content = "剩余空间：" & dev.AvailableFreeSpace / 1024 ^ 2 & " MB"
			End If
		Next
	End Sub

	Private Structure CpInfo
		Public Source As String
		Public Destination As String
		Public Lyric As String
	End Structure

	Private Sub ButtonRemoteSync_Click(sender As Object, e As RoutedEventArgs) Handles ButtonRemoteSync.Click
		If ButtonRemoteSync.Content.Equals(_syncRemoteDeviceContent) Then
			_flgSyncStop = False
			_flgUSBRefreshPause = True
			StartSync()
		Else
			ButtonRemoteSync.Content = "正在取消"
			ButtonRemoteSync.IsEnabled = False
			_flgSyncStop = True
			_flgUSBRefreshPause = False
		End If
	End Sub

	Private Async Sub StartSync()

		ProgressBarSyncTotal.IsIndeterminate = True
		ButtonRemoteSync.Content = "取消"
		Dim drivePath = ComboBoxDevices.SelectedItem.ToString.Trim.Substring(0, 2)
		Dim wmManagedPath = drivePath & "\MUSIC\wmManaged"
		AddSyncLog(LogType.Information, "连接数据库")
		Dim conn = Connect()

		If Not My.Computer.FileSystem.DirectoryExists(wmManagedPath) Then
			My.Computer.FileSystem.CreateDirectory(wmManagedPath)
		End If

		Dim lstCopy As New List(Of CpInfo)
		Dim lstDelete As New List(Of String)
		Dim totalCopySize As Long
		Dim spaceNeeded As Long
		Dim flagCopyLrc As Boolean = Not CheckBoxSyncOptionDoNotCopyLyric.IsChecked
		Dim lstSongs = GetSongs()
		Dim progressSubscriber() As Long = {0, 0}
		Dim progressUpdateThread As Thread
		Dim flgProgressUpdateThreadStop = False
		Dim flgProgressUpdateThreadPause = False
		Dim flgHashCheck = CheckBoxSyncOptionHashCheck.IsChecked

		ProgressBarSyncSub.Maximum = 2
		ProgressBarSyncSub.IsIndeterminate = False
		ProgressBarSyncSub.Value = 0

		If IsNothing(lstSongs) Then
			GoTo Complete
		End If

		progressUpdateThread = New Thread(Sub()
											  Do
												  Dispatcher.Invoke(Sub()
																		If flgProgressUpdateThreadPause Then
																			Exit Sub
																		End If
																		If progressSubscriber(1) <> 0 Then
																			ProgressBarSyncSub.IsIndeterminate = False
																			ProgressBarSyncSub.Maximum = progressSubscriber(1)
																			ProgressBarSyncSub.Value = progressSubscriber(0)
																		Else
																			ProgressBarSyncSub.IsIndeterminate = True
																		End If
																	End Sub)
												  Thread.Sleep(300)
												  If flgProgressUpdateThreadStop Then
													  Exit Do
												  End If
											  Loop
										  End Sub)
		progressUpdateThread.IsBackground = True
		progressUpdateThread.Start()

		AddSyncLog(LogType.Information, "查找删除项目", False)
		lstDelete = Await Task.Run(Function()
									   Return SyncAnalyzer.FindDeleted(wmManagedPath, lstSongs, progressSubscriber, _flgSyncStop)
								   End Function)
		ProgressBarSyncSub.AddOne()
		If _flgSyncStop Then
			GoTo Complete
		End If

		AddSyncLog(LogType.Information, "发现需要删除的项目：" & lstDelete.Count, False)
		AddSyncLog(LogType.Information, "查找需要复制/覆盖的项目", False)
		Dim lstChanged = Await Task.Run(Function()
											Return SyncAnalyzer.FindChangedFiles(wmManagedPath, lstSongs, flgHashCheck, progressSubscriber, _flgSyncStop)
										End Function)
		If _flgSyncStop Then
			GoTo Complete
		End If
		AddSyncLog(LogType.Information, "发现需要复制/覆盖的项目：" & lstChanged.Count, False)
		ProgressBarSyncSub.Value = 0

		AddSyncLog(LogType.Information, "正在计算所需空间...", False)
		ProgressBarSyncSub.Maximum = lstChanged.Count + lstDelete.Count

		ProgressBarSyncSub.IsIndeterminate = False
		flgProgressUpdateThreadStop = True
		Await Task.Run(Sub()
						   For Each itm In lstChanged
							   If My.Computer.FileSystem.FileExists(itm) Then
								   Dim fInfo = My.Computer.FileSystem.GetFileInfo(itm)
								   totalCopySize += fInfo.Length
								   Dim sCopy As New CpInfo
								   sCopy.Source = itm
								   sCopy.Destination = wmManagedPath & "\" & My.Computer.FileSystem.GetFileInfo(itm).Name
								   If flagCopyLrc And My.Computer.FileSystem.FileExists(fInfo.DirectoryName & "\" & fInfo.Name & ".lrc") Then
									   sCopy.Lyric = fInfo.DirectoryName & "\" & fInfo.Name & ".lrc"
									   totalCopySize += My.Computer.FileSystem.GetFileInfo(fInfo.DirectoryName & "\" & fInfo.Name & ".lrc").Length
								   End If

								   lstCopy.Add(sCopy)
							   End If
							   ProgressBarSyncSub.AddOne(Me)

							   If _flgSyncStop Then
								   Exit Sub
							   End If
						   Next
						   spaceNeeded = totalCopySize

						   For Each itm In lstDelete
							   spaceNeeded -= My.Computer.FileSystem.GetFileInfo(itm).Length
							   ProgressBarSyncSub.AddOne(Me)

							   If _flgSyncStop Then
								   Exit Sub
							   End If
						   Next
					   End Sub)

		If _
			spaceNeeded > My.Computer.FileSystem.GetDriveInfo(drivePath).TotalFreeSpace And
			Not CheckBoxSyncOptionNoSpaceCheck.IsChecked Then
			Dim errorDlg As New DlgMessageDialog("同步失败", "磁盘空间不足")
			Await DialogHost.Show(errorDlg, "window-root")
			ProgressBarSyncTotal.Value = 0
			ProgressBarSyncTotal.Maximum = 0
			ProgressBarSyncTotal.IsIndeterminate = False
			ProgressBarSyncSub.Value = 0
			ProgressBarSyncSub.Maximum = 0
			ProgressBarSyncSub.IsIndeterminate = False
			ProgressBarSyncSub.Value = 0
			ProgressBarSyncTotal.Value = 0
			ButtonRemoteSync.Content = _syncRemoteDeviceContent
			Return
		End If

		ProgressBarSyncTotal.Maximum = lstCopy.Count + lstDelete.Count
		ProgressBarSyncTotal.IsIndeterminate = False

		Dim cp As New Synchronizer
		AddHandler cp.Update, AddressOf CopyingDetailUpdateEventHandler

		Await Task.Run(Sub()
						   For Each itm In lstDelete
							   AddSyncLog(LogType.Information, "删除文件：" & itm)
							   Try
								   My.Computer.FileSystem.DeleteFile(itm)
							   Catch ex As Exception
								   AddSyncLog(LogType.Err, "删除文件时出现错误：" & ex.Message)
								   Exit Sub
							   End Try
							   ProgressBarSyncTotal.AddOne(Me)

							   If _flgSyncStop Then
								   Exit Sub
							   End If
						   Next

						   For Each itm In lstCopy
							   Try
								   AddSyncLog(LogType.Information, "写入：" & itm.Destination)
								   cp.CopyFile(itm.Source, itm.Destination)
								   If itm.Lyric <> "" Then
									   AddSyncLog(LogType.Information, "写入：" & SyncAnalyzer.ChangePath(itm.Lyric, wmManagedPath))
									   cp.CopyFile(itm.Lyric, SyncAnalyzer.ChangePath(itm.Lyric, wmManagedPath))
								   End If
							   Catch ex As Exception
								   AddSyncLog(LogType.Err, "写入文件时出现错误：" & ex.Message)
								   Exit Sub
							   End Try
							   ProgressBarSyncTotal.AddOne(Me)

							   If _flgSyncStop Then
								   Exit Sub
							   End If
						   Next
					   End Sub)

		' Write playlist files
		Await Task.Run(Sub()
						   Dim lstPlaylists = GetPlaylists()
						   For Each p In lstPlaylists
							   Try
								   AddSyncLog(LogType.Information, "写入：" & wmManagedPath & "\" & p & ".m3u")
								   Dim playlistFile = My.Computer.FileSystem.OpenTextFileWriter(wmManagedPath & "\" & p & ".m3u", False, Text.Encoding.UTF8)
								   For Each s In GetSongsFromPlaylist(p)
									   Dim sInfo = GetSongById(s)
									   playlistFile.WriteLine(My.Computer.FileSystem.GetFileInfo(sInfo.Path).Name)
									   playlistFile.Flush()
								   Next
								   playlistFile.Close()
							   Catch ex As Exception
								   AddSyncLog(LogType.Err, ex.Message)
							   End Try
						   Next
					   End Sub)

Complete:
		ProgressBarSyncSub.Value = 0
		ProgressBarSyncSub.IsIndeterminate = False
		ProgressBarSyncTotal.Value = 0
		ButtonRemoteSync.Content = _syncRemoteDeviceContent
		Dim msgDlg As New DlgMessageDialog("", "同步完成")
		If Not IsNothing(progressUpdateThread) Then
			If progressUpdateThread.IsAlive Then
				progressUpdateThread.Abort()
			End If
		End If
		Await DlgWindowRoot.ShowDialog(msgDlg)
		ButtonRemoteSync.IsEnabled = True
	End Sub

	Private Sub CopyingDetailUpdateEventHandler(sender As Synchronizer)
		Dispatcher.Invoke(Sub()
							  ProgressBarSyncSub.Maximum = sender.TotalLength
							  ProgressBarSyncSub.Value = sender.CopiedLength
						  End Sub)
	End Sub

	Private Enum LogType
		Information
		Warning
		Err
	End Enum

	Private Sub AddSyncLog(type As LogType, message As String, Optional reqDelegate As Boolean = True,
						   Optional dispOnCpDetail As Boolean = True)
		Dim dispColor As Color

		Select Case type
			Case LogType.Information
				dispColor = Colors.Black
			Case LogType.Warning
				dispColor = Colors.DarkOrange
			Case LogType.Err
				dispColor = Colors.Red
		End Select

		If Not reqDelegate Then
			ListBoxSyncEventLog.Items.Add(New ListBoxItem() With {.Content =
											 String.Format("[{0}][{1}]: {2}", Now.ToString, type.ToString, message),
											 .Foreground = New SolidColorBrush(dispColor)})
			If dispOnCpDetail Then
				TextBoxOp.Text = message
			End If
		Else
			Me.Dispatcher.Invoke(Sub()
									 ListBoxSyncEventLog.Items.Add(New ListBoxItem() With {.Content =
																	  String.Format("[{0}][{1}]: {2}", Now.ToString, type.ToString, message),
																	  .Foreground = New SolidColorBrush(dispColor)})
									 If dispOnCpDetail Then
										 TextBoxOp.Text = message
									 End If
								 End Sub)
		End If
	End Sub

	Private Sub MenuRenamePlaylist_Click(sender As Object, e As RoutedEventArgs) Handles MenuRenamePlaylist.Click
		If ListBoxPlaylist.SelectedIndex <> -1 Then
			If ListBoxPlaylist.SelectedItem.Content.GetType = GetType(String) Then
				Dim textBoxRenamePlaylist = New TextBox _
						With {.Tag = New Object() {ListBoxPlaylist.SelectedItem, ListBoxPlaylist.SelectedItem.Content},
						.Width = ListBoxPlaylist.Width - 20,
						.Text = ListBoxPlaylist.SelectedItem.Content}
				AddHandler textBoxRenamePlaylist.KeyDown, AddressOf TextBoxRenamePlaylist_KeyDown
				ListBoxPlaylist.SelectedItem.Content = textBoxRenamePlaylist
				textBoxRenamePlaylist.Focus()
			End If
		End If
	End Sub

	Private Async Sub TextBoxRenamePlaylist_KeyDown(sender As TextBox, e As KeyEventArgs)
		If e.Key = Key.Escape Then
			sender.Tag(0).Content = sender.Tag(1)
			sender = Nothing
		ElseIf e.Key = Key.Enter Or e.Key = Key.Return Then
			If CheckPlaylistNameAvailability(sender.Text) Then
				RenamePlaylist(sender.Tag(1), sender.Text)
				sender.Tag(0).Content = sender.Text
				sender = Nothing
			Else
				Dim dlg As New DlgMessageDialog("重命名歌单失败", "有重名的歌单")
				Await DlgWindowRoot.ShowDialog(dlg)
				sender.Tag(0).Content = sender.Tag(1)
				sender = Nothing
			End If
		End If
	End Sub

	Private Sub ListBoxPlaylist_KeyDown(sender As Object, e As KeyEventArgs) Handles ListBoxPlaylist.KeyDown
		If e.Key = Key.F2 Then
			MenuRenamePlaylist_Click(sender, Nothing)
		End If
	End Sub

	Private Async Sub BtnSettings_Click(sender As Object, e As RoutedEventArgs) Handles BtnSettings.Click
		Dim dlg = New DlgSettings
		dlg.Init()
		Try
			Await DlgWindowRoot.ShowDialog(dlg)
			If dlg.FlgForceRestart Then
				Process.Start(Application.ResourceAssembly.Location)
				Environment.Exit(0)
			End If
		Catch
		End Try
	End Sub
End Class
