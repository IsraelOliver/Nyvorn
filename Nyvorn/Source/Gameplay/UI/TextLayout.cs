using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Text;

namespace Nyvorn.Source.Gameplay.UI
{
    public static class TextLayout
    {
        public static string WrapText(SpriteFont font, string text, float maxWidth)
        {
            if (font == null || string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return text ?? string.Empty;

            string[] paragraphs = text.Replace("\r", string.Empty).Split('\n');
            StringBuilder wrapped = new StringBuilder(text.Length + 16);

            for (int paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
            {
                if (paragraphIndex > 0)
                    wrapped.Append('\n');

                AppendWrappedParagraph(font, paragraphs[paragraphIndex], maxWidth, wrapped);
            }

            return wrapped.ToString();
        }

        private static void AppendWrappedParagraph(SpriteFont font, string paragraph, float maxWidth, StringBuilder wrapped)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
                return;

            string[] words = paragraph.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            string currentLine = string.Empty;

            foreach (string word in words)
            {
                string candidate = string.IsNullOrEmpty(currentLine)
                    ? word
                    : $"{currentLine} {word}";

                if (font.MeasureString(candidate).X <= maxWidth)
                {
                    currentLine = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(currentLine))
                {
                    wrapped.Append(currentLine);
                    wrapped.Append('\n');
                    currentLine = string.Empty;
                }

                foreach (string fragment in BreakLongWord(font, word, maxWidth))
                {
                    if (font.MeasureString(fragment).X <= maxWidth)
                    {
                        currentLine = fragment;
                    }
                    else
                    {
                        wrapped.Append(fragment);
                        wrapped.Append('\n');
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
                wrapped.Append(currentLine);
        }

        private static IEnumerable<string> BreakLongWord(SpriteFont font, string word, float maxWidth)
        {
            if (font.MeasureString(word).X <= maxWidth)
            {
                yield return word;
                yield break;
            }

            int start = 0;
            while (start < word.Length)
            {
                int length = 1;
                while (start + length <= word.Length &&
                       font.MeasureString(word.Substring(start, length)).X <= maxWidth)
                {
                    length++;
                }

                length = length == 1 ? 1 : length - 1;
                yield return word.Substring(start, length);
                start += length;
            }
        }
    }
}
