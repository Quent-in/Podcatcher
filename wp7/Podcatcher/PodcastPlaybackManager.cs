﻿using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Podcatcher.ViewModels;
using Microsoft.Phone.Tasks;
using Microsoft.Phone.BackgroundAudio;
using System.Linq;
using System.Diagnostics;
using Microsoft.Phone.Controls;


namespace Podcatcher
{
    public class PodcastPlaybackManager
    {
        public event EventHandler OnOpenPodcastPlayer;

        private static PodcastPlaybackManager m_instance;

        private PodcastPlaybackManager()
        {
            initializeCurrentlyPlayingEpisode();
            BackgroundAudioPlayer.Instance.PlayStateChanged += new EventHandler(PlayStateChanged);
        }

        public static PodcastPlaybackManager getInstance()
        {
            if (m_instance == null)
            {
                m_instance = new PodcastPlaybackManager();
            }

            return m_instance;
        }

        public void play(PodcastEpisodeModel episode, bool openPlayerView = true)
        {
            if (episode == null)
            {
                Debug.WriteLine("Warning: Trying to play a NULL episode.");
                return;
            }

            if (App.CurrentlyPlayingEpisode != null
                && (episode.EpisodeId != App.CurrentlyPlayingEpisode.EpisodeId))
            {
                addEpisodeToPlayHistory(App.CurrentlyPlayingEpisode);
                saveEpisodePlayPosition(App.CurrentlyPlayingEpisode);

                // If next episode is different from currently playing, the track changed.
                App.CurrentlyPlayingEpisode.setNoPlaying();
            }

            App.CurrentlyPlayingEpisode = episode;

            // Play locally from a downloaded file.
            if (episode.EpisodeDownloadState == PodcastEpisodeModel.EpisodeDownloadStateEnum.Downloaded)
            {
                PodcastPlayerControl player = PodcastPlayerControl.getIntance();
                episode.setPlaying();
                episode.EpisodePlayState = PodcastEpisodeModel.EpisodePlayStateEnum.Playing;
                player.playEpisode(episode);
            }
            else
            {
                // Stream it if not downloaded. 
                if (PodcastPlayerControl.isAudioPodcast(episode))
                {
                    episode.setPlaying();
                    audioStreaming(episode);
                    episode.EpisodePlayState = PodcastEpisodeModel.EpisodePlayStateEnum.Streaming;
                }
                else
                {
                    PodcastPlayerControl player = PodcastPlayerControl.getIntance();
                    player.StopPlayback();
                    videoStreaming(episode);
                    episode.EpisodePlayState = PodcastEpisodeModel.EpisodePlayStateEnum.Streaming;
                    openPlayerView = false;
                }
            }

            if (openPlayerView)
            {
                OnOpenPodcastPlayer(this, new EventArgs());
            }

            App.mainViewModels.PlayQueue = new System.Collections.ObjectModel.ObservableCollection<PlaylistItem>(); // Notify playlist changed.
        }

        public PodcastEpisodeModel currentlyPlayingEpisode()
        {
            PlaylistItem plItem = null;
            using (var playlistDb = new PlaylistDBContext())
            {
                if (playlistDb.Playlist.Count() == 0)
                {
                    return null;
                }

                plItem = playlistDb.Playlist.Where(item => item.IsCurrent).FirstOrDefault();
            }

            if (plItem != null)
            {
                using (var db = new PodcastSqlModel())
                {
                    return db.Episodes.Where(ep => ep.EpisodeId == plItem.EpisodeId).FirstOrDefault();
                }
            }

            return null;
        }

        public void playPlaylistItem(int tappedPlaylistItemId)
        {
            int episodeId = -1;
            using (var db = new PlaylistDBContext())
            {
                if (db.Playlist.Count() < 1)
                {
                    return;
                }

                PlaylistItem current = db.Playlist.FirstOrDefault(item => item.IsCurrent == true);
                if (current.ItemId == tappedPlaylistItemId)
                {
                    Debug.WriteLine("Tapped on the currently playing episode. I am not changing the track...");
                    return;
                }

                episodeId = (int)db.Playlist.Where(item => item.ItemId == tappedPlaylistItemId).Select(item => item.EpisodeId).First();

                if (episodeId > -1)
                {
                    if (current != null)
                    {
                        current.IsCurrent = false;
                    }

                    PlaylistItem next = db.Playlist.FirstOrDefault(item => item.ItemId == tappedPlaylistItemId);
                    if (next != null)
                    {
                        next.IsCurrent = true;
                    }

                    db.SubmitChanges();
                }
            }

            PodcastEpisodeModel episode = null;
            using (var db = new PodcastSqlModel())
            {
                episode = db.Episodes.First(ep => ep.EpisodeId == episodeId);
            }

            if (episode != null)
            {
                play(episode, false);
            }
            else
            {
                Debug.WriteLine("Warning: Could not play episode: " + episodeId);
            }
        }

        /****************************** Private implementations *******************************/

        private void videoStreaming(PodcastEpisodeModel podcastEpisode)
        {
            MediaPlayerLauncher mediaPlayerLauncher = new MediaPlayerLauncher();
            mediaPlayerLauncher.Media = new Uri(podcastEpisode.EpisodeDownloadUri, UriKind.Absolute);
            mediaPlayerLauncher.Controls = MediaPlaybackControls.All;
            mediaPlayerLauncher.Location = MediaLocationType.Data;
            mediaPlayerLauncher.Show();
        }

        private void audioStreaming(PodcastEpisodeModel podcastEpisode)
        {
            PodcastPlayerControl player = PodcastPlayerControl.getIntance();
            player.streamEpisode(podcastEpisode);
        }

        private void addEpisodeToPlayHistory(PodcastEpisodeModel episode)
        {
            using (var db = new PodcastSqlModel())
            {
                db.addEpisodeToPlayHistory(episode);
            }
        }

        private void clearPlayList()
        {
            using (var db = new PlaylistDBContext())
            {
                db.Playlist.DeleteAllOnSubmit(db.Playlist);
                db.SubmitChanges();
            }
        }

        private void initializeCurrentlyPlayingEpisode()
        {
            // If we have an episodeId stored in local cache, this means we returned to the app and 
            // have that episode playing. Hence, here we need to reload the episode data from the SQL. 
            App.CurrentlyPlayingEpisode = currentlyPlayingEpisode();
            if (App.CurrentlyPlayingEpisode != null)
            {
                App.CurrentlyPlayingEpisode.setPlaying();
            }
        }

        private void saveEpisodePlayPosition(PodcastEpisodeModel episode)
        {
            try
            {
                episode.SavedPlayPos = BackgroundAudioPlayer.Instance.Position.Ticks;
            }
            catch (NullReferenceException)
            {
                Debug.WriteLine("BackgroundAudioPlayer returned NULL. Player didn't probably have a track that it was playing.");
                return;
            }
            catch (SystemException)
            {
                Debug.WriteLine("Got system exception when trying to save position.");
                return;
            }

            using (var db = new PodcastSqlModel())
            {
                PodcastEpisodeModel e = db.Episodes.Where(ep => ep.EpisodeId == episode.EpisodeId).First();
                e.SavedPlayPos = episode.SavedPlayPos;
                db.SubmitChanges();
            }
        }

        private void GoBack()
        {
            PhoneApplicationFrame rootFrame = Application.Current.RootVisual as PhoneApplicationFrame;
            if (rootFrame.CanGoBack)
            {
                rootFrame.GoBack();
            }
        }

        private void PlayStateChanged(object sender, EventArgs e)
        {
            switch (BackgroundAudioPlayer.Instance.PlayerState)
            {
                case PlayState.Playing:
                    PodcastEpisodeModel currentEpisode = currentlyPlayingEpisode();
                    if (currentEpisode == null)
                    {
                        Debug.WriteLine("Error: No playing episode in DB.");
                        return;
                    }

                    App.CurrentlyPlayingEpisode = currentEpisode;
                    App.CurrentlyPlayingEpisode.setPlaying();
                    break;

                case PlayState.Paused:
                    saveEpisodePlayPosition(App.CurrentlyPlayingEpisode);
                    break;

                case PlayState.Stopped:
                case PlayState.Shutdown:
                    if (App.CurrentlyPlayingEpisode == null)
                    {
                        // We didn't have a track playing.
                        return;
                    }

                    saveEpisodePlayPosition(App.CurrentlyPlayingEpisode);
                    addEpisodeToPlayHistory(App.CurrentlyPlayingEpisode);
                    PodcastSubscriptionsManager.getInstance().podcastPlaystateChanged(App.CurrentlyPlayingEpisode.PodcastSubscriptionInstance);
                    
                    // Cleanup
                    App.CurrentlyPlayingEpisode = null;
                    BackgroundAudioPlayer.Instance.Close();
                    GoBack();
                    break;

                case PlayState.TrackReady:
                    break;

                case PlayState.TrackEnded:
                    saveEpisodePlayPosition(App.CurrentlyPlayingEpisode);
                    break;

                case PlayState.Unknown:
                    break;
            }
        }
    }
}
