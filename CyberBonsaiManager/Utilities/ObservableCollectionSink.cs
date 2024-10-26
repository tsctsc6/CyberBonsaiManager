using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Media;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace CyberBonsaiManager.Utilities;

public class ObservableCollectionSink : ILogEventSink, IDisposable
{
    private readonly ObservableCollection<StringWithColor> _output;
    private readonly Action? _action;
    private readonly StringBuilder _sb = new();
    private readonly TextWriter _textWriter;
    private readonly MessageTemplateTextFormatter _formatter = new(DefaultOutputTemplate);
    private const string DefaultOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    public ObservableCollectionSink(ObservableCollection<StringWithColor> output, Action? action = null)
    {
        _output = output;
        _action = action;
        _textWriter = new StringWriter(_sb);
    }
    
    public void Emit(LogEvent logEvent)
    {
        _formatter.Format(logEvent, _textWriter);
        Brush brush = logEvent.Level switch
        {
            LogEventLevel.Debug => Brushes.Black,
            LogEventLevel.Verbose => Brushes.Black,
            LogEventLevel.Information => Brushes.Black,
            LogEventLevel.Warning => Brushes.Yellow,
            LogEventLevel.Error => Brushes.Red,
            LogEventLevel.Fatal => Brushes.Red,
            _ => throw new ArgumentOutOfRangeException()
        };
        _output.Add(new(){Value = _sb.ToString(), Color = brush});
        _sb.Clear();
        _action?.Invoke();
    }

    public void Dispose()
    {
        _textWriter.Dispose();
        GC.SuppressFinalize(this);
    }
}