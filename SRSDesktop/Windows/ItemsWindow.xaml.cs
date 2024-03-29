﻿using NAudio.Vorbis;
using NAudio.Wave;
using SRSDesktop.Entities;
using SRSDesktop.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SRSDesktop.Windows
{
	public partial class ItemsWindow : Window
	{
		private readonly Brush radicalBrush = (Brush)new BrushConverter().ConvertFrom("#FFE9F4FF");
		private readonly Brush kanjiBrush = (Brush)new BrushConverter().ConvertFrom("#FFFF9BFF");
		private readonly Brush vocabBrush = (Brush)new BrushConverter().ConvertFrom("#FFC79BFF");
		private readonly Thickness defaultThickness = new Thickness(1);
		private readonly Thickness highlightThickness = new Thickness(3);
		private readonly Brush defaultBorderBrush = SystemColors.ControlDarkDarkBrush;
		private readonly Brush highlightBorderBrush = SystemColors.HighlightBrush;
		private readonly string[] buttonText = new string[] { "Very bad", "Bad", "Again", "Good", "Easy" };

		private TimeSpan TimeSpent;
		private DateTime TimeActivated;
		private VorbisWaveReader VorbisWaveReader;
		private WaveOutEvent WaveOutEvent;

		private List<Item> Items { get; set; }
		private Dictionary<Item, int> LevelChange { get; set; }
		private int CurrentIndex { get; set; }
		private Item CurrentItem => Items[CurrentIndex];
		private ItemsWindowMode CurrentMode { get; set; }
		private ItemsWindowMode Mode { get; }
		private State AppState { get; set; }

		public ItemsWindow()
		{
			InitializeComponent();
		}

		public ItemsWindow(List<Item> items, ItemsWindowMode mode) : this()
		{
			Items = items;
			LevelChange = new Dictionary<Item, int>();
			CurrentIndex = 0;
			CurrentMode = mode;
			Mode = mode;

			switch (CurrentMode)
			{
				case ItemsWindowMode.Lesson:
					ShowLessonControls();
					break;
				case ItemsWindowMode.Review:
					break;
				case ItemsWindowMode.View:
					throw new NotImplementedException("View mode is not implemented");
				default:
					throw new ArgumentException("Unsupported ItemsWindowMode");
			}

			WaitForInput();
		}

		private void WaitForInput()
		{
			if (CurrentIndex >= Items.Count)
			{
				End();
				return;
			}

			AppState = State.AwaitInput;
			TaskbarItemInfo.ProgressValue = (float)CurrentIndex / Items.Count;

			SetWindowTitle();
			EnableInputControls();
			ClearInputControls();
			DisableAnswerControls();
			ClearAnswerTextBlocks();
			SetItemCharacter();
			SetItemBackgroundColor();
			HideItemLvlInfo();

			if (CurrentMode != ItemsWindowMode.Review)
			{
				DisplayAnswer();
			}
		}

		private void DisplayAnswer()
		{
			AppState = State.DisplayAnswer;

			EnableAnswerControls();
			DisableInputControls();
			ShowItemLvlInfo();
			FillAnswerTextBlock(CurrentItem);
		}

		private void AcceptAnswer(int levelChange)
		{
			var alreadyRepeated = LevelChange.ContainsKey(CurrentItem) && LevelChange[CurrentItem] <= 0;
			var readd = levelChange == 0 || (levelChange < 0 && !alreadyRepeated);

			if (levelChange != 0 || Mode == ItemsWindowMode.Lesson)
			{
				LevelChange[CurrentItem] = levelChange;
			}

			if (Mode == ItemsWindowMode.Lesson)
			{
				readd = levelChange < 0;
			}

			SkipAnswer(readd);
		}

		private void SkipAnswer(bool readd = false)
		{
			if (readd)
			{
				Items.Add(CurrentItem);
				Items.RemoveAt(CurrentIndex);
			}
			else
			{
				CurrentIndex++;
			}

			WaitForInput();
		}

		private void Answer()
		{
			DisplayAnswer();
		}

		private void End()
		{
			if (LevelChange.Count > 0)
			{
				if (!Finish()) return;
				Summary();
				DialogResult = true;
			}
			else
			{
				DialogResult = false;
			}
		}

		private bool Finish()
		{
			if (CurrentMode == ItemsWindowMode.Lesson)
			{
				CurrentMode = ItemsWindowMode.Review;
				Items = LevelChange.Keys.ToList();
				LevelChange = new Dictionary<Item, int>();
				CurrentIndex = 0;

				ShowLessonReviewControls();
				WaitForInput();

				return false;
			}

			foreach (var item in LevelChange)
			{
				if (item.Key.UserSpecific == null)
				{
					item.Key.UserSpecific = new UserSpecific(item.Key);
					item.Key.Learnable = false;
				}

				var value = item.Value;
				if (value < 0) value++;

				item.Key.UserSpecific.AddProgress(value);

				if (value <= 0 && item.Key.UserSpecific.SrsNumeric >= 3)
				{
					item.Key.UserSpecific.Again();
				}
			}

			return true;
		}

		private void Summary()
		{
			if (Mode == ItemsWindowMode.Lesson)
			{
				var items = LevelChange.Keys;
				new SummaryWindow(new Item[0], items).ShowDialog();
				SRS.StatsManager.AddStats((int)TimeSpent.TotalSeconds, items.Count());
			}
			else
			{
				var badItems = LevelChange.Where(i => i.Value <= 0/* || (i.Key.UserSpecific?.SrsNumeric == 1 && i.Value == 0)*/).Select(i => i.Key); // count 1 lvl okay as bad
				var goodItems = LevelChange.Where(i => i.Value > 0/* && !(i.Key.UserSpecific?.SrsNumeric == 1 && i.Value == 0)*/).Select(i => i.Key);
				new SummaryWindow(badItems, goodItems).ShowDialog();
				SRS.StatsManager.AddStats((int)TimeSpent.TotalSeconds, goodItems.Count(), badItems.Count());
			}
		}

		private void PlaySound()
		{
			if (WaveOutEvent != null)
			{
				VorbisWaveReader.Position = 0;
				WaveOutEvent.Play();
			}
		}

		#region Controls

		private void SetWindowTitle()
		{
			Title = $"{CurrentMode} [{CurrentIndex + 1}/{Items.Count}]";
		}

		private void EnableInputControls()
		{
			buttonAnswer.IsEnabled = true;
			buttonPrev.IsEnabled = CurrentIndex > 0;
			buttonSkip.Content = CurrentIndex < Items.Count - 1 ? "Next >" : "Finish";
		}

		private void DisableInputControls()
		{
			buttonAnswer.IsEnabled = false;
			buttonPrev.IsEnabled = CurrentIndex > 0;
			buttonSkip.Content = CurrentIndex < Items.Count - 1 ? "Next >" : "Finish";
		}

		private void EnableAnswerControls()
		{
			if (CurrentMode != ItemsWindowMode.Review) return;

			var index = 0;

			buttonDetails.IsEnabled = true;

			if (Mode == ItemsWindowMode.Lesson)
			{
				index = 2;

				buttonBad.IsEnabled = true;
				buttonBad.Content = $"{buttonText[index++]}";

				buttonOkay.IsEnabled = true;
				buttonOkay.Content = $"{buttonText[index++]} ({UserSpecific.GetLevelInfo(1).TimeString})";

				buttonGood.IsEnabled = true;
				buttonGood.Content = $"{buttonText[index++]} ({UserSpecific.GetLevelInfo(2).TimeString})";

				return;
			}

			var srsNumeric = CurrentItem.UserSpecific.SrsNumeric;

			if (CurrentItem.UserSpecific.SrsNumeric > 2)
			{
				buttonVeryBad.IsEnabled = true;
				buttonVeryBad.Content = $"{buttonText[index]}";
			}

			index++;

			buttonBad.IsEnabled = true;
			buttonBad.Content = $"{buttonText[index++]}";

			buttonOkay.IsEnabled = true;
			buttonOkay.Content = $"{buttonText[index++]}";

			buttonGood.IsEnabled = true;
			buttonGood.Content = $"{buttonText[index++]} ({UserSpecific.GetLevelInfo(srsNumeric + 1).TimeString})";

			if (CurrentItem.UserSpecific.SrsNumeric < 8)
			{
				buttonEasy.IsEnabled = true;
				buttonEasy.Content = $"{buttonText[index++]} ({UserSpecific.GetLevelInfo(srsNumeric + 2).TimeString})";
			}

			SetAnswerControlsStyle();
		}

		private void DisableAnswerControls()
		{
			if (CurrentMode != ItemsWindowMode.Review) return;
			var index = Mode == ItemsWindowMode.Lesson ? 1 : 0;

			buttonVeryBad.IsEnabled = false;
			buttonVeryBad.Content = buttonText[index++];

			buttonBad.IsEnabled = false;
			buttonBad.Content = buttonText[index++];

			buttonOkay.IsEnabled = false;
			buttonOkay.Content = buttonText[index++];

			buttonGood.IsEnabled = false;
			buttonGood.Content = buttonText[index++];

			if (Mode != ItemsWindowMode.Lesson)
			{
				buttonEasy.IsEnabled = false;
				buttonEasy.Content = buttonText[index++];
			}

			buttonDetails.IsEnabled = false;

			ClearAnswerControlsStyle();
		}

		private void SetAnswerControlsStyle()
		{
			var levelChange = LevelChange.TryGetValue(CurrentItem, out var value) ? (int?)value : null;

			buttonVeryBad.BorderThickness = levelChange == -2 ? highlightThickness : defaultThickness;
			buttonVeryBad.BorderBrush = levelChange == -2 ? highlightBorderBrush : defaultBorderBrush;

			buttonBad.BorderThickness = levelChange == -1 ? highlightThickness : defaultThickness;
			buttonBad.BorderBrush = levelChange == -1 ? highlightBorderBrush : defaultBorderBrush;

			buttonOkay.BorderThickness = levelChange == 0 ? highlightThickness : defaultThickness;
			buttonOkay.BorderBrush = levelChange == 0 ? highlightBorderBrush : defaultBorderBrush;

			buttonGood.BorderThickness = levelChange == 1 ? highlightThickness : defaultThickness;
			buttonGood.BorderBrush = levelChange == 1 ? highlightBorderBrush : defaultBorderBrush;

			buttonEasy.BorderThickness = levelChange == 2 ? highlightThickness : defaultThickness;
			buttonEasy.BorderBrush = levelChange == 2 ? highlightBorderBrush : defaultBorderBrush;
		}

		private void ClearAnswerControlsStyle()
		{
			buttonVeryBad.BorderThickness = buttonBad.BorderThickness = buttonOkay.BorderThickness = buttonGood.BorderThickness = buttonEasy.BorderThickness = defaultThickness;
			buttonVeryBad.BorderBrush = buttonBad.BorderBrush = buttonOkay.BorderBrush = buttonGood.BorderBrush = buttonEasy.BorderBrush = defaultBorderBrush;
		}

		private void ShowLessonControls()
		{
			buttonVeryBad.Visibility = Visibility.Hidden;
			buttonBad.Visibility = Visibility.Hidden;
			buttonOkay.Content = "Learn";
			buttonGood.Visibility = Visibility.Hidden;
			buttonEasy.Visibility = Visibility.Hidden;
		}

		private void ShowLessonReviewControls()
		{
			buttonBad.Visibility = Visibility.Visible;
			buttonGood.Visibility = Visibility.Visible;
		}

		private void ClearInputControls()
		{
			textBlockCharacter.Text = "";
			imageCharacter.Source = null;
			labelSmallCharacter.Content = "";
		}

		private void ClearAnswerTextBlocks()
		{
			textBlockInfo.Text = "";

			DisposeAudio();
		}

		private void FillAnswerTextBlock(Item item)
		{
			List<Run> runs;

			scrollViewer.ScrollToTop();

			if (item is Radical radical)
			{
				runs = GenerateRuns("Meaning", radical.Meaning);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateMeaningRuns("Mnemonic", radical.Mnemonic);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateRuns("Kanji examples", string.Join(", ", radical.Related.Take(5).Select(k => k.Character + " - " + k.Meanings[0])));
				textBlockInfo.Inlines.AddRange(runs);
			}
			else if (item is Kanji kanji)
			{
				runs = GenerateRuns("Meaning", kanji.Meaning);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateReadingRuns("Reading", kanji);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateRuns("Radicals", string.Join(" + ", kanji.Related.OfType<Radical>().Select(r => r.Character == null ? r.Meaning : r.Character + " " + r.Meaning)));
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateMeaningRuns("Meaning mnemonic", kanji.MeaningMnemonic);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateMeaningRuns("Reading mnemonic", kanji.ReadingMnemonic);
				textBlockInfo.Inlines.AddRange(runs);

				var similar = kanji.Related.OfType<Kanji>();
				if (similar.Any())
				{
					runs = GenerateRuns("Visually similar", string.Join(", ", similar.Select(k => k.Character + " - " + k.Meaning)));
					textBlockInfo.Inlines.AddRange(runs);
				}

				runs = GenerateRuns("Vocabulary examples", string.Join(", ", kanji.Related.OfType<Vocab>().Take(5).Select(v => v.Character + " - " + v.Meanings[0])));
				textBlockInfo.Inlines.AddRange(runs);
			}
			else if (item is Vocab vocab)
			{
				runs = GenerateRuns("Meaning", vocab.Meaning);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateRuns("Reading", vocab.Kana);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateRuns("Part of speech", vocab.PartOfSpeech);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateMeaningRuns("Meaning explanation", vocab.MeaningExplanation);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateMeaningRuns("Reading explanation", vocab.ReadingExplanation);
				textBlockInfo.Inlines.AddRange(runs);
				runs = GenerateRuns("Context sentences", string.Join(Environment.NewLine + Environment.NewLine, vocab.ContextSentences.Select(cs => cs.Japanese + Environment.NewLine + cs.English)));
				textBlockInfo.Inlines.AddRange(runs);

				var soundPath = Utils.GetResourcesPath() + "Sound/" + item.Character + "_2.ogg";
				if (File.Exists(soundPath))
				{
					VorbisWaveReader = new VorbisWaveReader(soundPath);
					WaveOutEvent = new WaveOutEvent();
					WaveOutEvent.Init(VorbisWaveReader);
					PlaySound();
				}
			}
		}

		private void SetItemCharacter()
		{
			if (string.IsNullOrWhiteSpace(CurrentItem.Character) && CurrentItem is Radical radical)
			{
				var imageUrl = Utils.GetResourcesPath() + "Images\\" + radical.Image.Split('/').Last();
				if (File.Exists(imageUrl))
				{
					var bitmap = new System.Drawing.Bitmap(imageUrl);
					Utils.InvertColors(bitmap);
					imageCharacter.Source = Utils.ToBitmapImage(bitmap);
				}
				else
				{
					MessageBox.Show(radical.Image + " not found");
				}
			}
			else
			{
				textBlockCharacter.Text = CurrentItem.Character;
				textBlockCharacter.FontSize = 150 / CurrentItem.Character.Length;

				labelSmallCharacter.Content = CurrentItem.Character;
			}
		}

		private void ShowItemLvlInfo()
		{
			labelItemLvlText.Visibility = Visibility.Visible;
			labelItemLvl.Visibility = Visibility.Visible;
			labelItemLvl.Content = CurrentItem.Level;

			if (CurrentItem.UserSpecific != null)
			{
				var lvlInfo = UserSpecific.GetLevelInfo(CurrentItem.UserSpecific.SrsNumeric);
				labelUserLvlText.Visibility = Visibility.Visible;
				labelUserLvl.Visibility = Visibility.Visible;
				labelUserLvl.Content = lvlInfo.SrsLevel.ToString() + " " + CurrentItem.UserSpecific.SrsNumeric;
			}
		}

		private void HideItemLvlInfo()
		{
			labelItemLvlText.Visibility = Visibility.Hidden;
			labelItemLvl.Visibility = Visibility.Hidden;

			labelUserLvlText.Visibility = Visibility.Hidden;
			labelUserLvl.Visibility = Visibility.Hidden;
		}

		private void SetItemBackgroundColor()
		{
			if (CurrentItem is Radical)
				rectItem.Fill = radicalBrush;
			else if (CurrentItem is Kanji)
				rectItem.Fill = kanjiBrush;
			else if (CurrentItem is Vocab)
				rectItem.Fill = vocabBrush;
		}

		private void ButtonAnswerClick(object sender, RoutedEventArgs e)
		{
			Answer();
		}

		private void ButtonSkipClick(object sender, RoutedEventArgs e)
		{
			SkipAnswer();
		}

		private void ButtonVeryBadClick(object sender, RoutedEventArgs e)
		{
			AcceptAnswer(-2);
		}

		private void ButtonBadClick(object sender, RoutedEventArgs e)
		{
			AcceptAnswer(-1);
		}

		private void ButtonOkayClick(object sender, RoutedEventArgs e)
		{
			AcceptAnswer(0);
		}

		private void ButtonGoodClick(object sender, RoutedEventArgs e)
		{
			AcceptAnswer(1);
		}

		private void ButtonEasyClick(object sender, RoutedEventArgs e)
		{
			AcceptAnswer(2);
		}

		private void CharacterClick(object sender, MouseButtonEventArgs e)
		{
			PlaySound();
		}

		private void ButtonPrevClick(object sender, RoutedEventArgs e)
		{
			if (CurrentIndex > 0)
			{
				CurrentIndex--;
				WaitForInput();
			}
		}

		private void ButtonDetailsClick(object sender, RoutedEventArgs e)
		{
			var detailsWindow = new DetailsWindow(CurrentItem);

			if (detailsWindow.ShowDialog() == true)
			{
				Items.AddRange(detailsWindow.ToEnqueue.FindAll(i => !Items.Contains(i)));
				SetWindowTitle();
				ClearAnswerTextBlocks();
				FillAnswerTextBlock(CurrentItem);
				EnableAnswerControls();
			}
		}

		private void WindowKeyUp(object sender, KeyEventArgs e)
		{
			if (AppState == State.AwaitInput)
			{
				switch (e.Key)
				{
					case Key.Enter:
					case Key.Space:
						Answer();
						break;
					case Key.Tab:
						SkipAnswer(true);
						break;
				}
			}
			else if (AppState == State.DisplayAnswer)
			{
				switch (e.Key)
				{
					case Key.Enter:
					case Key.Space:
						PlaySound();
						break;
					case Key.Tab:
						SkipAnswer(true);
						break;
					case Key.D1:
						AcceptAnswer(-2);
						break;
					case Key.D2:
						AcceptAnswer(-1);
						break;
					case Key.D3:
						AcceptAnswer(0);
						break;
					case Key.D4:
						AcceptAnswer(1);
						break;
					case Key.D5:
						AcceptAnswer(2);
						break;
				}
			}
		}

		#endregion

		#region Generate runs

		private List<Run> GenerateRuns(string label, string text)
		{
			var result = new List<Run>();

			result.Add(new Run() { Text = label + Environment.NewLine, Foreground = Brushes.Gray });
			result.Add(new Run(text + Environment.NewLine + Environment.NewLine));

			return result;
		}

		private List<Run> GenerateReadingRuns(string label, Kanji kanji)
		{
			var result = new List<Run>();

			result.Add(new Run() { Text = label + Environment.NewLine, Foreground = Brushes.Gray });

			if (!string.IsNullOrEmpty(kanji.Onyomi))
			{
				var run = new Run($"Onyomi: {kanji.Onyomi}  ");
				if (kanji.ImportantReading != ReadingType.Onyomi) run.Foreground = Brushes.DarkGray;
				result.Add(run);
			}

			if (!string.IsNullOrEmpty(kanji.Kunyomi))
			{
				var run = new Run($"Kunyomi: {kanji.Kunyomi}  ");
				if (kanji.ImportantReading != ReadingType.Kunyomi) run.Foreground = Brushes.DarkGray;
				result.Add(run);
			}

			if (!string.IsNullOrEmpty(kanji.Nanori)) result.Add(new Run { Text = $"Nanori: {kanji.Nanori}", Foreground = Brushes.DarkGray });

			result.Add(new Run(Environment.NewLine + Environment.NewLine));

			return result;
		}

		private List<Run> GenerateMeaningRuns(string label, string text)
		{
			var result = new List<Run>();

			result.Add(new Run() { Text = label + Environment.NewLine, Foreground = Brushes.Gray });

			var split = Regex.Split(text, @"(\[[^\[\]]+\])").Where(s => !string.IsNullOrEmpty(s));
			foreach (var s in split)
			{
				if (s.StartsWith("["))
				{
					var res = s.Trim('[', ']');
					var fgBrush = (Brush)Brushes.White;
					var bgBrush = (Brush)Brushes.Gray;
					if (s[2] == ':')
					{
						res = res.Substring(2);
						switch (s[1])
						{
							case 'r': bgBrush = radicalBrush; fgBrush = Brushes.Gray; break;
							case 'k': bgBrush = kanjiBrush; break;
							case 'v': bgBrush = vocabBrush; break;
						}
					}
					result.Add(new Run() { Text = res, Foreground = fgBrush, Background = bgBrush });
				}
				else
				{
					result.Add(new Run(s));
				}
			}

			result.Add(new Run(Environment.NewLine + Environment.NewLine));

			return result;
		}

		#endregion

		private void DisposeAudio()
		{
			VorbisWaveReader?.Dispose();
			WaveOutEvent?.Dispose();
			VorbisWaveReader = null;
			WaveOutEvent = null;
		}

		private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (!DialogResult.HasValue && LevelChange.Count > 0)
			{
				var isLesson = CurrentMode == ItemsWindowMode.Lesson;
				var text = isLesson ? "Do a review? Progress can only be saved after doing a review" : "Save progress?";
				var result = MessageBox.Show(text, "Exit", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
				if (result == MessageBoxResult.Cancel)
				{
					e.Cancel = true;
					return;
				}
				if (result == MessageBoxResult.Yes)
				{
					if (isLesson) e.Cancel = true;
					End();
				}
			}

			DisposeAudio();
		}

		private void WindowActivated(object sender, EventArgs e)
		{
			TimeActivated = DateTime.Now;
		}

		private void WindowDeactivated(object sender, EventArgs e)
		{
			TimeSpent += DateTime.Now - TimeActivated;
		}


		private enum State
		{
			DisplayAnswer,
			AwaitInput
		}
	}
}
