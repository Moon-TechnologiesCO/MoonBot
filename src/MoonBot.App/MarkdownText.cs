using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace MoonBot.App;

// Texto seleccionable que entiende lo básico de Markdown que suelen usar los modelos:
// **negrita**, `código`, ```bloques```, # títulos y viñetas "- ".
// No es un parser completo a propósito (KISS): cubre el 90 % de las respuestas.
public class MarkdownText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownText, string?>(nameof(Markdown));

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.Parse("#2A3366"));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    // Usa el mismo estilo del tema que un SelectableTextBlock normal.
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
            Rebuild(Markdown ?? "");
    }

    private void Rebuild(string text)
    {
        var inlines = new InlineCollection();
        var inCodeBlock = false;
        var first = true;

        foreach (var rawLine in text.Replace("\r", "").Split('\n'))
        {
            if (rawLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (!first)
                inlines.Add(new LineBreak());
            first = false;

            if (inCodeBlock)
            {
                inlines.Add(new Run(rawLine) { FontFamily = Mono, Background = CodeBackground });
                continue;
            }

            var line = rawLine.TrimStart();
            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
                hashes++;

            if (hashes is > 0 and <= 6 && line.Length > hashes && line[hashes] == ' ')
                AddFormatted(inlines, line[(hashes + 1)..], bold: true, scale: hashes <= 2 ? 1.15 : 1.05);
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                var indent = new string(' ', (rawLine.Length - line.Length) * 2);
                inlines.Add(new Run(indent + "•  "));
                AddFormatted(inlines, line[2..]);
            }
            else
                AddFormatted(inlines, rawLine);
        }

        Inlines = inlines;
    }

    private void AddFormatted(InlineCollection inlines, string text, bool bold = false, double scale = 1)
    {
        var strong = false;
        var italic = false;
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0) return;
            var run = new Run(buffer.ToString());
            if (bold || strong) run.FontWeight = FontWeight.SemiBold;
            if (italic) run.FontStyle = FontStyle.Italic;
            if (scale != 1) run.FontSize = FontSize * scale;
            inlines.Add(run);
            buffer.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                strong = !strong;
                i++;
                continue;
            }

            // *cursiva*: solo si hay un asterisco de cierre y no es un "2 * 3" suelto.
            if (text[i] == '*' && (italic || (i + 1 < text.Length && text[i + 1] != ' '
                                              && text.IndexOf('*', i + 1) > i + 1)))
            {
                Flush();
                italic = !italic;
                continue;
            }

            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    inlines.Add(new Run(" " + text[(i + 1)..end] + " ") { FontFamily = Mono, Background = CodeBackground });
                    i = end;
                    continue;
                }
            }

            buffer.Append(text[i]);
        }

        Flush();
    }
}
