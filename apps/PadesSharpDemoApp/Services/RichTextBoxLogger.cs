using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace PadesSharpDemoApp.Services;

/// <summary>
/// ILogger implementation ghi màu vào RichTextBox trên UI thread.
/// </summary>
public sealed class RichTextBoxLogger : ILogger
{
    private readonly RichTextBox _console;
    private readonly SynchronizationContext _ctx;

    public RichTextBoxLogger(RichTextBox console)
    {
        _console = console;
        _ctx     = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public void Log<TState>(LogLevel level, EventId eventId,
        TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var msg  = formatter(state, exception);
        var time = DateTime.Now.ToString("HH:mm:ss");
        var (tag, tagColor, msgColor) = level switch
        {
            LogLevel.Error       => ("ERROR", Color.Tomato,     Color.LightCoral),
            LogLevel.Warning     => ("WARN ", Color.Gold,       Color.Khaki),
            LogLevel.Information => ("INFO ", Color.LightGreen, Color.WhiteSmoke),
            _                    => ("DEBUG", Color.Silver,     Color.Silver),
        };

        // Include full exception details (message + stack) so user can copy
        string detail = msg;
        if (exception is not null && level == LogLevel.Error)
            detail = $"{msg}\n{exception}";

        _ctx.Post(_ =>
        {
            if (_console.IsDisposed) return;
            _console.SuspendLayout();

            AppendColored($"[{time}] ", Color.DimGray);
            AppendColored(tag + "  ", tagColor);

            if (level == LogLevel.Error && exception is not null)
            {
                // First line: error text
                AppendColored(msg + "\n", msgColor);
                // Stack trace: dimmed
                AppendColored(exception + "\n", Color.Gray);
            }
            else
            {
                AppendColored(msg + "\n", msgColor);
            }

            _console.SelectionStart  = _console.TextLength;
            _console.ScrollToCaret();
            _console.ResumeLayout();
        }, null);
    }

    private void AppendColored(string text, Color color)
    {
        _console.SelectionStart  = _console.TextLength;
        _console.SelectionLength = 0;
        _console.SelectionColor  = color;
        _console.AppendText(text);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
