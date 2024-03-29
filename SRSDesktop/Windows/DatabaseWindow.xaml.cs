﻿using SRSDesktop.Entities;
using SRSDesktop.Util;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SRSDesktop.Windows
{
	public partial class DatabaseWindow : Window
	{
		private List<Item> items;

		public Item ReturnValue { get; set; }


		public DatabaseWindow()
		{
			// TODO:
			//  row color by type
			//  reading col

			InitializeComponent();
			Update();

			cbLevel.SelectedValuePath = "Key";
			cbLevel.DisplayMemberPath = "Value";
			cbLevel.ItemsSource = Enumerable.Range(0, 61).ToDictionary(k => k, v => v == 0 ? "All" : $"Level {v}");

			tbSearch.Focus();
		}


		private void Update()
		{
			items = SRS.ReviewManager.GetAll();
			lsvDatabase.ItemsSource = items;
		}

		private void Filter()
		{
			if (lsvDatabase != null && items != null)
			{
				var result = items;

				if (tbSearch.Text != "") result = result.FindAll(i => (chkExact.IsChecked == false &&
					(i.Meaning.Contains(tbSearch.Text) || (i.Character != null && i.Character.Contains(tbSearch.Text)))) ||
					i.Meaning == tbSearch.Text || i.Character == tbSearch.Text || ReadingSearch(i, tbSearch.Text, chkExact.IsChecked == true));

				if (cbLevel.SelectedIndex > 0) result = result.FindAll(i => i.Level == ((KeyValuePair<int, string>)cbLevel.SelectedItem).Key);

				if (chkLearnable.IsChecked == true) result = result.FindAll(i => i.Learnable);

				if (chkRadicals.IsChecked == false) result = result.FindAll(i => !(i is Radical));
				if (chkKanji.IsChecked == false) result = result.FindAll(i => !(i is Kanji));
				if (chkVocab.IsChecked == false) result = result.FindAll(i => !(i is Vocab));

				result = result.FindAll(i => (i.UserSpecific == null && sldMinUserLvl.Value == 0) ||
					(i.UserSpecific?.SrsNumeric >= sldMinUserLvl.Value && i.UserSpecific?.SrsNumeric <= sldMaxUserLvl.Value));

				if (cbSort.SelectedIndex == 0) result = result.OrderBy(i => i.Level).ToList();
				else result = result.OrderByDescending(i => i.UserSpecific != null).ThenBy(i => i.UserSpecific?.AvailableDate).ToList();

				lblResultsCount.Content = result.Count + " results";
				lsvDatabase.ItemsSource = result;
			}
		}

		private bool ReadingSearch(Item item, string text, bool exact = false)
		{
			var kana = text.ToHiragana();

			if (item is Kanji kanji)
			{
				if (exact) return kanji.Onyomi == kana || kanji.Kunyomi == kana || kanji.Nanori == kana;
				return kanji.Onyomi?.Contains(kana) == true || kanji.Kunyomi?.Contains(kana) == true || kanji.Nanori?.Contains(kana) == true;
			}
			else if (item is Vocab vocab)
			{
				return exact ? vocab.Kana == kana : vocab.Kana.Contains(kana);
			}

			return false;
		}

		private void BtnConfirmClick(object sender, RoutedEventArgs e)
		{
			SRS.ReviewManager.Save();

			if (lsvDatabase.SelectedItem == null)
			{
				MessageBox.Show("Nothing's selected");
				return;
			}

			ReturnValue = (Item)lsvDatabase.SelectedItem;
			DialogResult = true;
		}

		private void LsvDatabaseMouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			var item = (Item)lsvDatabase.SelectedItem;
			if (item == null) return;
			var detailsWindow = new DetailsWindow(item);
			if (detailsWindow.ShowDialog() == true)
			{
				lsvDatabase.Items.Refresh();
			}
		}

		private void BtnCurrLevelClick(object sender, RoutedEventArgs e)
		{
			cbLevel.SelectedIndex = SRS.LessonManager.UserLevel;
		}

		private void BtnSaveClick(object sender, RoutedEventArgs e)
		{
			SRS.ReviewManager.Save();
			Update();
		}

		private void SldMinUserLvlValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (sldMinUserLvl != null && sldMaxUserLvl != null)
			{
				sldMaxUserLvl.Minimum = sldMinUserLvl.Value;
				lblUserLevelValue.Content = $"{sldMinUserLvl.Value}-{sldMaxUserLvl.Value}";
				Filter();
			}
		}

		private void SldMaxUserLvlValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (sldMinUserLvl != null && sldMaxUserLvl != null)
			{
				sldMinUserLvl.Maximum = sldMaxUserLvl.Value;
				lblUserLevelValue.Content = $"{sldMinUserLvl.Value}-{sldMaxUserLvl.Value}";
				Filter();
			}
		}

		private void BtnBelowUnlockClick(object sender, RoutedEventArgs e)
		{
			sldMinUserLvl.Value = 1;
			sldMaxUserLvl.Value = SRS.LessonManager.UnlockLevel - 1;
		}

		private void BtnResetClick(object sender, RoutedEventArgs e)
		{
			tbSearch.Text = "";
			chkExact.IsChecked = false;
			chkRadicals.IsChecked = true;
			chkKanji.IsChecked = true;
			chkVocab.IsChecked = true;
			chkLearnable.IsChecked = false;
			cbLevel.SelectedIndex = 0;
			sldMinUserLvl.Value = 0;
			sldMaxUserLvl.Value = sldMaxUserLvl.Maximum;
			cbSort.SelectedIndex = 0;
			tbSearch.Focus();
		}

		private void TextBoxTextChanged(object sender, TextChangedEventArgs e) => Filter();

		private void ChkExactClick(object sender, RoutedEventArgs e) => Filter();

		private void ChkRadicalsClick(object sender, RoutedEventArgs e) => Filter();

		private void ChkKanjiClick(object sender, RoutedEventArgs e) => Filter();

		private void ChkVocabClick(object sender, RoutedEventArgs e) => Filter();

		private void CbLevelSelectionChanged(object sender, SelectionChangedEventArgs e) => Filter();

		private void CbStatusSelectionChanged(object sender, SelectionChangedEventArgs e) => Filter();

		private void ChkLearnableClick(object sender, RoutedEventArgs e) => Filter();

		private void CbSortSelectionChanged(object sender, SelectionChangedEventArgs e) => Filter();
	}
}
