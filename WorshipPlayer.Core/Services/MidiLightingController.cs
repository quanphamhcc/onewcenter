using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Controls lighting via MIDI output. Sends Note On/Off and CC messages.
/// Fallback option when OSC is not available or for other MIDI-capable controllers.
/// 
/// NOTE: This uses NAudio.Midi which must be referenced from the UI/Audio project.
/// This class defines the interface. The actual NAudio.Midi implementation is in the Audio project.
/// </summary>
public interface IMidiLightingController : IDisposable
{
    bool IsConnected { get; }
    bool Connect(string deviceName, int channel);
    void Disconnect();
    void ApplyPreset(LightingPreset preset);
    void SendNoteOn(int channel, int note, int velocity);
    void SendNoteOff(int channel, int note);
    void SendControlChange(int channel, int controller, int value);
    void AllNotesOff();
    List<string> GetAvailableDevices();
}

/// <summary>
/// Stub/placeholder implementation - actual MIDI output is platform-specific.
/// The real implementation uses NAudio.Midi.MidiOut on Windows.
/// </summary>
public class MidiLightingControllerStub : IMidiLightingController
{
    public bool IsConnected => false;

    public bool Connect(string deviceName, int channel)
    {
        Console.WriteLine($"[MIDI] Connecting to {deviceName} on channel {channel}...");
        return false;
    }

    public void Disconnect() { }

    public void ApplyPreset(LightingPreset preset)
    {
        Console.WriteLine($"[MIDI] Applying preset: Note={preset.MidiNote}, Ch={preset.MidiChannel}, Vel={preset.MidiVelocity}");
    }

    public void SendNoteOn(int channel, int note, int velocity)
    {
        Console.WriteLine($"[MIDI] Note On: Ch={channel}, Note={note}, Vel={velocity}");
    }

    public void SendNoteOff(int channel, int note)
    {
        Console.WriteLine($"[MIDI] Note Off: Ch={channel}, Note={note}");
    }

    public void SendControlChange(int channel, int controller, int value)
    {
        Console.WriteLine($"[MIDI] CC: Ch={channel}, CC={controller}, Val={value}");
    }

    public void AllNotesOff() { }

    public List<string> GetAvailableDevices() => new();

    public void Dispose() { }
}
