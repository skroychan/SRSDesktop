﻿namespace SRSDesktop.Entities
{
	public class Kanji : Item
	{
		public string Onyomi { get; set; }
		public string Kunyomi { get; set; }
		public string Nanori { get; set; }

		public ReadingType ImportantReading { get; set; }

		public string ReadingMnemonic { get; set; }
		public string ReadingHint { get; set; }

		public string MeaningMnemonic { get; set; }
		public string MeaningHint { get; set; }
	}
}