﻿using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Meleze.Web.Razor
{
    /// <summary>
    /// MinifyHtmlMinifier performs the HTML minification at compile time.
    /// </summary>
#if DEBUG
    public sealed class MinifyHtmlMinifier
#else
    internal sealed class MinifyHtmlMinifier
#endif
    {
        private static char[] _lineSeparators = new char[] { '\n', '\r' };
        private static char[] _whiteSpaceSeparators = new char[] { ' ', '\t', '\n', '\r' };
        private static string[] _commentsMarkers = new string[] { "{", "}", "function", "var", "[if", "[endif" };
        private static string[] _blockElementsOpenStarts;
        private static string[] _blockElementsCloseStarts;
        static MinifyHtmlMinifier()
        {
            var blockElements = new string[] {
            "article", "aside", "div", "dt", "caption", "footer", "form", "header", "hgroup", "html", "map", "nav", "section",
            "body", "p", "dl", "multicol", "dd", "blockquote", "figure", "address", "center",
            "title", "meta", "link", "html", "head", "body", "script", "br", "!DOCTYPE",
            "h1","h2","h3","h4","h5","h6", "pre", "ul", "menu", "dir", "ol", "li", "tr", "tbody", "thead", "tfoot", "td", "th" };

            _blockElementsOpenStarts = new string[blockElements.Length];
            _blockElementsCloseStarts = new string[blockElements.Length];
            for (int i = 0; i < blockElements.Length; i++)
            {
                _blockElementsOpenStarts[i] = "<" + blockElements[i];
                _blockElementsCloseStarts[i] = "</" + blockElements[i];
            }
        }

        private static Func<string, string> _minifyJS;
        private static Func<string, string> _minifyCSS;
        private bool _comments = true;
        private bool _aggressive = true;
        private bool _javascript = true;
        private bool _css = true;

        public Func<string, string> MinifyJS { set { _minifyJS = value; } }
        public Func<string, string> MinifyCSS { set { _minifyCSS = value; } }
        public bool Comments { set { _comments = value; } }
        public bool Aggressive { set { _aggressive = value; } }
        public bool Javascript { set { _javascript = value; } }
        public bool CSS { set { _css = value; } }

        public void AnalyseContent(string content, ref bool previousIsWhiteSpace, ref bool previousTokenEndsWithBlockElement)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            previousIsWhiteSpace = char.IsWhiteSpace(content[content.Length - 1]);
            previousTokenEndsWithBlockElement = EndsWithBlockElement(content);
        }

        public string Minify(string content, bool previousIsWhiteSpace, bool previousTokenEndsWithBlockElement)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(content.Length);

            if (_comments)
            {
                content = MinifyComments(content, builder);
            }

            if (_javascript && (_minifyJS != null))
            {
                // JS is minified before the HTML to still have the end of lines
                // when analysing the JS (which is needed to take // comments into account correctly)
                content = MinifyJavascript(content, builder);
            }
            else
            {
                content = MinifyJavascriptComments(content, builder);
            }

            if (_css && (_minifyCSS != null))
            {
                content = MinifyInlineCSS(content, builder);
            }

            if (_aggressive)
            {
                content = MinifyAggressivelyHTML(content, builder, previousTokenEndsWithBlockElement);
            }
            else
            {
                content = MinifySafelyHTML(content, builder, previousIsWhiteSpace);
            }

            return content;
        }

        /// <summary>
        /// Removes all the comments that are not Javascript or IE conditional comments.
        /// </summary>
        /// <param name="content"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static string MinifyComments(string content, StringBuilder builder)
        {
            builder.Clear();
            var icommentstart = content.IndexOf("<!--");
            while (icommentstart >= 0)
            {
                var icommentend = content.IndexOf("-->", icommentstart + 3);
                if (icommentend < 0)
                {
                    break;
                }

                if (_commentsMarkers.Select(m => content.IndexOf(m, icommentstart)).Any(i => i > 0 && i < icommentend))
                {
                    // There is a comment but it contains javascript or IE conditionals
                    // => we keep it
                    break;
                }

                builder.Append(content, 0, icommentstart);
                builder.Append(content, icommentend + 3, content.Length - icommentend - 3);
                content = builder.ToString();
                builder.Clear();

                icommentstart = content.IndexOf("<!--", icommentstart);
            }
            return content;
        }

        /// <summary>
        /// Minify white space while keeping the HTML compatible with the given one.
        /// Blanks between tags on the same line are not minified.
        /// Just the line start/end are trimmed (the indentation).
        /// </summary>
        /// <param name="content"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static string MinifySafelyHTML(string content, StringBuilder builder, bool previousIsWhiteSpace) {
            builder.Clear();
            var lines = content.Split(_lineSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();
                if (trimmedLine.Length == 0)
                {
                    continue;
                }
                if (!previousIsWhiteSpace && char.IsWhiteSpace(line[0]) && (trimmedLine[0] != '<'))
                {
                    builder.Append(' ');
                }
                builder.Append(Regex.Replace(trimmedLine,@"\s+"," ")); // replace multiple spaces with a single space

                var endsWithWhiteSpace = char.IsWhiteSpace(line[line.Length - 1]) && (trimmedLine[trimmedLine.Length - 1] != '>');
                var hasEndOfLine = (i < lines.Length - 1) || (_lineSeparators.Any(s => s == content[content.Length - 1]));
                if (hasEndOfLine)
                {
                    builder.Append(' '); // this is a change to the original meleze.web logic which has '\n'
                }
                else if (endsWithWhiteSpace)
                {
                    builder.Append(' ');
                }
                previousIsWhiteSpace = hasEndOfLine || endsWithWhiteSpace;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Minify all the white space. Only one space is kept between attributes and words.
        /// Whitespace is completly remove arround HTML block elements while only a single
        /// one is kept arround inline elements.
        /// </summary>
        /// <param name="content"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static string MinifyAggressivelyHTML(string content, StringBuilder builder, bool previousTokenEndsWithBlockElement)
        {
            throw new NotSupportedException("This functionality removed. Use non-aggressive.");
            builder.Clear();
            var tokens = content.Split(_whiteSpaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            previousTokenEndsWithBlockElement |= (content.Length > 0) && !char.IsWhiteSpace(content[0]);
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (!previousTokenEndsWithBlockElement && !StartsWithBlockElement(token))
                {
                    // We have to keep a white space between 2 texts or an inline element and a text or between 2 inline elements
                    builder.Append(' ');
                }
                builder.Append(token);
                previousTokenEndsWithBlockElement = EndsWithBlockElement(tokens, i);
            }
            if (!previousTokenEndsWithBlockElement && char.IsWhiteSpace(content[content.Length - 1]))
            {
                builder.Append(' ');
            }
            content = builder.ToString();
            return content;
        }
        private static bool StartsWithBlockElement(string content)
        {
            return content[0] == '<' && (_blockElementsOpenStarts.Any(b => content.StartsWith(b)) || _blockElementsCloseStarts.Any(b => content.StartsWith(b)));
        }
        private static bool EndsWithBlockElement(string content)
        {
            if (content[content.Length - 1] != '>')
            {
                return false;
            }
            var istart = content.LastIndexOf('<');
            if (istart < 0)
            {
                return false;
            }
            return StartsWithBlockElement(content.Substring(istart));
        }
        private static bool EndsWithBlockElement(string[] tokens, int i)
        {
            var content = tokens[i];
            if (content[content.Length - 1] != '>')
            {
                return false;
            }
            int istart;
            for (istart = -1; istart < 0 && i >= 0; i--)
            {
                content = tokens[i];
                istart = content.LastIndexOf('<');
            }
            if (istart < 0)
            {
                return false;
            }
            return StartsWithBlockElement(content.Substring(istart));
        }

        /// <summary>
        /// Uses an external Javascript minifier to minimize inline JS code.
        /// </summary>
        /// <param name="content"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static string MinifyJavascript(string content, StringBuilder builder)
        {
            builder.Clear();
            var iscriptstart = content.IndexOf("<script");
            while (iscriptstart >= 0)
            {
                var iscriptautoend = content.IndexOf("/>", iscriptstart + 7);
                var iscriptend = content.IndexOf("</script>", iscriptstart + 7);
                if ((iscriptend < 0) || ((iscriptautoend > 0) && (iscriptautoend < iscriptend)))
                {
                    break;
                }

                // We have some javascript code inside the tag
                // => we can ask a JS minifier to simplify it
                var istartcode = content.IndexOf('>', iscriptstart) + 1;
                var iendcode = iscriptend;
                var code = content.Substring(istartcode, iendcode - istartcode);
                builder.Append(content, 0, istartcode);

                if (!string.IsNullOrWhiteSpace(code))
                {
                    // We call the Microsoft JS minifier by reflexion to cut the dependency.
                    var minifiedCode = code;
                    try
                    {
                        minifiedCode = _minifyJS(code);
                    }
                    catch
                    {
                    }
                    builder.Append(minifiedCode);
                }

                iscriptstart = builder.Length;

                builder.Append(content, iscriptend, content.Length - iscriptend);
                content = builder.ToString();
                builder.Clear();

                iscriptstart = content.IndexOf("<script", iscriptstart);
            }
            return content;
        }

        /// <summary>
        /// Removes the single line comments from the Javascript code.
        /// (because when line returns are minimized, these comments commant the whole script).
        /// </summary>
        /// <param name="content"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static string MinifyJavascriptComments(string content, StringBuilder builder)
        {
            builder.Clear();
            var iscriptstart = content.IndexOf("<script");
            while (iscriptstart >= 0)
            {
                var iscriptautoend = content.IndexOf("/>", iscriptstart + 7);
                var iscriptend = content.IndexOf("</script>", iscriptstart + 7);
                if ((iscriptend < 0) || ((iscriptautoend > 0) && (iscriptautoend < iscriptend)))
                {
                    break;
                }

                // We have some javascript code inside the tag
                // => we can ask a JS minifier to simplify it
                var istartcode = content.IndexOf('>', iscriptstart) + 1;
                var iendcode = iscriptend;
                var code = content.Substring(istartcode, iendcode - istartcode);
                builder.Append(content, 0, istartcode);

                if (!string.IsNullOrWhiteSpace(code))
                {
                    // We remove all // comments that cause problems when minifying the HTML later
                    var lines = code.Split('\n');
                    foreach (var line in lines)
                    {
                        var minifiedLine = line;
                        int icomment = line.IndexOf("//");
                        if (icomment >= 0)
                        {
                            minifiedLine = line.Substring(0, icomment);
                        }
                        builder.AppendLine(minifiedLine);
                    }
                }

                iscriptstart = builder.Length;

                builder.Append(content, iscriptend, content.Length - iscriptend);
                content = builder.ToString();
                builder.Clear();

                iscriptstart = content.IndexOf("<script", iscriptstart);
            }
            return content;
        }

        /// <summary>
        /// Uses an external CSS minifier to minimize inline CSS code.
        /// </summary>
        /// <param name="content"></param>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static string MinifyInlineCSS(string content, StringBuilder builder)
        {
            builder.Clear();
            var iscriptstart = content.IndexOf("<style");
            while (iscriptstart >= 0)
            {
                var iscriptautoend = content.IndexOf("/>", iscriptstart + 6);
                var iscriptend = content.IndexOf("</style>", iscriptstart + 6);
                if ((iscriptend < 0) || ((iscriptautoend > 0) && (iscriptautoend < iscriptend)))
                {
                    break;
                }

                // We have some CSS code inside the tag
                // => we can ask a CSS minifier to simplify it
                var istartcode = content.IndexOf('>', iscriptstart) + 1;
                var iendcode = iscriptend;
                var code = content.Substring(istartcode, iendcode - istartcode);
                builder.Append(content, 0, istartcode);

                if (!string.IsNullOrWhiteSpace(code))
                {
                    // We call the Microsoft JS minifier by reflexion to cut the dependency.
                    var minifiedCode = code;
                    try
                    {
                        minifiedCode = _minifyCSS(code);
                    }
                    catch
                    {
                    }
                    builder.Append(minifiedCode);
                }

                iscriptstart = builder.Length;

                builder.Append(content, iscriptend, content.Length - iscriptend);
                content = builder.ToString();
                builder.Clear();

                iscriptstart = content.IndexOf("<style", iscriptstart);
            }
            return content;
        }
    }
}