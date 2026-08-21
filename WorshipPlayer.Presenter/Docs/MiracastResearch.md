# Wireless Display Research: Miracast, NDI, and Alternatives

## Overview

This document evaluates wireless display options for the OWC Presenter module,
focusing on reliability, latency, and ease of integration for church production environments.

---

## Miracast

### What It Is

Miracast is a Wi-Fi Direct-based screen mirroring standard built into Windows 10/11.
It allows peer-to-peer display mirroring without a network router.

### Limitations

- **Hardware dependency**: Requires Wi-Fi Direct support in both the sending PC and the receiving display. Many projectors and TVs do not support Miracast natively.
- **No programmatic API for arbitrary windows**: Windows only exposes Miracast as a full-screen or display mirror. There is no public API to cast a specific window content (like our OutputWindow) without mirroring the entire display.
- **Latency**: Typical Miracast latency is 100-300ms, which is acceptable for static lyrics but noticeable during transitions or video backgrounds.
- **Reliability**: Miracast connections are frequently dropped, especially in environments with heavy Wi-Fi congestion (common in churches with many connected devices).
- **Resolution limitations**: Often negotiates down to 1080p or lower depending on hardware capabilities.
- **No multi-receiver**: Miracast is point-to-point. Cannot send to multiple displays simultaneously.

### Verdict

Miracast is suitable for **informal use only** (small groups, portable setups). It is NOT recommended for production worship services due to reliability and latency concerns.

---

## NDI (Network Device Interface) - RECOMMENDED

### What It Is

NDI is a royalty-free protocol developed by Vizrt (formerly NewTek) for high-quality, low-latency video transport over standard Ethernet/Wi-Fi networks.

### Advantages

- **Low latency**: Typically 1-3 frames (33-100ms at 30fps), significantly better than Miracast.
- **Multi-receiver**: Multiple NDI receivers can pick up the same source simultaneously.
- **Professional AV integration**: Works directly with OBS Studio, vMix, Wirecast, ProPresenter, hardware NDI decoders, and professional AV switchers (Blackmagic ATEM, Ross, etc.).
- **No special hardware**: Works over any gigabit Ethernet or decent Wi-Fi network.
- **Configurable quality**: Supports full bandwidth (lossless-quality) and HX modes (lower bandwidth for Wi-Fi).
- **Discovery**: NDI sources are automatically discovered on the network via mDNS.
- **Free tools available**: NDI Monitor, NDI Studio Monitor for preview/output.

### Integration in OWC Presenter

The OWC Presenter captures OutputWindow frames via RenderTargetBitmap and sends them through the NDI SDK. This provides a clean, alpha-free output that receivers can composite or display directly.

### Requirements

- NDI SDK (free download from https://ndi.video/for-developers/ndi-sdk/)
- Native DLL: `Processing.NDI.Lib.x64.dll` (Windows)
- Gigabit Ethernet or 5GHz Wi-Fi for reliable operation

### Recommended Hardware Receivers

- **NDI HX Decoder** (e.g., BirdDog Mini): Converts NDI to HDMI for any display
- **NDI-capable displays**: Some commercial displays have built-in NDI
- **OBS/vMix on a second PC**: Software-based NDI receiving for recording/streaming

---

## Alternative Approaches

### USB-C / Wireless HDMI Adapters

- **Examples**: AIMIBO, TIMBOOTECH, or similar wireless HDMI dongles
- **Pros**: Zero-configuration, plug-and-play, very low latency (5-20ms)
- **Cons**: Limited range (typically 30-50 feet), point-to-point only, hardware cost per display
- **Verdict**: Good for portable setups where running HDMI cable is impractical

### Chromecast (Screen Mirror)

- **Pros**: Inexpensive, widely available
- **Cons**: High latency (200-500ms), requires Chrome browser or Windows cast, no API for window-specific casting, quality can be inconsistent
- **Verdict**: Unsuitable for production use. Acceptable for lobby displays showing announcements.

### Apple AirPlay via Third-Party (Reflector, AirServer)

- **Pros**: Good quality, moderate latency
- **Cons**: Requires third-party software (Reflector/AirServer) on receiving end, not native to Windows sending, additional cost
- **Verdict**: Only relevant if macOS or iOS devices are involved in the workflow.

### SMPTE ST 2110 / SDI

- **Pros**: Broadcast-grade, zero-compromise quality
- **Cons**: Extremely expensive hardware, overkill for worship environments
- **Verdict**: Only for mega-churches with broadcast-grade infrastructure.

---

## Recommendation Summary

| Use Case | Recommended Solution |
|----------|---------------------|
| **Production worship (wired)** | Physical HDMI from GPU to projector/display |
| **Production worship (wireless)** | NDI over gigabit Ethernet + NDI decoder to HDMI |
| **Recording/streaming integration** | NDI (picked up directly by OBS/vMix) |
| **Portable/informal** | Wireless HDMI adapter or Miracast |
| **Lobby/overflow displays** | NDI (multiple receivers) or Chromecast for static content |
| **Multi-campus** | NDI HX over WAN or dedicated streaming |

## Conclusion

**NDI is the primary wireless output protocol** for the OWC Presenter. It provides professional-grade quality with low latency, works with the tools churches already use (OBS, vMix), and scales to multiple receivers. Physical HDMI remains the gold standard for primary displays, with NDI handling recording, streaming, and additional wireless outputs.

Miracast and AirPlay should be considered informal-use-only options and are not supported as primary output methods.
