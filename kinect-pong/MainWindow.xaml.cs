﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Kinect;

namespace kinect_pong
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Constants
        private const double DEG_TO_RAD = Math.PI / 180;            // Converting degrees to radians
        private const double MARGIN_OFFSET = 25;                    // Offset of paddles from edges of canvas
        private const int FRAME_RATE = 60;                          // Frame rate for the animation
        private const JointType HAND_RIGHT = JointType.HandRight;   // Int representing the right hand
        private const JointType HAND_LEFT = JointType.HandLeft;     // Int representing the left hand

        // Instance of Kinect Helper, access to data streams and sensor properties
        private KinectHelper helper;

        // Random number generator
        private Random rando;

        // Timer to count down the beginning of a round
        private System.Timers.Timer newBallTimer;
        private int countdown = 3;

        // Thread to run all game operations on
        private Thread gameThread;
        private bool gameStarted = false;
        private bool gamePaused = false;
        private bool newGame = true;
        
        // Player paddles and positions
        private Rectangle playerOne;
        private Rectangle playerTwo;
        private Point playerOnePos;
        private Point playerOnePosPast;
        private Point playerTwoPos;
        private Point playerTwoPosPast;
 
        // Ball and it's movement/location properties
        private Ellipse ball;
        private Point ballPos;
        private Vector ballFwd;
        private double ballSpd;

        // Markers for the player skeletons
        private Ellipse markOne;
        private Ellipse markTwo;
        private Point markOnePos;
        private Point markTwoPos;

        // Scorekeepers
        private int playerOneScore = 0;
        private int playerTwoScore = 0;
        private int winScore = 10;

        // What hands do we want to track?
        private JointType playerOneHandedness = HAND_RIGHT;
        private JointType playerTwoHandedness = HAND_RIGHT;

        // Constructor for the Main Window
        public MainWindow()
        {
            InitializeComponent();
            //Initialize the Kinect Helper for Skeleton data only
            helper = new KinectHelper(true, false, true);
            helper.ToggleSeatedMode(true);
            helper.SkeletonDataChanged += this.SkeletonDataChanged;
            SkeletonImage.Source = helper.skeletonBitmap;
            rgbImage.Source = helper.colorBitmap;
        }

        // Initialize a new game
        // Called when the "Start Game" button is clicked
        private void InitializeGame()
        {
            // Clear the canvas of existing elements
            GameCanvas.Children.Clear();
            // Initialize game variables
            rando = new Random();
            newBallTimer = new System.Timers.Timer();
            newBallTimer.Elapsed += new ElapsedEventHandler(countdownTick);
            newBallTimer.Interval = 1000;
            playerOnePos = new Point();
            playerOnePosPast = new Point();
            playerTwoPos = new Point();
            playerTwoPosPast = new Point();
            ballPos = new Point();
            ballFwd = new Vector();
            markOnePos = new Point();
            markTwoPos = new Point();
            // Initialize the scores
            winScore = Convert.ToInt32(winningScoreBox.Text);
            playerOneScore = 0;
            scoreOneLabel.Content = playerOneScore;
            playerTwoScore = 0;
            scoreTwoLabel.Content = playerTwoScore;
            // Initialize the ball
            ball = new Ellipse();
            ball.Width = 25;
            ball.Height = 25;
            ball.Stroke = new SolidColorBrush(Colors.Black);
            ball.StrokeThickness = 2;
            ball.Fill = new SolidColorBrush(Colors.Green);
            ResetBall();
            GameCanvas.Children.Add(ball);
            // Initialize the left paddle (Player One)
            playerOne = new Rectangle();
            playerOne.Width = 40;
            playerOne.Height = 120;
            playerOne.Stroke = new SolidColorBrush(Colors.Black);
            playerOne.StrokeThickness = 2;
            playerOne.Fill = new SolidColorBrush(Colors.Blue);
            playerOnePos.X = MARGIN_OFFSET;
            playerOnePos.Y = GameCanvas.Height / 2 - playerOne.Height / 2;
            playerOnePosPast = playerOnePos;
            GameCanvas.Children.Add(playerOne);
            // Initialize the right paddle (Player Two)
            playerTwo = new Rectangle();
            playerTwo.Width = 40;
            playerTwo.Height = 120;
            playerTwo.Stroke = new SolidColorBrush(Colors.Black);
            playerTwo.StrokeThickness = 2;
            playerTwo.Fill = new SolidColorBrush(Colors.Red);
            playerTwoPos.X = GameCanvas.Width - playerTwo.Width - MARGIN_OFFSET;
            playerTwoPos.Y = GameCanvas.Height / 2 - playerTwo.Height / 2;
            playerTwoPosPast = playerTwoPos;
            GameCanvas.Children.Add(playerTwo);
            // Initialize player markers
            markOne = new Ellipse();
            markOne.Width = 75;
            markOne.Height = 75;
            markOne.Fill = new SolidColorBrush(Colors.Blue);
            markOne.Opacity = 0;
            markOnePos.X = GameCanvas.Width;
            markOnePos.Y = GameCanvas.Height;
            GameCanvas.Children.Add(markOne);
            markTwo = new Ellipse();
            markTwo.Width = 75;
            markTwo.Height = 75;
            markTwo.Fill = new SolidColorBrush(Colors.Red);
            markTwo.Opacity = 0;
            markTwoPos.X = GameCanvas.Width;
            markTwoPos.Y = GameCanvas.Height;
            GameCanvas.Children.Add(markTwo);
            // (Re)Initialize the game thread
            gameThread = new Thread(new ThreadStart(this.GameLoop));
            gameThread.IsBackground = true;
        }

        // Game loop for a game of Pong
        // Method run by the gameThread
        private void GameLoop()
        {
            while (true)
            {
                if (!gamePaused && gameStarted)
                {
                    Thread.Sleep(1000 / FRAME_RATE);
                    this.Dispatcher.Invoke((Action)(() =>
                    {
                        Update();
                        Draw();
                    }));
                }
            }
        }

        // Update method for the game loop
        // Update the game logic, perform calculations, checks, etc
        private void Update()
        {
            // If it's a new game, start with a countdown
            if (newGame)
            {
                NewRound();
                newGame = false;
            }

            // Move the ball forward
            ballPos = Vector.Add(ballFwd * ballSpd, ballPos);

            // Bounce off the side walls
            if (ballPos.Y < 0 || ballPos.Y > GameCanvas.Height - ball.Height)
            {
                BounceBallVertical();   
            }

            // Check if the ball will score on player two
            if (ballPos.X < MARGIN_OFFSET + playerOne.Width)
            {
                // If the ball hit the paddle, make it bounce
                if (ballPos.Y < playerOnePos.Y + playerOne.Height + ball.Height/2 && ballPos.Y > playerOnePos.Y - ball.Height/2)
                {
                    // Move the ball a few pixels in front of the paddle (to prevent glitches)
                    ballPos.X = playerOnePos.X + playerOne.Width + 3;
                    // Add some slight randomization to the ball's direction
                    double delta = (playerOnePos.Y - playerOnePosPast.Y)/GameCanvas.Height;
                    ballFwd.Y += delta * 2;
                    // Bounce the ball of the paddle
                    BounceBallHorizontal();
                }
                // If the ball misses the paddle, score a point
                else if (ballPos.X < MARGIN_OFFSET + playerOne.Width / 2)
                {
                    // Reset the ball
                    ResetBall();
                    // Increment player two's score
                    playerTwoScore += 1;
                    scoreTwoLabel.Content = playerTwoScore;
                    // Start a new round
                    NewRound();
                }
            }
            
            // Check if the ball will score on player two
            if (ballPos.X + ball.Width > GameCanvas.Width - MARGIN_OFFSET - playerTwo.Width)
            {
                // If the ball hit the paddle, make it bounce
                if (ballPos.Y < playerTwoPos.Y + playerTwo.Height + ball.Height/2 && ballPos.Y > playerTwoPos.Y - ball.Height/2)
                {
                    // Move the ball a few pixels in front of the paddle (to prevent glitches)
                    ballPos.X = playerTwoPos.X - ball.Width - 3;
                    // Add some slight randomization to the ball's direction
                    double delta = (playerTwoPos.Y - playerTwoPosPast.Y) / GameCanvas.Height;
                    ballFwd.Y += delta * 2;
                    // Bounce the ball of the paddle
                    BounceBallHorizontal();
                }
                // If the ball misses the paddle, score a point
                else if (ballPos.X + ball.Width> GameCanvas.Width - MARGIN_OFFSET - playerTwo.Width / 2)
                {
                    // Reset the ball
                    ResetBall();
                    // Increment player one's score
                    playerOneScore += 1;
                    scoreOneLabel.Content = playerOneScore;
                    // Start a new round
                    NewRound();
                }
            }
            
            // Check for a winner
            if (playerOneScore >= winScore)
            {
                ToggleStart();
                gameLabel.Content = "Player One Wins!";
            }
            else if (playerTwoScore >= winScore)
            {
                ToggleStart();
                gameLabel.Content = "Player Two Wins!";
            }
        }

        // Reset the ball when a point is scored and a new round begins
        private void ResetBall()
        {
            // Set the ball's position to the center of the canvas
            ballPos = new Point(GameCanvas.Width / 2 - ball.Width / 2, GameCanvas.Height / 2 - ball.Height / 2);
            // Reset the ball's speed
            ballSpd = 4;
            // Randomize the ball's direction and angle
            double direction = rando.NextDouble();
            if (direction > 0.5)
                direction = 1;
            else 
                direction = -1;
            double angle = direction * (rando.NextDouble() * 90 + 45);
            // Set the ball's direction
            ballFwd = new Vector(Math.Sin(angle * DEG_TO_RAD), Math.Cos(angle * DEG_TO_RAD));
        }

        // Begin the countdown before a new round begins
        private void NewRound()
        {
            // Pause the game
            gamePaused = true;
            pauseGameButton.IsEnabled = false;
            // Start the countdown timer
            countdown = 3;
            gameLabel.Content = countdown;
            newBallTimer.Start();
        }

        // Tick event for the countdownTimer
        // Display countdown status, begin the game when the countdown is over
        private void countdownTick(object o, ElapsedEventArgs e)
        {
            // Decrement the countdown timer
            countdown--;
            // If the countdown is finished, unpuase the game and begin
            if (countdown == 0)
            {
                gamePaused = false;
                this.Dispatcher.Invoke((Action)(() =>
                {
                    gameLabel.Content = "";
                    pauseGameButton.IsEnabled = true;
                }));
                newBallTimer.Stop();
            }
            // If the countdown is still going, update the label on the screen
            else 
            {
                this.Dispatcher.Invoke((Action)(() =>
                {
                    gameLabel.Content = countdown;
                }));
            }
            
        }

        // Bounce the ball horizontally
        // Used when the ball collides with a paddle
        private void BounceBallHorizontal()
        {
            // Reflect the ball's horizontal direction
            ballFwd.X = -ballFwd.X;
            // Increase the speed a bit
            ballSpd += .5;
        }

        // Bounce the ball vertically
        // Used when the ball collides with the floor/ceiling
        private void BounceBallVertical()
        {
            // Reflect the ball's vertical direction
            ballFwd.Y = -ballFwd.Y;
        }

        // Draw method of the game loop
        // Draw a frame of the game, update the on-screen positions of all game objects
        private void Draw()
        {
            // Move the paddles to their new positions
            Canvas.SetLeft(playerOne, playerOnePos.X);
            Canvas.SetTop(playerOne, playerOnePos.Y);
            Canvas.SetLeft(playerTwo, playerTwoPos.X);
            Canvas.SetTop(playerTwo, playerTwoPos.Y);

            // Move the ball to its new position
            Canvas.SetLeft(ball, ballPos.X);
            Canvas.SetTop(ball, ballPos.Y);
        }

        // Event handler for changes in Skeleton stream data
        // Used as a secondary "update" method for setting the position of kinect-controlled paddles
        private void SkeletonDataChanged(object sender, SkeletonDataChangeEventArgs e)
        {
            // Don't update if the game hasn't started
            if (gameThread == null)
                return;

            // Determine which skeletons are on which side
            Skeleton right = null;
            Skeleton left = null;
            // Loop through all available skeletons
            for (int i = 0; i < e.skeletons.Length; i++)
            {
                // Grab the current skeleton
                Skeleton skel = e.skeletons[i];
                // If we're tracked figure out what side of the screen we're on
                if (skel.TrackingState == SkeletonTrackingState.Tracked)
                {
                    Point position = helper.SkeletonPointToScreen(skel.Joints[JointType.ShoulderCenter].Position);
                    // If the skeleton is the first on the left side of the screen, it is the left skeleton
                    if ((position.X > 0 && position.X <= GameCanvas.Width / 2) && left == null)
                        left = skel;
                    // If the skeleton is the first on the right side of the screen, it is the right skeleton
                    else if ((position.X > GameCanvas.Width / 2 && position.X < GameCanvas.Width) && right == null)
                        right = skel;
                }
                // If both skeletons have been found, no need to keep looking
                if (left != null & right != null)
                    break;
            }

            // If the left skeleton wasn't found, hide the marker
            if (left == null)
                markOne.Opacity = 0;
            // If the left skeleton was found, update some values
            else
            {
                // Get the locations of the skeleton's head and hand
                Point playerOneHand = helper.SkeletonPointToScreen(left.Joints[playerOneHandedness].Position);
                Point playerOneHead = helper.SkeletonPointToScreen(left.Joints[JointType.Head].Position);
                // Save the last position of player one's paddle
                playerOnePosPast = playerOnePos;
                // Update the position of player one's paddle
                playerOnePos.Y = playerOneHand.Y - playerOne.Height / 2;
                // Show and move the player's marker
                markOne.Opacity = 1;
                markOnePos.X = playerOneHead.X - markOne.Width / 2;
                markOnePos.Y = playerOneHead.Y - markOne.Height / 2;
            }

            // If the right skeleton wasn't found, hide the marker
            if (right == null)
                markTwo.Opacity = 0;
            // If the right skeleton was found, update some values
            else
            {
                // Get the locations of the skeleton's head and hand
                Point playerTwoHand = helper.SkeletonPointToScreen(right.Joints[playerTwoHandedness].Position);
                Point playerTwoHead = helper.SkeletonPointToScreen(right.Joints[JointType.Head].Position);
                // Save the last position of player two's paddle
                playerTwoPosPast = playerTwoPos;
                // Update the position of player one's paddle
                playerTwoPos.Y = playerTwoHand.Y - playerTwo.Height / 2;
                // Show and move the player's marker
                markTwo.Opacity = 1;
                markTwoPos.X = playerTwoHead.X - markTwo.Width / 2;
                markTwoPos.Y = playerTwoHead.Y - markTwo.Height / 2;
            }

            // Draw the markers separately from the rest of the game
            // TO-DO: Move this somewhere better
            this.Dispatcher.Invoke((Action)(() =>
            {
                // Move the markers to their new positions
                Canvas.SetLeft(markOne, markOnePos.X);
                Canvas.SetTop(markOne, markOnePos.Y);
                Canvas.SetLeft(markTwo, markTwoPos.X);
                Canvas.SetTop(markTwo, markTwoPos.Y);
            }));
        }

        // Event handler for closing the window
        // Used to abort the game thread
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (gameThread != null)
            {
                gameThread.Abort();
            }
        }

        // Event handlers for the radio button's Checked events
        // Used to adjust the handedness of the two players
        private void playerOneLeftButton_Checked(object sender, RoutedEventArgs e)
        {
            playerOneHandedness = HAND_LEFT;
            playerOneRightButton.IsChecked = false;
        }
        private void playerOneRightButton_Checked(object sender, RoutedEventArgs e)
        {
            playerOneHandedness = HAND_RIGHT;
            playerOneLeftButton.IsChecked = false;
        }
        private void playerTwoLeftButton_Checked(object sender, RoutedEventArgs e)
        {
            playerTwoHandedness = HAND_LEFT;
            playerTwoRightButton.IsChecked = false;
        }
        private void playerTwoRightButton_Checked(object sender, RoutedEventArgs e)
        {
            playerTwoHandedness = HAND_RIGHT;
            playerTwoLeftButton.IsChecked = false;
        }

        // Event handler for the winningScoreBox's Changed event
        // Update the max score of the game
        private void winningScoreBox_Changed(object sender, TextChangedEventArgs e)
        {
            int newScore;
            try
            {
                // Grab the new score from the text field
                newScore = Convert.ToInt32(winningScoreBox.Text);
                // Keep the score within reasonable limits
                if (newScore > 100)
                    newScore = 100;
                else if (newScore < 1)
                    newScore = 1;
            }
            catch
            {
                // If an error occurred grabbing the text field's value, just use the current win score
                newScore = winScore;
            }
            // Make sure the text box is displaying the proper value
            winningScoreBox.Text = newScore.ToString();
        }

        // Event handler for the pauseGameButton's Click event
        // Used to pause or unpause the game
        private void pauseGameButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePaused();
        }

        // Toggle the paused state of the game
        private void TogglePaused()
        {
            // If the game is not paused, pause it
            if (!gamePaused)
            {
                gamePaused = true;
                pauseGameButton.Content = "Unpause Game";
                gameLabel.Content = "* Game Paused *";
            }
            // If the game is paused, unpause it
            else
            {
                gamePaused = false;
                pauseGameButton.Content = "Pause Game";
                gameLabel.Content = "";
            }
        }
        
        // Event handler for the startGameButton's Click event
        // Used to start or reset the game
        private void startGameButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleStart();
        }

        // Start or end the current game
        private void ToggleStart()
        {
            // If the game isn't started, start it
            if (!gameStarted)
            {
                // Reset the game state
                newGame = true;
                gameStarted = true;
                if (gamePaused)
                    TogglePaused();
                // Initialize the game state
                InitializeGame();
                // Start the game thread
                gameThread.Start();
                gameLabel.Content = "";
                startGameButton.Content = "End Game";
            }
            else
            {
                // Set the game state
                gameStarted = false;
                // Clear the canvas
                GameCanvas.Children.Clear();
                // End the game thread
                gameThread.Abort();
                gameLabel.Content = "";
                startGameButton.Content = "Start Game";
            }
        }
    }
}
