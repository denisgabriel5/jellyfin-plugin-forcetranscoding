# Force Transcode — Jellyfin Plugin

Force-transcodes specific video codecs to a target codec for configured user and device combinations, bypassing Jellyfin's normal direct-play and remux decisions.

## Why?

Jellyfin relies on the client's declared device profile to decide whether to direct-play, remux, or transcode. Some devices claim support for codecs like HEVC or AV1 in their profile but cannot actually decode those streams, resulting in playback failures or blank screens.

This plugin lets you override that decision on a per-user, per-device basis without touching Jellyfin core or the device profile itself.

## How it works

The plugin registers a global MVC action filter that intercepts every `POST /Items/{id}/PlaybackInfo` request. When the requesting user+device match a configured profile, the plugin patches the device profile in-place before Jellyfin's stream builder evaluates it:

1. **Removes the source codecs** from all video `DirectPlayProfiles` — so the stream builder cannot choose direct-play for those codecs.
2. **Sets the target codec** on all video `TranscodingProfiles` — so the only remaining option is to transcode to that codec.

The patch is applied per-request; no state is written to disk and the client's stored capabilities are not modified.

## Installation

### Via custom repository (recommended)

1. In the Jellyfin dashboard go to **Plugins → Repositories** and add:
   ```
   https://raw.githubusercontent.com/denisgabriel5/jellyfin-plugin-forcetranscoding/master/manifest.json
   ```
2. Go to **Plugins → Catalogue**, find **Force Transcode**, and install.
3. Restart Jellyfin.

### Manual

1. Download `Jellyfin.Plugin.ForceTranscode.dll` from the [Releases](../../releases) page.
2. Create `<jellyfin-data>/plugins/ForceTranscode_0.1.0.0/`.
3. Copy the DLL and `meta.json` into that folder.
4. Restart Jellyfin.

**meta.json**
```json
{
  "id": "3c5f2d1a-8b4e-4f7c-a92d-1e6b0f3d9c2a",
  "name": "Force Transcode",
  "version": "0.1.0.0",
  "targetAbi": "10.12.0.0",
  "overview": "Force-transcode video codecs per user and device",
  "description": "Force-transcodes video codecs per user and device combination.",
  "owner": "",
  "category": "General"
}
```

## Configuration

Open **Dashboard → Plugins → Force Transcode → Settings**.

### Creating a profile

1. Click **New Profile**.
2. Give it a name (e.g. "Living Room TV").
3. Select the **Jellyfin user** the profile applies to.
4. Check the **source codecs** to intercept (e.g. HEVC / H.265).
5. Choose the **target codec** to transcode to (default: H.264).
6. Check the **devices** this profile applies to. Devices that have never connected to Jellyfin won't appear — play any video from the device first, then reload the page.
7. Click **Save**.

### Source codecs

| Option | Codecs intercepted |
|---|---|
| HEVC / H.265 | `hevc`, `h265`, `dvhe`, `dvh1` (incl. Dolby Vision) |
| AV1 | `av1` |
| VP9 | `vp9` |
| MPEG-2 | `mpeg2video` |

### Target codecs

| Option | Notes |
|---|---|
| H.264 | Most compatible — recommended default |
| H.265 / HEVC | Higher quality; device must support HEVC decoding |
| AV1 | Best compression; requires modern hardware encoder |
| VP9 | Open codec; good browser compatibility |

## Requirements

- Jellyfin 10.12 or later
- The user's Jellyfin policy must allow at minimum **"Allow media playback that requires conversion without re-encoding"** (remuxing). For transcoding to work, **"Allow video playback that requires transcoding"** must also be enabled.

## Limitations

- Only intercepts `POST /Items/{id}/PlaybackInfo`. Clients using cached sessions or other playback endpoints may bypass the filter.
- When a client sends no device profile in the request body, the plugin seeds the profile from the device's stored capabilities. If those are absent, a minimal HLS fallback is used which may restrict direct-play for all formats.

## Building from source

```bash
git clone https://github.com/denisgabriel5/jellyfin-plugin-forcetranscoding.git
cd jellyfin-plugin-forcetranscoding
dotnet build Jellyfin.Plugin.ForceTranscode/Jellyfin.Plugin.ForceTranscode.csproj -c Release
```

### Running tests

```bash
dotnet test Jellyfin.Plugin.ForceTranscode.Tests/
```

## License

[GPL-2.0](LICENSE)
