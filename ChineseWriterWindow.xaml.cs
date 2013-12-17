﻿using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Reactive.Linq;
using System.Threading;
using RT = clojure.lang.RT;

namespace ChineseWriter {

    public partial class ChineseWriterWindow : Window {

        private TextBox _pinyinInput;
        private FrameworkElement _cursorPanel;
        private bool _draggingSelection = false;
        private Tuple<WordPanel,WordPanel> _selection = null;
        private WordPanel[] _wordPanels = new WordPanel[] {};

        private Key[] TEXT_EDIT_KEYS = new Key[] { Key.Back, Key.Delete, Key.Left, Key.Right, Key.Home, Key.End };
        private Key[] DECIMAL_KEYS = new Key[] { Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9 };

        public void InitClojureLoadPath( ) {
            var loadPath = Environment.GetEnvironmentVariable( "CLOJURE_LOAD_PATH" ) ?? "";
            loadPath = AppendToPath( loadPath, "c:/github/ChineseWriter/Clojure" );
            loadPath = loadPath.Replace( '\\', '/' );
            Environment.SetEnvironmentVariable( "CLOJURE_LOAD_PATH", loadPath );
        }

        private string AppendToPath( string path, string newDir ) {
            return path + ( Directory.Exists( newDir ) ? ";" + newDir : "" );
        }

        public ChineseWriterWindow( ) {
            InitializeComponent( );
            InitClojureLoadPath( );
            try {

                _pinyinInput = new TextBox { Style = GuiUtils.PinyinStyle, MinWidth = 20 };
                _pinyinInput.KeyUp += new KeyEventHandler( PinyinInput_KeyUp );
                _pinyinInput.GotFocus += _pinyinInput_GotFocus;

                _cursorPanel = CreateCursorPanel( );

                var ControlKeyPresses = Observable.
                    FromEventPattern<KeyEventArgs>( _pinyinInput, "KeyUp" ).
                    Where( args => args.EventArgs.KeyboardDevice.Modifiers.HasFlag( ModifierKeys.Control ) ).
                    Select( args => args.EventArgs.Key );

                ControlKeyPresses.Where( key => TEXT_EDIT_KEYS.Contains( key ) ).
                    Subscribe( key => TextEdit( key ) );

                ControlKeyPresses.Where( key => DECIMAL_KEYS.Contains( key ) ).
                    Select( key => Array.IndexOf<Key>( DECIMAL_KEYS, key ) ).
                    Subscribe( pinyinIndex => SelectSuggestionIndex( pinyinIndex ) );

                var PinyinChanges = Observable.
                    FromEventPattern<TextChangedEventArgs>( _pinyinInput, "TextChanged" ).
                    ObserveOnDispatcher( ).
                    Select( args => ( (TextBox)args.Sender ).Text );
                // Update UI based on writing state changes
                GuiUtils.CheckBoxChangeObservable( ShowEnglish ).
                    CombineLatest( PinyinChanges, ( english, input ) => Tuple.Create( english, input ) ).
                    ObserveOnDispatcher( ).
                    Subscribe( tuple => UpdateSuggestions( WordDatabase.Suggestions( tuple.Item2, tuple.Item1 ) ) );
                WritingState.WordsChanges.
                    ObserveOnDispatcher( ).Subscribe( words => PopulateCharGrid( words, WritingState.CursorPos ) );
                WritingState.WordsChanges.Subscribe( words => WritingState.SaveCurrentText( ) );
                _pinyinInput.Focus( );

            } catch ( Exception ex ) {
                MessageBox.Show( ex.ToString( ), "Error in startup of ChineseWriter" );
            }
        }

        void _pinyinInput_GotFocus( object sender, RoutedEventArgs e ) {
            RemoveTextSelection( );
        }

        void RemoveTextSelection( ) {
            _selection = Tuple.Create<WordPanel, WordPanel>( null, null );
            MarkDraggedSelection( );            
        }

        public void TextEdit( Key key ) {
            switch ( key ) {
                case Key.Left:
                case Key.Right:
                case Key.Home:
                case Key.End:
                    WritingState.Move( key.ToString( ) );
                    break;
                case Key.Back:
                    WritingState.BackSpace( );
                    break;
                case Key.Delete:
                    WritingState.Delete( );
                    break;
            }
        }

        public int CursorPos {
            get { return WritingState.CursorPos; }
        }

        private void Window_Loaded( object sender, RoutedEventArgs e ) {
            ThreadPool.QueueUserWorkItem( state => {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                try {
                    RT.load( "WritingState" );
                    RT.load( "ExportText" );
                    RT.load( "ParseChinese" );
                    RT.var( "ExportText", "set-add-diacritics-func!" ).invoke( StringUtils.AddDiacriticsFunc );
                    Dispatcher.Invoke( new Action( ( ) => WritingState.LoadText( ) ) );
                    WordDatabase.LoadWords( );
                } catch ( Exception ex ) {
                    ReportErrorThreadSafe( ex );
                }
            } );
            ThreadPool.QueueUserWorkItem( state => ReportLoadStateTask( ) );
        }

        private void ReportLoadStateTask( ) {
            var start = DateTime.Now;
            var wordCount = 0;
            var ready = false;
            SetTitleThreadsafe( "Loading Clojure runtime..." );
            try {
                while ( true ) {
                    string status;
                    if ( RT.var( "WordDatabase", "all-words" ).isBound ) {
                        var wordsList = (IList<object>)( (clojure.lang.Atom)RT.var( "WordDatabase", "all-words" ).deref( ) ).deref( );
                        wordCount = wordsList.Count;
                        ready = wordCount > 10000;
                        status = wordCount == 0 ? "Loading word list..." :
                            !ready ? "Short word list loaded (start writing!), loading full dictionary..." :
                            "ChineseWriter. " + RT.var( "WordDatabase", "database-info" ).invoke( ).ToString( ) + " --- ";
                    } else {
                        status = "Loading Clojure runtime...";
                    }
                    var dur = DateTime.Now - start;
                    SetTitleThreadsafe( string.Format( "{0} {1:0} s", status, dur.TotalSeconds ) );
                    if ( ready ) return;
                    Thread.Sleep( 100 );
                }
            } catch ( Exception ex ) {
                ReportErrorThreadSafe( ex );
            }
        }

        private void ReportErrorThreadSafe( Exception ex ) {
            var message = ex.ToString( );
            if ( message.Contains( "Could not locate clojure.core.clj.dll" ) ) {
                this.Dispatcher.Invoke( new Action( ( ) => {
                    MessageBox.Show( "Please ensure that environment variable CLOJURE_LOAD_PATH\n" +
                        "Contains clojure-clr directory and any other library directories needed",
                        "Cannot find Clojure files" );
                } ) );
            } else {
                this.Dispatcher.Invoke( new Action( ( ) => {
                    MessageBox.Show( ex.ToString( ), "Error in ChineseWriter Clojure initialization" );
                } ) );
            }
        }

        private void SetTitleThreadsafe( string title ) {
            this.Dispatcher.Invoke( new Action( ( ) => { this.Title = title; } ) );
        }

        private int CurrentUpdater = 0;
        private int ActiveUpdaters = 0;

        private void UpdateSuggestions( IEnumerable<IDictionary<object, object>> suggestions ) {
            ThreadPool.QueueUserWorkItem(
                state => UpdateSuggestionsBackground( (IEnumerable<IDictionary<object, object>>)state ), suggestions );
        }

        /// <summary>
        /// TODO: Use dotnet Task-framework for this?
        /// </summary>
        /// <param name="suggestions"></param>
        private void UpdateSuggestionsBackground( IEnumerable<IDictionary<object, object>> suggestions ) {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            CurrentUpdater++;
            var id = CurrentUpdater;
            ActiveUpdaters++;
            try {
                while ( ActiveUpdaters > 1 ) {
                    Thread.Sleep( 10 );
                    if ( id != CurrentUpdater ) return; // abort old updaters that have been replaced with newer ones
                }
                var items = new ObservableCollection<SuggestionWord>( ); // The default ItemsCollection of DataGrid does not allow editing
                items.CollectionChanged += items_CollectionChanged;
                this.Dispatcher.Invoke( new Action( ( ) => {
                    ProcessingLabel.Content = "Searching dictionary...";
                    ProcessingLabel.Foreground = new SolidColorBrush( Colors.Red );
                    Suggestions.ItemsSource = items;
                } ), TimeSpan.FromSeconds( 0.5 ), DispatcherPriority.Background );
                var index = 1;
                foreach ( var suggestion in suggestions ) {
                    if ( id != CurrentUpdater ) return; // abort old updaters that have been replaced with newer ones
                    var shortcut = index == 1 ? "Enter" :
                        index <= 10 ? string.Format( "CTRL+{0}", index ) : "<click>";
                    var dataWord = new SuggestionWord( index, suggestion, shortcut );
                    this.Dispatcher.Invoke( new Action( ( ) => items.Add( dataWord ) ), TimeSpan.FromSeconds( 0.5 ), DispatcherPriority.Background );
                    index++;
                }
                this.Dispatcher.Invoke( new Action( ( ) => {
                    ProcessingLabel.Foreground = new SolidColorBrush( Colors.Black );
                    ProcessingLabel.Content = string.Format( "{0} suggestions", Suggestions.Items.Count );
                } ) );
            } finally {
                ActiveUpdaters--;
            }
        }

        void ShowTempMessage( string message ) {
            ProcessingLabel.Foreground = new SolidColorBrush( Colors.DarkGreen );
            ProcessingLabel.Content = message;
            ThreadPool.QueueUserWorkItem( state => {
                try {
                    Thread.Sleep(1000);
                    Dispatcher.Invoke( new Action( ( ) => { 
                        ProcessingLabel.Content = "";
                        ProcessingLabel.Foreground = new SolidColorBrush( Colors.Black );
                    } ));                    
                } catch ( Exception ex ) {
                    ReportErrorThreadSafe( ex );
                }
            } );
        }

        void items_CollectionChanged( object sender, NotifyCollectionChangedEventArgs e ) {
            if ( e.Action == NotifyCollectionChangedAction.Remove ) {
                foreach ( SuggestionWord item in e.OldItems ) {
                    item.Delete();
                }
            }
        }

        private FrameworkElement CreateCursorPanel( ) {
            var cursorBrush = new SolidColorBrush( );
            var panel = GuiUtils.WrapToBorder( new Label {
                Content = _pinyinInput,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = cursorBrush
            } );
            var cursorAnimation = new ColorAnimation( Color.FromRgb( 192, 192, 255 ), Colors.White,
                new Duration( TimeSpan.FromSeconds( 0.5 ) ),
                FillBehavior.HoldEnd ) {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
            cursorBrush.BeginAnimation( SolidColorBrush.ColorProperty, cursorAnimation );
            return panel;
        }

        void PinyinInput_KeyUp( object sender, KeyEventArgs e ) {
            if ( e.Key == Key.Enter ) {
                if ( Suggestions.Items.Count == 0 ) {
                    WritingState.LiteralInput( _pinyinInput.Text );
                    _pinyinInput.Text = "";
                } else {
                    SelectSuggestion( (SuggestionWord)Suggestions.Items[0] );
                }
                e.Handled = true;
            }
        }

        private void SelectSuggestionIndex( int pinyinIndex ) {
            if ( pinyinIndex <= Suggestions.Items.Count ) {
                SelectSuggestion( (SuggestionWord)Suggestions.Items[pinyinIndex - 1] );
            }
        }

        private void SelectSuggestion( SuggestionWord word ) {
            WritingState.InsertWord( word.Hanyu, word.Pinyin );
            ShowEnglish.IsChecked = false;
            _pinyinInput.Text = "";
        }

        private void PopulateCharGrid( IEnumerable<IDictionary<object, object>> words, int cursorPos ) {
            _selection = Tuple.Create<WordPanel, WordPanel>( null, null );
            _wordPanels = words.Select( word => new WordPanel( word ) ).ToArray();
            Characters.Children.Clear( );
            int pos = 0;            
            foreach ( var wordPanel in _wordPanels ) {
                if ( pos == cursorPos ) Characters.Children.Add( _cursorPanel );
                Characters.Children.Add( wordPanel );
                wordPanel.MouseEnter += wordPanel_MouseEnter;
                wordPanel.MouseLeftButtonDown += wordPanel_MouseLeftButtonDown;
                wordPanel.MouseLeftButtonUp += wordPanel_MouseLeftButtonUp;
                wordPanel.MouseRightButtonUp += wordPanel_MouseRightButtonUp;
                pos++;
            }
            if ( pos == cursorPos ) Characters.Children.Add( _cursorPanel );
            _pinyinInput.Focus( );
        }

        void wordPanel_MouseRightButtonUp( object sender, MouseButtonEventArgs e ) {
            
        }

        void wordPanel_MouseLeftButtonDown( object sender, MouseButtonEventArgs e ) {
            var wordPanel = (WordPanel)sender;
            _draggingSelection = true;
            _selection = Tuple.Create( wordPanel, wordPanel );
            MarkDraggedSelection( );
        }

        void wordPanel_MouseEnter( object sender, MouseEventArgs e ) {
            var thisPanel = (WordPanel)sender;
            if (Mouse.LeftButton != MouseButtonState.Pressed) _draggingSelection = false;
            if ( _draggingSelection ) _selection = Tuple.Create( _selection.Item1, thisPanel );
            MarkDraggedSelection( );
        }

        void wordPanel_MouseLeftButtonUp( object sender, MouseButtonEventArgs e ) {
            if ( _draggingSelection ) {
                _draggingSelection = false;
                CopyNow( );
                if ( object.ReferenceEquals( _selection.Item1, _selection.Item2 ) ) {
                    var word = ( (WordPanel)sender ).Word;
                    // Show word breakdown and Move cursor only on clicking on single field, not dragging selection
                    UpdateSuggestions( WordDatabase.BreakDown( word.Hanyu( ), word.Pinyin( ) ) );
                    WritingState.SetCursorPos( WordPanelIndex( (WordPanel)sender ) + 1 );
                }
            }
        }

        void MarkDraggedSelection( ) {
            // First deselect all
            foreach ( WordPanel wordPanel in _wordPanels ) {
                wordPanel.SetSelected(false);
            }
            // Then mark selected
            foreach ( WordPanel wordPanel in SelectedWordPanels ) {
                wordPanel.SetSelected(true );
            }
        }

        int WordPanelIndex( WordPanel panelToFind ) {
            int index = 0;
            foreach ( WordPanel wordPanel in _wordPanels ) {
                if ( object.ReferenceEquals( wordPanel, panelToFind ) ) return index;
                index++;
            }
            throw new ApplicationException( "Panel not found");
        }

        WordPanel[] SelectedWordPanels {
            get {
                if ( _selection.Item1 == null ) {
                    return new WordPanel[] {};
                } else {
                    int startIndex = WordPanelIndex( _selection.Item1 );
                    int endIndex = WordPanelIndex( _selection.Item2 );
                    var first = Math.Min( startIndex, endIndex );
                    var last = Math.Max( startIndex, endIndex );
                    return _wordPanels.Skip( first ).Take( last - first + 1 ).ToArray( );           
                }
            }
        }

        private void CopyClick( object sender, RoutedEventArgs e ) {
            CopyNow( );
        }

        private void CopyNow( ) {
            if ( CopyHtml.IsChecked ?? false ) {
                ClipboardTool.CopyToClipboard(
                    (string)RT.var( "ExportText", "html" ).invoke( CopyEnglish.IsChecked, CopyFullEnglish.IsChecked ),
                    new Uri( "http://www.brotherus.net" ) );
            } else {
                var data = WritingState.HanyiPinyinLines(
                    SelectedWords,
                    CopyPinyin.IsChecked ?? false,
                    CopyEnglish.IsChecked ?? false );
                HandleExceptions( ( ) => Clipboard.SetText( data, TextDataFormat.UnicodeText ) );
            }
            ShowTempMessage( "Copied" );
        }

        private IEnumerable<IDictionary<object, object>> SelectedWords {
            get {
                if ( _selection.Item1 == null ) return null;
                return SelectedWordPanels.Select( panel => panel.Word );
            }
        }

        private void Clear_Text_Click( object sender, RoutedEventArgs e ) {
            WritingState.Clear( );
            _pinyinInput.Text = "";
        }

        private void StayOnTop_Checked( object sender, RoutedEventArgs e ) {
            this.Topmost = StayOnTop.IsChecked ?? false;
        }

        private void HandleExceptions( Action action ) {
            try {
                action.Invoke( );
            } catch ( Exception ex ) {
                MessageBox.Show( this, ex.Message, "Exception in ChineseWriter", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.None );
            }
        }

        private void Window_Activated( object sender, EventArgs e ) {
            this._pinyinInput.Focus( );
        }

        private void PasteChineseClick( object sender, RoutedEventArgs e ) {
            WritingState.LiteralInput(
                Regex.Replace( Clipboard.GetText( ), @"\s", "", RegexOptions.None ) );
        }

        private void Suggestions_MouseUp( object sender, MouseButtonEventArgs e ) {
            Console.WriteLine( sender );
            var source = (DependencyObject)e.OriginalSource;
            var row = (DataGridRow)GuiUtils.FindParent( source, typeof( DataGridRow ) );
            var cell = (DataGridCell)GuiUtils.FindParent( source, typeof( DataGridCell ) );
            if ( row == null || cell == null ) return;
            if ( cell.Column.Header.ToString( ) == "Shortcut" ) {
                SelectSuggestion( (SuggestionWord)row.Item );
                e.Handled = true;
            }
        }

    } // class

} // namespace
