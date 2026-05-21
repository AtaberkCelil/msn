using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Client
{
    public partial class ToastNotification : Window
    {
        private readonly string _title;
        private readonly string _message;

        public ToastNotification(string title, string message)
        {
            InitializeComponent();
            _title = title;
            _message = message;
            
            LblTitle.Text = _title;
            LblMessage.Text = _message;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position toast at bottom right of working area
            double right = SystemParameters.WorkArea.Right;
            double bottom = SystemParameters.WorkArea.Bottom;

            this.Left = right - this.Width - 10;
            this.Top = bottom; // start hidden off-screen

            // Animate slide up
            double targetTop = bottom - this.Height - 10;
            var slideAnim = new DoubleAnimation(bottom, targetTop, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            this.BeginAnimation(Window.TopProperty, slideAnim);

            // Wait, then fade out and close
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(600));
                fadeAnim.Completed += (s2, ev2) => this.Close();
                this.BeginAnimation(Window.OpacityProperty, fadeAnim);
            };
            timer.Start();
        }
    }
}
