using System.Net;
using System.Net.Sockets;
using System.Text;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Controls MaestroDMX (or other OSC-capable lighting) via Open Sound Control protocol.
/// Sends palette changes, intensity, excitement, and snapshot triggers per section.
/// </summary>
public class OscLightingController : IDisposable
{
    private UdpClient? _udpClient;
    private IPEndPoint? _endpoint;
    private bool _connected = false;

    public string Host { get; private set; } = "192.168.1.100";
    public int Port { get; private set; } = 8000;
    public bool IsConnected => _connected;

    /// <summary>
    /// Connect to the MaestroDMX device.
    /// </summary>
    public bool Connect(string host, int port)
    {
        try
        {
            Host = host;
            Port = port;
            _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            _udpClient = new UdpClient();
            _connected = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OSC Connection failed: {ex.Message}");
            _connected = false;
            return false;
        }
    }

    /// <summary>
    /// Disconnect from the device.
    /// </summary>
    public void Disconnect()
    {
        _udpClient?.Close();
        _udpClient = null;
        _connected = false;
    }

    /// <summary>
    /// Apply a lighting preset for a section change.
    /// </summary>
    public void ApplyPreset(LightingPreset preset)
    {
        if (!_connected || _udpClient == null || _endpoint == null) return;

        // Send palette change
        SendOscMessage("/maestro/palette", preset.PaletteIndex);

        // Send intensity (0.0 to 1.0)
        SendOscMessage("/maestro/intensity", preset.Intensity);

        // Send excitement (0.0 to 1.0)
        SendOscMessage("/maestro/excitement", preset.Excitement);

        // Send snapshot if defined
        if (!string.IsNullOrEmpty(preset.SnapshotName))
        {
            SendOscMessage("/maestro/snapshot", preset.SnapshotName);
        }
    }

    /// <summary>
    /// Set master intensity (0.0 to 1.0).
    /// </summary>
    public void SetIntensity(float value)
    {
        SendOscMessage("/maestro/intensity", Math.Clamp(value, 0f, 1f));
    }

    /// <summary>
    /// Set excitement level (0.0 to 1.0).
    /// </summary>
    public void SetExcitement(float value)
    {
        SendOscMessage("/maestro/excitement", Math.Clamp(value, 0f, 1f));
    }

    /// <summary>
    /// Set the color palette by index.
    /// </summary>
    public void SetPalette(int index)
    {
        SendOscMessage("/maestro/palette", index);
    }

    /// <summary>
    /// Trigger a named snapshot.
    /// </summary>
    public void TriggerSnapshot(string name)
    {
        SendOscMessage("/maestro/snapshot", name);
    }

    /// <summary>
    /// Send BPM to MaestroDMX for sync.
    /// </summary>
    public void SetBpm(float bpm)
    {
        SendOscMessage("/maestro/bpm", bpm);
    }

    /// <summary>
    /// Trigger a blackout (all lights off).
    /// </summary>
    public void Blackout()
    {
        SendOscMessage("/maestro/intensity", 0f);
    }

    /// <summary>
    /// Trigger a flash/bump effect.
    /// </summary>
    public void Flash()
    {
        SendOscMessage("/maestro/flash", 1);
    }

    /// <summary>
    /// Send a raw OSC message with a float value.
    /// </summary>
    public void SendOscMessage(string address, float value)
    {
        if (!_connected || _udpClient == null || _endpoint == null) return;

        try
        {
            var packet = BuildOscMessage(address, 'f', BitConverter.GetBytes(SwapEndian(value)));
            _udpClient.Send(packet, packet.Length, _endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OSC Send Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a raw OSC message with an int value.
    /// </summary>
    public void SendOscMessage(string address, int value)
    {
        if (!_connected || _udpClient == null || _endpoint == null) return;

        try
        {
            var packet = BuildOscMessage(address, 'i', BitConverter.GetBytes(SwapEndian(value)));
            _udpClient.Send(packet, packet.Length, _endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OSC Send Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a raw OSC message with a string value.
    /// </summary>
    public void SendOscMessage(string address, string value)
    {
        if (!_connected || _udpClient == null || _endpoint == null) return;

        try
        {
            var valueBytes = PadToFourBytes(Encoding.ASCII.GetBytes(value + '\0'));
            var packet = BuildOscMessage(address, 's', valueBytes);
            _udpClient.Send(packet, packet.Length, _endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OSC Send Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Build an OSC packet from address, type tag, and value bytes.
    /// </summary>
    private byte[] BuildOscMessage(string address, char typeTag, byte[] valueBytes)
    {
        var addressBytes = PadToFourBytes(Encoding.ASCII.GetBytes(address + '\0'));
        var typeTagBytes = PadToFourBytes(Encoding.ASCII.GetBytes("," + typeTag + '\0'));

        var packet = new byte[addressBytes.Length + typeTagBytes.Length + valueBytes.Length];
        Buffer.BlockCopy(addressBytes, 0, packet, 0, addressBytes.Length);
        Buffer.BlockCopy(typeTagBytes, 0, packet, addressBytes.Length, typeTagBytes.Length);
        Buffer.BlockCopy(valueBytes, 0, packet, addressBytes.Length + typeTagBytes.Length, valueBytes.Length);

        return packet;
    }

    /// <summary>
    /// Pad byte array to nearest 4-byte boundary.
    /// </summary>
    private byte[] PadToFourBytes(byte[] data)
    {
        int padding = (4 - (data.Length % 4)) % 4;
        var result = new byte[data.Length + padding];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        return result;
    }

    private static int SwapEndian(int value)
    {
        return IPAddress.HostToNetworkOrder(value);
    }

    private static float SwapEndian(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    public void Dispose()
    {
        Disconnect();
    }
}
