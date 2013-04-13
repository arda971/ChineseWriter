﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Key = System.Windows.Input.Key;
using RT = clojure.lang.RT;
using Keyword = clojure.lang.Keyword;

namespace ChineseWriter {

    internal class WritingState {

        private int _cursorPos;
        private IDictionary<object,object>[] _words;

        // Observables
        public Subject<IDictionary<object, object>[]> WordsChanges =
            new Subject<IDictionary<object, object>[]>( );
        public Subject<int> CursorPosChanges = new Subject<int>( );

        public IDictionary<object,object>[] Words { 
            get { return _words; }
            set { 
                _words = value;
                if (_cursorPos > _words.Length) CursorPos = _words.Length;
                WordsChanges.OnNext( _words ); 
            }
        }

        public int CursorPos {
            get { return _cursorPos; }
            set {
                if ( value != _cursorPos ) {
                    _cursorPos = value; 
                    CursorPosChanges.OnNext( value ); 
                }
            }
        }

        // Constructor
        public WritingState( ) {
        }

        public void LoadText() {
            if (File.Exists( TextSaveFileName )) {
                var data = RT.var( "Utils", "load-from-file" ).invoke( TextSaveFileName );
                Words = ( (IEnumerable<object>)data ).Cast<IDictionary<object, object>>( ).ToArray( );
                CursorPos = Words.Length;
            }
        }

        public IEnumerable<IDictionary<object, object>> Suggestions( string input, bool english ) {
            var findWords = RT.var( "WordDatabase", "find-words" );
            if (findWords.isBound) {
                var method = english ? "find-words-english" : "find-words";
                var suggestions = (IEnumerable<object>)RT.var( "WordDatabase", method ).invoke( input );
                // Do *not* cast suggestions to list or Array here! That kills the performance since it forces
                // the lazy list to be fully evaluated
                return suggestions.Cast<IDictionary<object, object>>( );
            } else {
                return new IDictionary<object, object>[] {};
            }
        }

        public void TextEdit( Key key ) {
            switch (key ) {
                case Key.Back: BackSpace(); break;
                case Key.Delete: Delete(); break;
                case Key.Left: CursorPos--; break;
                case Key.Right: CursorPos++; break;
                case Key.Home: CursorPos = 0; break;
                case Key.End: CursorPos = Words.Length; break;
            }
            if (CursorPos < 0) CursorPos = 0;
            if (CursorPos > Words.Length) CursorPos = Words.Length;
        }

        private void Delete() {
            if (CursorPos < Words.Length) {
                Words = Words.Take(CursorPos).
                    Concat(Words.Skip(CursorPos + 1)).
                    ToArray();
            }            
        }

        private void BackSpace() {
            if (CursorPos > 0) {
                var orignalCursor = CursorPos;
                Words = Words.Take( orignalCursor - 1 ).
                    Concat( Words.Skip( orignalCursor ) ).
                    ToArray();
                CursorPos = orignalCursor - 1;
            }
        }

        internal void LiteralInput( string text ) {
            var words = (IEnumerable<object>)RT.var( "WordDatabase", "hanyu-to-words" ).invoke( text );
            InsertWords( words.Cast<IDictionary<object, object>>( ).ToArray( ) );
        }

        internal void SelectWord( IDictionary<object,object> word ) {
            InsertWords( new IDictionary<object,object>[] { word } );
            WordDatabase.IncreaseUsageCount( word );
        }

        private void InsertWords( IDictionary<object,object>[] newWords ) {
            Words = Words.Take( CursorPos ).
                Concat( newWords.Select( w => ExpandChars( w ) ) ).
                Concat( Words.Skip( CursorPos ) ).
                ToArray( );
            CursorPos = CursorPos + newWords.Count();
        }

        public void RefreshInfo( ) {
            Words = Words.Select( w => ExpandChars( w ) ).ToArray();
        }

        private IDictionary<object,object> ExpandChars(IDictionary<object,object> word) {
            return word.HasKeyword( "hanyu" ) && word.HasKeyword( "pinyin" ) ?
                (IDictionary<object,object>) RT.var( "WordDatabase", "expanded-word" ).
                    invoke( word.Hanyu(), word.Pinyin() ) :
                word;
        }

        private static string PadHanyu( string s, int latinLength ) {
            int hanyuExtraLen = s.Replace( " ", "" ).Length / 2;
            return s.PadRight( latinLength - hanyuExtraLen );
        }

        public string HanyiPinyinLines { 
            get {
                var hanyus = Words.Select( word => word.Hanyu());
                var pinyins = Words.Select( word => word.PinyinDiacritics());
                var english = Words.Select( word => word.ShortEnglish()); 
                var lengths = hanyus.Select( word => word.Length + 1 )
                    .Zip( pinyins, (l1,s2) => Math.Max(l1, s2.Length + 1))
                    .Zip( english, (l1,s2) => Math.Max(l1, s2.Length + 1));
                // could not think of way to do this with the base LINQ funcs:
                var cumulativeLengths = new List<int>();
                int cumulativeLength = 0;
                foreach( int length in lengths) {
                    cumulativeLength += length;
                    cumulativeLengths.Add( cumulativeLength );
                }
                // Chinese characters in a monospaced font are ~1.5 times as wide as Latin
                // characters in the same font, so take account into padding:
                var hanyuLine = hanyus
                    .Zip( cumulativeLengths, ( h, len ) => Tuple.Create( h, len ) )
                    .Aggregate( "", ( hanyu, tuple ) => PadHanyu( hanyu + tuple.Item1, tuple.Item2 ) );
                var pinyinLine = string.Join( "", pinyins.Zip(lengths, (h,len) => h.PadRight(len)));
                var englishLine = string.Join( "", english.Zip(lengths, (h,len) => h.PadRight(len)));
                return hanyuLine + "\n" + pinyinLine + "\n" + englishLine;
            } 
        }

        public string Html {
            get {
                return "<table style='border: 1px solid #d0d0d0; border-collapse:collapse;' cellpadding='4'>" +
                    "<tr>" + HanyuHtml + "</tr>" +
                    "<tr>" + PinyinHtml + "</tr>" +
                    "<tr>" + EnglishHtml +"</tr>" +
                    "</table>";
            }
        }

        private string HanyuHtml {
            get {
                return string.Join( "", 
                    Words.Select( word => WordCell( word.HanyuHtml(), "font-size:20pt;" ) ).ToArray() );
            }
        }

        private string PinyinHtml {
            get {
                return string.Join( "", 
                    Words.Select( word => WordCell( word.PinyinHtml() ) ).ToArray( ) );
            }
        }

        private string EnglishHtml {
            get {
                return string.Join( "",
                    Words.Select( word => WordCell( word.EnglishHtml(), "color:#808080; font-size:9pt;" ) ).ToArray( ) );
            }
        }

        public static string WordCell( string content, string attr = "" ) {
            return string.Format("<td style='{0}'>{1} </td>", attr, content);
        }

        internal void Clear( ) {
            Words = new IDictionary<object,object>[] { };
            CursorPos = 0;
        }

        internal string Hanyu {
            get { return string.Join( "", Words.Select( word => word.Get<string>("hanyu") ).ToArray( ) ); }
        }

        internal string TextSaveFileName {
            get {
                return Path.Combine( WordDatabase.ExeDir.ToString( ), "text.clj" );
            }
        }

        internal void SaveCurrentText( ) {
            var content = (string) RT.var( "Utils", "list-to-str" ).invoke( Words );
            File.WriteAllText( TextSaveFileName, content, Encoding.UTF8 );
        }
    } // class

} // namespace
