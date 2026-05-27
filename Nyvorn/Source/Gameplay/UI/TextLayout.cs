using Microsoft.Xna.Framework.Graphics;
using System;
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

            DebugValidateSpriteFontText(font, text);

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

        public static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    lines++;
            }

            return lines;
        }

        public static float GetWrappedHeight(SpriteFont font, string text)
        {
            if (font == null)
                return 0f;

            return CountLines(text) * font.LineSpacing;
        }

        private static void AppendWrappedParagraph(SpriteFont font, string paragraph, float maxWidth, StringBuilder wrapped)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
                return;

            DebugValidateSpriteFontText(font, paragraph);

            string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string currentLine = string.Empty;

            foreach (string word in words)
            {
                DebugValidateSpriteFontText(font, word);

                string candidate = string.IsNullOrEmpty(currentLine)
                    ? word
                    : $"{currentLine} {word}";

                DebugValidateSpriteFontText(font, candidate);

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

                string[] fragments = BreakLongWord(font, word, maxWidth);
                for (int i = 0; i < fragments.Length; i++)
                {
                    string fragment = fragments[i];
                    DebugValidateSpriteFontText(font, fragment);

                    if (font.MeasureString(fragment).X <= maxWidth)
                    {
                        if (i < fragments.Length - 1)
                        {
                            wrapped.Append(fragment);
                            wrapped.Append('\n');
                        }
                        else
                        {
                            currentLine = fragment;
                        }
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

        private static string[] BreakLongWord(SpriteFont font, string word, float maxWidth)
        {
            DebugValidateSpriteFontText(font, word);

            if (font.MeasureString(word).X <= maxWidth)
            {
                return new[] { word };
            }

            List<string> fragments = new List<string>();
            int start = 0;

            while (start < word.Length)
            {
                int length = 1;

                while (start + length <= word.Length)
                {
                    string fragment = word.Substring(start, length);
                    DebugValidateSpriteFontText(font, fragment);

                    if (font.MeasureString(fragment).X > maxWidth)
                        break;

                    length++;
                }

                length = length == 1 ? 1 : length - 1;

                string result = word.Substring(start, length);
                DebugValidateSpriteFontText(font, result);

                fragments.Add(result);
                start += length;
            }

            return fragments.ToArray();
        }

        private static void DebugValidateSpriteFontText(SpriteFont font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            foreach (char c in text)
            {
                try
                {
                    font.MeasureString(c.ToString());
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("[SpriteFont ERROR] Caractere invalido encontrado.");
                    Console.WriteLine($"Caractere: '{c}'");
                    Console.WriteLine($"Unicode: U+{((int)c):X4}");
                    Console.WriteLine($"Codigo decimal: {(int)c}");
                    Console.WriteLine($"Texto completo: {text}");
                    Console.WriteLine("========================================");

                    throw;
                }
            }
        }
    }
}
