using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;

namespace ProspectRenamer.App.Dialogs;

public partial class CompletionDialog : Window
{
    private readonly MemoryStream _soundStream;
    private readonly SoundPlayer _soundPlayer;

    public CompletionDialog(string oldName, string newName, bool isRename)
    {
        OperationSummary = isRename
            ? "The prospect was renamed and the old live save was removed safely."
            : "The prospect was duplicated and the original remains untouched.";
        NameTransition = isRename
            ? $"{oldName}  >  {newName}"
            : $"SOURCE: {oldName}  |  COPY: {newName}";

        _soundStream = CreateOrbitalChime();
        _soundPlayer = new SoundPlayer(_soundStream);

        InitializeComponent();
        DataContext = this;
    }

    public string OperationSummary { get; }
    public string NameTransition { get; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _soundPlayer.Load();
            _soundPlayer.Play();
        }
        catch
        {
            // Completion remains usable if Windows has no available audio device.
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _soundPlayer.Dispose();
        _soundStream.Dispose();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static MemoryStream CreateOrbitalChime()
    {
        const int sampleRate = 44_100;
        const double durationSeconds = 0.78;
        var sampleCount = (int)(sampleRate * durationSeconds);
        var stream = new MemoryStream(44 + sampleCount * sizeof(short));
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + sampleCount * sizeof(short));
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(sampleCount * sizeof(short));

            var tones = new[]
            {
                (Start: 0.00, Frequency: 523.25, Length: 0.34),
                (Start: 0.15, Frequency: 659.25, Length: 0.36),
                (Start: 0.31, Frequency: 987.77, Length: 0.43)
            };

            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var time = (double)sampleIndex / sampleRate;
                var mixed = 0.0;
                foreach (var tone in tones)
                {
                    var localTime = time - tone.Start;
                    if (localTime < 0 || localTime > tone.Length)
                    {
                        continue;
                    }

                    var attack = Math.Min(1.0, localTime / 0.018);
                    var release = Math.Pow(Math.Max(0.0, 1.0 - localTime / tone.Length), 2.2);
                    var shimmer = Math.Sin(2 * Math.PI * tone.Frequency * localTime) +
                                  0.22 * Math.Sin(2 * Math.PI * tone.Frequency * 2.01 * localTime);
                    mixed += 0.23 * attack * release * shimmer;
                }

                mixed = Math.Clamp(mixed, -0.92, 0.92);
                writer.Write((short)(mixed * short.MaxValue));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
