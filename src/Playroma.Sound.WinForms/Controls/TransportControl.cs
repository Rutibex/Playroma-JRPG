using Playroma.Sound.NAudio.Devices;
using Playroma.Sound.NAudio.Playback;

namespace Playroma.Sound.WinForms.Controls;

public sealed class DeviceSelectionChangedEventArgs : EventArgs
{
    public DeviceSelectionChangedEventArgs(AudioDeviceSelection selection) => Selection = selection;
    public AudioDeviceSelection Selection { get; }
}

public sealed class GainChangedEventArgs : EventArgs
{
    public GainChangedEventArgs(float gain) => Gain = gain;
    public float Gain { get; }
}

public sealed class TransportControl : UserControl
{
    private readonly Button _playButton;
    private readonly Button _stopButton;
    private readonly Button _recordButton;
    private readonly ComboBox _deviceCombo;
    private readonly TrackBar _gainSlider;
    private readonly Label _gainLabel;
    private readonly ProgressBar _peakMeter;
    private readonly ProgressBar _bufferMeter;
    private readonly Label _stateLabel;
    private bool _updatingDevices;

    public TransportControl()
    {
        Dock = DockStyle.Fill;
        Height = 54;
        MinimumSize = new Size(600, 54);

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(5),
            AutoScroll = true
        };

        _playButton = CreateButton("▶ Play", 74);
        _stopButton = CreateButton("■ Stop", 74);
        _recordButton = CreateButton("● Record", 82);
        _stopButton.Enabled = false;

        _deviceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
            Margin = new Padding(12, 7, 3, 3)
        };

        _gainSlider = new TrackBar
        {
            Minimum = 0,
            Maximum = 200,
            TickFrequency = 25,
            Value = 100,
            Width = 140,
            Height = 35,
            Margin = new Padding(8, 1, 0, 0)
        };
        _gainLabel = new Label { Text = "Gain 1.00", AutoSize = true, Margin = new Padding(0, 11, 8, 0) };

        _peakMeter = new ProgressBar { Minimum = 0, Maximum = 1000, Width = 105, Height = 18, Margin = new Padding(4, 9, 2, 0) };
        _bufferMeter = new ProgressBar { Minimum = 0, Maximum = 1000, Width = 85, Height = 18, Margin = new Padding(4, 9, 2, 0) };
        _stateLabel = new Label { Text = "Stopped", AutoSize = true, Margin = new Padding(8, 11, 0, 0), AutoEllipsis = true };

        layout.Controls.Add(_playButton);
        layout.Controls.Add(_stopButton);
        layout.Controls.Add(_recordButton);
        layout.Controls.Add(new Label { Text = "Output:", AutoSize = true, Margin = new Padding(12, 11, 0, 0) });
        layout.Controls.Add(_deviceCombo);
        layout.Controls.Add(_gainSlider);
        layout.Controls.Add(_gainLabel);
        layout.Controls.Add(new Label { Text = "Peak", AutoSize = true, Margin = new Padding(4, 11, 0, 0) });
        layout.Controls.Add(_peakMeter);
        layout.Controls.Add(new Label { Text = "Buffer", AutoSize = true, Margin = new Padding(4, 11, 0, 0) });
        layout.Controls.Add(_bufferMeter);
        layout.Controls.Add(_stateLabel);
        Controls.Add(layout);

        _playButton.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        _recordButton.Click += (_, _) => RecordRequested?.Invoke(this, EventArgs.Empty);
        _gainSlider.ValueChanged += (_, _) =>
        {
            float gain = _gainSlider.Value / 100f;
            _gainLabel.Text = $"Gain {gain:0.00}";
            GainChanged?.Invoke(this, new GainChangedEventArgs(gain));
        };
        _deviceCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingDevices || _deviceCombo.SelectedItem is not DeviceItem item) return;
            DeviceSelectionChanged?.Invoke(this, new DeviceSelectionChangedEventArgs(item.Selection));
        };
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? RecordRequested;
    public event EventHandler<DeviceSelectionChangedEventArgs>? DeviceSelectionChanged;
    public event EventHandler<GainChangedEventArgs>? GainChanged;

    public void SetGain(float gain)
    {
        gain = float.IsFinite(gain) ? Math.Clamp(gain, 0f, 2f) : 1f;
        _gainSlider.Value = (int)Math.Round(gain * 100);
    }

    public void SetDevices(IReadOnlyList<AudioDeviceInfo> devices, string selectedDeviceId)
    {
        ArgumentNullException.ThrowIfNull(devices);
        _updatingDevices = true;
        try
        {
            _deviceCombo.Items.Clear();
            _deviceCombo.Items.Add(new DeviceItem("Default multimedia device", AudioDeviceSelection.Default()));
            foreach (AudioDeviceInfo device in devices)
            {
                string defaultSuffix = device.IsDefaultMultimedia ? " (default)" : "";
                _deviceCombo.Items.Add(new DeviceItem(device.FriendlyName + defaultSuffix, AudioDeviceSelection.Specific(device.Id)));
            }

            int index = 0;
            if (!string.IsNullOrWhiteSpace(selectedDeviceId))
            {
                for (int i = 1; i < _deviceCombo.Items.Count; i++)
                {
                    if (_deviceCombo.Items[i] is DeviceItem item && StringComparer.Ordinal.Equals(item.Selection.DeviceId, selectedDeviceId))
                    {
                        index = i;
                        break;
                    }
                }
            }
            _deviceCombo.SelectedIndex = index;
        }
        finally
        {
            _updatingDevices = false;
        }
    }

    public void UpdateTelemetry(PlaybackTelemetrySnapshot telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _peakMeter.Value = Math.Clamp((int)Math.Round(telemetry.MasterPeak * 1000), 0, 1000);
        _bufferMeter.Value = Math.Clamp((int)Math.Round(telemetry.RingFillRatio * 1000), 0, 1000);
        _stateLabel.Text = telemetry.State switch
        {
            WasapiPlaybackHostState.Playing => $"Playing — {telemetry.DeviceName}",
            WasapiPlaybackHostState.Recovering => $"Recovering device ({telemetry.CurrentRecoveryAttempt})",
            WasapiPlaybackHostState.Faulted => "Playback faulted",
            WasapiPlaybackHostState.Starting => "Starting…",
            _ => "Stopped"
        };
        bool playing = telemetry.State is WasapiPlaybackHostState.Playing or WasapiPlaybackHostState.Starting or WasapiPlaybackHostState.Recovering;
        _playButton.Enabled = !playing;
        _stopButton.Enabled = playing;
        _recordButton.Enabled = playing;
        _recordButton.Text = telemetry.IsRecording ? "■ Recording" : "● Record";
        _deviceCombo.Enabled = !telemetry.IsRecording;
    }

    private static Button CreateButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 30,
        Margin = new Padding(2)
    };

    private sealed record DeviceItem(string DisplayName, AudioDeviceSelection Selection)
    {
        public override string ToString() => DisplayName;
    }
}
