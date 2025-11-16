using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Clippy.Configurations;
using Button = System.Windows.Controls.Button;
using Visibility = System.Windows.Visibility;

namespace c4r.UI
{
    /// <summary>
    /// Interaction logic for ClippyWindow.xaml
    /// </summary>
    public partial class ClippyWindow : Window
    {
        private int _animation = 0;
        private static readonly List<ClippyAnimations> _clippyAnimations = new List<ClippyAnimations>();
        public static Clippy clippyItem;
        private static Border _bubbleBody;
        private static Polygon _bubbleCorner;
        private static TextBlock _bubbleText;
        System.Windows.Forms.Timer aTimer = new System.Windows.Forms.Timer();
        private static Button _bubbleButton;


        public ClippyWindow()
        {
            try
            {
                InitializeComponent();
                //HideBubble();
                
                //event handlers for drag window and close window
                this.MouseLeftButtonDown += OnOnMouseLeftButtonDown;
                this.MouseRightButtonDown += OnOnMouseRightButtonDown;

                //generate a new clippy element
                clippyItem = new Clippy(this.ClippyCanvas);
                if (clippyItem != null)
                {
                    clippyItem.StartAnimation(ClippyAnimations.Greeting);
                }

                GetAnimations();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing ClippyWindow: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }



       

        private void OnOnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (clippyItem != null)
            {
                clippyItem.StartAnimation(ClippyAnimations.GoodBye);
            }
            //start a timer to delay the closing.
            aTimer.Interval = 4000;
            aTimer.Tick += OnTimerOnElapsed;
            aTimer.Start();
        }

        private void OnTimerOnElapsed(object sender, EventArgs e)
        {
            HideBubble();
            aTimer.Stop();
            this.Close();
        }


        private void OnOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
        private void ClippyCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (clippyItem == null || _clippyAnimations.Count == 0)
                return;

            try
            {
                Random rnd = new Random();
                int animationInt = rnd.Next(0, _clippyAnimations.Count);

                ClippyAnimations animation = _clippyAnimations[animationInt];
                clippyItem.StartAnimation(animation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ClippyCanvas_OnMouseDown: {ex.Message}");
            }
        }

        private void GetAnimations()
        {
            var values = Enum.GetValues(typeof(ClippyAnimations));
            foreach (ClippyAnimations v in values)
            {
                if (v != ClippyAnimations.Save && v != ClippyAnimations.Print && v != ClippyAnimations.GoodBye && v != ClippyAnimations.Searching && v != ClippyAnimations.Greeting)
                {
                    _clippyAnimations.Add(v);
                }
            }
        }

        internal static void ShowBubble()
        {
            _bubbleCorner.Visibility = Visibility.Visible;
            _bubbleBody.Visibility = Visibility.Visible;
            _bubbleText.Visibility = Visibility.Visible;
            _bubbleButton.Visibility = Visibility.Visible;
        }
        internal void HideBubble()
        {
            BubbleBody.Visibility = Visibility.Hidden;
            BubbleText.Visibility = Visibility.Hidden;
            BubbleButton.Visibility = Visibility.Hidden;
        }

        private void BubbleButton_Click(object sender, RoutedEventArgs e)
        {
            HideBubble();
            this.Close();
        }
    }

}
