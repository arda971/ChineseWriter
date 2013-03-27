﻿using System;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Reactive.Subjects;
using RT = clojure.lang.RT;
using Keyword = clojure.lang.Keyword;

namespace ChineseWriter {

    public static class WordDatabase {

        private static string FilePath(string fileName) {
            return SearchUpwardFile( ExeDir, fileName );
        }

        public static void LoadWords( ) {
            RT.var( "WordDatabase", "load-database" )
                .invoke( FilePath( "cedict_ts.clj" ), FilePath( "words.clj" ) );
        }

        private static DirectoryInfo ExeDir {
            get {
                var exePath = new Uri( Assembly.GetExecutingAssembly( ).CodeBase ).LocalPath;
                var exeDir = new FileInfo( exePath ).Directory;
                return exeDir;
            }
        }

        private static string SearchUpwardFile( DirectoryInfo startDir, string fileName ) {
            var theFile = startDir.GetFiles( ).FirstOrDefault( file => file.Name == fileName );
            if (theFile != null) return theFile.FullName;
            return SearchUpwardFile( startDir.Parent, fileName );            
        }

        /// <summary>
        /// Recursively convert nested clojure hashes / dictionaries to
        /// more C# friendlt form with Keywords replaced by corresponding string
        /// </summary>
        /// <param name="clojureData">raw clojure data</param>
        /// <returns></returns>
        public static object ConvertDictionaries( object clojureData ) {
            if (clojureData is IDictionary<object, object>) {
                return ( (IDictionary<object, object>)clojureData ).
                    ToDictionary( 
                        pair => ( (Keyword)pair.Key ).getName( ), 
                        pair => ConvertDictionaries(pair.Value) );
            } else if (clojureData is IList<object>) {
                return ( (IList<object>)clojureData ).
                    Select( obj => ConvertDictionaries( obj ) ).ToArray( );
            } else {
                return clojureData;
            }
        }

        internal static void SetShortEnglish( IDictionary<string,object> word, string shortEnglish ) {
            throw new NotImplementedException( );
        }

        internal static void SetWordKnown( IDictionary<string, object> word ) {
            Debug.Print( "SetWordKnown not implemented" );
        }

        internal static void IncreaseUsageCount( IDictionary<string, object> word ) {
            Debug.Print( "IncreaseUsageCount not implemented" );
        }
    } // class

} // namespace
