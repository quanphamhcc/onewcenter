namespace WorshipPlayer.Core.Services;

/// <summary>
/// Interface for NDI (Network Device Interface) output.
/// Allows the presenter to send video frames over the network for pickup
/// by OBS, vMix, professional AV switchers, or any NDI-compatible receiver.
/// 
/// This abstraction keeps the Core project platform-independent.
/// The actual NDI implementation requires the NewTek NDI SDK native DLL
/// and should be provided by the Presenter or a dedicated plugin project.
/// 
/// Pattern follows IMidiCueOutput: interface in Core, implementation requires native libs.
/// </summary>
public interface INdiOutput : IDisposable
{
    /// <summary>
    /// Whether the NDI sender is currently active and sending frames.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// The NDI source name visible to receivers on the network.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Initialize the NDI sender with the given source name.
    /// Must be called before Start().
    /// </summary>
    /// <param name="sourceName">The name that will appear on NDI receivers (e.g., "OWC Presenter Output").</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    bool Initialize(string sourceName);

    /// <summary>
    /// Start the NDI sender. After calling this, frames can be sent via SendFrame.
    /// </summary>
    void Start();

    /// <summary>
    /// Stop the NDI sender. No more frames will be transmitted.
    /// </summary>
    void Stop();

    /// <summary>
    /// Send a single video frame to all connected NDI receivers.
    /// </summary>
    /// <param name="bgraPixels">Raw pixel data in BGRA format (Blue, Green, Red, Alpha).</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="stride">Number of bytes per row (typically width * 4 for BGRA).</param>
    void SendFrame(byte[] bgraPixels, int width, int height, int stride);
}
