using NAudio.Midi;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Audio.DSP;

/// <summary>
/// NAudio.Midi implementation of IMidiCueOutput for WorshipTools Presenter.
/// 
/// CRITICAL DESIGN DECISION: This class ONLY sends Note On messages.
/// It NEVER sends Note Off messages, because WorshipTools Presenter
/// responds to both Note On AND Note Off, causing double-advances.
/// 
/// Connection: Works with virtual MIDI ports (loopMIDI on Windows,
/// IAC Driver on Mac) or network MIDI (rtpMIDI).
/// </summary>
public class MidiCueOutput : IMidiCueOutput
{
    private MidiOut? _midiOut;
    private int _deviceIndex = -1;
    private string _deviceName = "";

    public bool IsConnected => _midiOut != null;
    public string DeviceName => _deviceName;

    /// <summary>
    /// Connect to a MIDI output device by name.
    /// </summary>
    public bool Connect(string deviceName)
    {
        try
        {
            Disconnect();

            // Find device by name
            int deviceCount = MidiOut.NumberOfDevices;
            for (int i = 0; i < deviceCount; i++)
            {
                var info = MidiOut.DeviceInfo(i);
                if (info.ProductName.Equals(deviceName, StringComparison.OrdinalIgnoreCase) ||
                    info.ProductName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    _midiOut = new MidiOut(i);
                    _deviceIndex = i;
                    _deviceName = info.ProductName;
                    return true;
                }
            }

            // Try exact match on index if name is numeric
            if (int.TryParse(deviceName, out int idx) && idx >= 0 && idx < deviceCount)
            {
                _midiOut = new MidiOut(idx);
                _deviceIndex = idx;
                _deviceName = MidiOut.DeviceInfo(idx).ProductName;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MidiCueOutput] Connect failed: {ex.Message}");
            _midiOut = null;
            return false;
        }
    }

    /// <summary>
    /// Disconnect from the MIDI output device.
    /// </summary>
    public void Disconnect()
    {
        if (_midiOut != null)
        {
            try
            {
                _midiOut.Dispose();
            }
            catch { }
            _midiOut = null;
            _deviceIndex = -1;
            _deviceName = "";
        }
    }

    /// <summary>
    /// Send a MIDI Note On message.
    /// 
    /// IMPORTANT: This method ONLY sends Note On (status 0x90).
    /// It does NOT send a corresponding Note Off.
    /// Velocity MUST be > 0 (velocity 0 is interpreted as Note Off by MIDI spec).
    /// </summary>
    public void SendNoteOn(int channel, int note, int velocity)
    {
        if (_midiOut == null) return;

        // Ensure velocity is never 0 (0 = Note Off in MIDI spec)
        velocity = Math.Clamp(velocity, 1, 127);
        channel = Math.Clamp(channel, 1, 16);
        note = Math.Clamp(note, 0, 127);

        try
        {
            // NAudio MidiMessage: channel is 1-based
            // Status byte for Note On: 0x90 + (channel - 1)
            var noteOnEvent = new NoteOnEvent(0, channel, note, velocity, 0);
            _midiOut.Send(noteOnEvent.GetAsShortMessage());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MidiCueOutput] SendNoteOn failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Get list of available MIDI output devices.
    /// </summary>
    public List<string> GetAvailableDevices()
    {
        var devices = new List<string>();
        try
        {
            int count = MidiOut.NumberOfDevices;
            for (int i = 0; i < count; i++)
            {
                var info = MidiOut.DeviceInfo(i);
                devices.Add(info.ProductName);
            }
        }
        catch { }
        return devices;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
