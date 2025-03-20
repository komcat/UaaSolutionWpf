using System;
using System.Windows;
using System.Windows.Controls;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Interaction logic for PlayPauseButtons.xaml
    /// </summary>
    public partial class PlayPauseButtons : UserControl
    {
        // Events for play and pause actions
        public event EventHandler PlayClicked;
        public event EventHandler PauseClicked;

        // Actions for more direct control
        private Action _playAction;
        private Action _pauseAction;

        public PlayPauseButtons()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Set direct action handlers for play and pause
        /// </summary>
        public void SetActions(Action playAction, Action pauseAction)
        {
            _playAction = playAction;
            _pauseAction = pauseAction;
        }

        /// <summary>
        /// Handle play button click
        /// </summary>
        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            // Invoke the play action if it's been set
            _playAction?.Invoke();

            // Raise the play clicked event
            PlayClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Handle pause button click
        /// </summary>
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            // Invoke the pause action if it's been set
            _pauseAction?.Invoke();

            // Raise the pause clicked event
            PauseClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Enable or disable the play button
        /// </summary>
        public void SetPlayEnabled(bool enabled)
        {
            PlayButton.IsEnabled = enabled;
            PlayButton.Opacity = enabled ? 1.0 : 0.5;
        }

        /// <summary>
        /// Enable or disable the pause button
        /// </summary>
        public void SetPauseEnabled(bool enabled)
        {
            PauseButton.IsEnabled = enabled;
            PauseButton.Opacity = enabled ? 1.0 : 0.5;
        }
    }
}