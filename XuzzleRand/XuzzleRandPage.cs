﻿using System;
using System.Threading.Tasks;
using Xamarin.Forms;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XuzzleRand
{
	static class StringExtensions
	{
		public static string Truncate(this string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value)) return value;
			return value.Length <= maxLength ? value : value.Substring(0, maxLength); 
		}
	}

	class XuzzleRandPage : ContentPage
	{
		// Number of squares horizontally and vertically,
		//  but if you change it, some code will break.
		static readonly int NUM = 4;

		// Array of XuzzleSquare views, and empty row & column.
		XuzzleRandSquare[,] squares = new XuzzleRandSquare[NUM, NUM];
		int emptyRow = NUM - 1;
		int emptyCol = NUM - 1;

		StackLayout stackLayout;
		AbsoluteLayout absoluteLayout;
		Button newGameButton;
		Button randomizeButton;
		Label timeLabel;
		double squareSize;
		bool isBusy;
		bool isPlaying;
		Stream stream;
		StreamReader reader;
		string[] lines;
		string text;
		string winText = "CONGRATULATIONS";
		int index;
		XuzzleRandSquare square;
		DateTime startTime;

		public XuzzleRandPage ()
		{
			// Loading files embedded as resources, more info:
			// http://developer.xamarin.com/guides/cross-platform/xamarin-forms/working-with/files/#Loading_Files_Embedded_as_Resources
			var assembly = typeof(XuzzleRandPage).GetTypeInfo().Assembly;

			stream = assembly.GetManifestResourceStream("XuzzleRand.BadWordsBannedByGoogle.txt");
			reader = new System.IO.StreamReader (stream);

			// Read lines.
			lines = reader.ReadToEnd().Split(new char[] {'\n'});

			// Get a random text.
			getRandomText();

			// This is the "New word" button.
			newGameButton = new Button {
				Text = "New game",
				HorizontalOptions = LayoutOptions.StartAndExpand,
				VerticalOptions = LayoutOptions.CenterAndExpand,
				BackgroundColor = Color.Transparent,
				FontSize = Device.GetNamedSize(NamedSize.Large, typeof(Label))
			};
			newGameButton.Clicked += OnNewGameButtonClicked;

			// This is the "Randomize" button.
			randomizeButton = new Button {
				Text = "Randomize",
				HorizontalOptions = LayoutOptions.EndAndExpand,
				VerticalOptions = LayoutOptions.CenterAndExpand,
				BackgroundColor = Color.Transparent,
				FontSize = Device.GetNamedSize(NamedSize.Large, typeof(Label))
			};
			randomizeButton.Clicked += OnRandomizeButtonClicked;

			// Label to display elapsed time.
			timeLabel = new Label {
				FontSize = 64,
				FontAttributes = FontAttributes.Bold,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.CenterAndExpand,
				TextColor = Color.Accent
			};

			// Put everything in a StackLayout.
			stackLayout = new StackLayout {
				BackgroundColor = Color.Black,
				Children = {
					new StackLayout {
						VerticalOptions = LayoutOptions.Fill,
						HorizontalOptions = LayoutOptions.FillAndExpand,
						Children = {
							new StackLayout {
								VerticalOptions = LayoutOptions.Fill,
								HorizontalOptions = LayoutOptions.FillAndExpand,
								Orientation = StackOrientation.Horizontal,
								Children = {
									newGameButton,
									randomizeButton
								}
							},
							timeLabel
						}
					},
					absoluteLayout
				}
			};
			stackLayout.SizeChanged += OnStackSizeChanged;

			// And set that to the content of the page.
			this.Padding = new Thickness (0, Device.OnPlatform (20, 0, 0), 0, 0);
			this.Content = stackLayout;
		}

		void OnStackSizeChanged (object sender, EventArgs args)
		{
			double width = stackLayout.Width;
			double height = stackLayout.Height;

			if (width <= 0 || height <= 0)
				return;

			// Orient StackLayout based on portrait/landscape mode.
			stackLayout.Orientation = (width < height) ? StackOrientation.Vertical :
				StackOrientation.Horizontal;

			// Calculate square size and position based on stack size.
			squareSize = Math.Min (width, height) / NUM;
			absoluteLayout.WidthRequest = NUM * squareSize;
			absoluteLayout.HeightRequest = NUM * squareSize;

			foreach (View view in absoluteLayout.Children) {
				XuzzleRandSquare square = (XuzzleRandSquare)view;
				square.SetLabelFont (0.4 * squareSize, FontAttributes.Bold);

				AbsoluteLayout.SetLayoutBounds (square,
					new Rectangle (square.Col * squareSize,
						square.Row * squareSize,
						squareSize,
						squareSize));
			}
		}

		async void OnSquareTapped (object parameter)
		{
			if (isBusy)
				return;

			isBusy = true;
			XuzzleRandSquare tappedSquare = (XuzzleRandSquare)parameter;
			await ShiftIntoEmpty (tappedSquare.Row, tappedSquare.Col);
			isBusy = false;

			// Check for a "win".
			if (isPlaying) {
				int index;

				for (index = 0; index < NUM * NUM - 1; index++) {
					int row = index / NUM;
					int col = index % NUM;
					XuzzleRandSquare square = squares [row, col];
					if (square == null || square.Index != index)
						break;
				}

				// We have a winner!
				if (index == NUM * NUM - 1) {
					isPlaying = false;
					await DoWinAnimation ();
				}
			}
		}

		async Task ShiftIntoEmpty (int tappedRow, int tappedCol, uint length = 100)
		{
			// Shift columns.
			if (tappedRow == emptyRow && tappedCol != emptyCol) {
				int inc = Math.Sign (tappedCol - emptyCol);
				int begCol = emptyCol + inc;
				int endCol = tappedCol + inc;

				for (int col = begCol; col != endCol; col += inc) {
					await AnimateSquare (emptyRow, col, emptyRow, emptyCol, length);
				}
			}
			// Shift rows.
			else if (tappedCol == emptyCol && tappedRow != emptyRow) {
				int inc = Math.Sign (tappedRow - emptyRow);
				int begRow = emptyRow + inc;
				int endRow = tappedRow + inc;

				for (int row = begRow; row != endRow; row += inc) {
					await AnimateSquare (row, emptyCol, emptyRow, emptyCol, length);
				}
			}
		}

		async Task AnimateSquare (int row, int col, int newRow, int newCol, uint length)
		{
			// The Square to be animated.
			XuzzleRandSquare animaSquare = squares [row, col];

			// The destination rectangle.
			Rectangle rect = new Rectangle (squareSize * emptyCol,
				squareSize * emptyRow,
				squareSize,
				squareSize);

			// This is the actual animation call.
			await animaSquare.LayoutTo (rect, length);

			// Set several variables and properties for new layout.
			squares [newRow, newCol] = animaSquare;
			animaSquare.Row = newRow;
			animaSquare.Col = newCol;
			squares [row, col] = null;
			emptyRow = row;
			emptyCol = col;
		}

		void OnNewGameButtonClicked (object sender, EventArgs args)
		{
			this.isPlaying = false;
			timeLabel.Text = null;

			stackLayout.Children.Remove (absoluteLayout);
			stackLayout.HorizontalOptions = LayoutOptions.Start;

			emptyRow = NUM - 1;
			emptyCol = NUM - 1;

			Array.Clear(squares, 0, squares.Length - 1);

			getRandomText();

			stackLayout.Children.Add (absoluteLayout);
			stackLayout.HorizontalOptions = LayoutOptions.Fill;
			stackLayout.SizeChanged += OnStackSizeChanged;
		}

		async void OnRandomizeButtonClicked (object sender, EventArgs args)
		{
			Button button = (Button)sender;
			button.IsEnabled = false;
			newGameButton.IsEnabled = false;
			Random rand = new Random ();

			isBusy = true;

			// Simulate some fast crazy taps.
			for (int i = 0; i < 100; i++) {
				await ShiftIntoEmpty (rand.Next (NUM), emptyCol, 25);
				await ShiftIntoEmpty (emptyRow, rand.Next (NUM), 25);
			}
			newGameButton.IsEnabled = true;
			button.IsEnabled = true;

			isBusy = false;

			// Prepare for playing.
			startTime = DateTime.Now;

			Device.StartTimer (TimeSpan.FromSeconds (1), () => {
				// Round duration and get rid of milliseconds.
				TimeSpan timeSpan = (DateTime.Now - startTime) + TimeSpan.FromSeconds (0.5);
				timeSpan = new TimeSpan (timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);

				// Display the duration.
				if (isPlaying)
					timeLabel.Text = timeSpan.ToString ("t");
				return isPlaying;
			});
			this.isPlaying = true;
		}

		async Task DoWinAnimation ()
		{
			// Inhibit all input.
			randomizeButton.IsEnabled = false;
			isBusy = true;

			for (int cycle = 0; cycle < 2; cycle++) {
				foreach (XuzzleRandSquare square in squares)
					if (square != null)
						await square.AnimateWinAsync (cycle == 1);

				if (cycle == 0)
					await Task.Delay (1500);
			}

			// All input.
			randomizeButton.IsEnabled = true;
			isBusy = false;
		}

		void getRandomText ()
		{
			// AbsoluteLayout to host the squares.
			absoluteLayout = new AbsoluteLayout () {
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center
			};

			using (reader) {
				// Seed a random number in the range 0 to your count.
				Random random = new Random();
				int randomNumber = random.Next(0, lines.Length);

				// Read the random line.
				text = lines.Skip(randomNumber - 1).Take(1).First().ToUpper().Trim();
				text = Regex.Replace (text, @"\s+", "");

				while (text.Length < 15) {
					randomNumber = random.Next(0, lines.Length);

					string text2 = lines.Skip(randomNumber - 1).Take(1).First().ToUpper().Trim();
					text2 = Regex.Replace (text2, @"\s+", "");
					text = string.Concat(text, text2);
				}

				text = text.Truncate (15);
			}

			// Create XuzzleSquare's for all the rows and columns.
			index = 0;

			for (int row = 0; row < NUM; row++) {
				for (int col = 0; col < NUM; col++) {
					// But skip the last one!
					if (row == NUM - 1 && col == NUM - 1)
						break;

					// Instantiate XuzzleSquare.
					square = new XuzzleRandSquare (text [index], winText [index], index) {
						Row = row,
						Col = col
					};

					// Add tap recognition.
					TapGestureRecognizer tapGestureRecognizer = new TapGestureRecognizer {
						Command = new Command (OnSquareTapped),
						CommandParameter = square
					};
					square.GestureRecognizers.Add (tapGestureRecognizer);

					// Add it to the array and the AbsoluteLayout.
					squares [row, col] = square;
					absoluteLayout.Children.Add (square);
					index++;
				}
			}
		}
	}
}
