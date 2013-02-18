﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ChineseWriter {

    class WordPanel : UserControl {

        private static readonly Color[] TONE_COLORS = { 
            Color.FromRgb(255,0,0), Color.FromRgb(160,160,0), Color.FromRgb(0,180,0), 
            Color.FromRgb(0,0,255), Colors.Black };

        private Word _word;
        private WordDatabase _wordsDb;

        public Word Word { get { return _word; } }

        // TODO: Make Word-class method CreatePanel(), less logic here...
        public WordPanel( Word word, WordDatabase wordsDb, bool big = false ) {
            _word = word;
            _wordsDb = wordsDb;
            var panel = new StackPanel {
                Orientation = Orientation.Vertical,
                Background = new SolidColorBrush( word.PanelColor ),
                MaxWidth = 150
            };
            // Hanyu text
            var hanyuText = new TextBlock {
                FontFamily = new FontFamily( "SimSun" ), FontSize = big ? 50 : 30,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            if (word is HanyuWord) {
                hanyuText.Inlines.AddRange(
                    ( word as HanyuWord ).Characters.
                    Select( c => new Run { 
                        Text = c.Item1, 
                        Foreground = new SolidColorBrush( ToneColor(c.Item2) ) } ) );
            } else {
                hanyuText.Text = word.Hanyu;
            }
            panel.Children.Add( hanyuText );
            // Pinyin text
            var pinyinText = new TextBlock {
                FontFamily = new FontFamily( "Times New Roman" ), 
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            if (word is HanyuWord) {
                pinyinText.Inlines.AddRange(
                    ( word as HanyuWord ).Characters.
                    Select( c => new Run {
                        Text = " " + c.Item2.AddToneDiacritics() + " ",
                        Foreground = new SolidColorBrush( ToneColor( c.Item2 ) )
                    } ) );
            } else {
                pinyinText.Text = word.DisplayPinyin;
            }
            panel.Children.Add( pinyinText );
            panel.Children.Add( new TextBlock {
                Padding = new Thickness( 4.0 ),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Text = big ? word.English : word.ShortEnglish,
                Foreground = new SolidColorBrush( Color.FromArgb( 192, 0, 0, 0 ) )
            } );
            if (_word is HanyuWord) {
                panel.ToolTip = CreateExplanationPanel( _word as HanyuWord );
            }
            this.Content = GuiUtils.WrapToBorder( panel );
        }

        private static Color ToneColor( string pinyin ) {
            var lastChar = pinyin.TakeLast( );
            int tone;
            if (!int.TryParse( lastChar, out tone )) return Colors.Gray;
            return TONE_COLORS[tone - 1];            
        }

        private object CreateExplanationPanel( HanyuWord word ) {
            if (word.Hanyu.Length == 1) {
                return word.English;
            } else {
                var panel = new StackPanel { Orientation = Orientation.Vertical };
                panel.Children.Add( new Label { Content = word.English } );
                var detailsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add( detailsPanel );
                foreach (FrameworkElement childPanel in
                    word.Characters.
                        Select( c => _wordsDb.WordForHanyuPinyin( c.Item1, c.Item2 ) ).
                        Select( w => new WordPanel( w, _wordsDb, big: true ) ))
                    detailsPanel.Children.Add( childPanel );
                return panel;
            }
        }

    }
}
