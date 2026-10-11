using System.Text.Json.Serialization;
using Supermonitor.Core.Overlay;

namespace Supermonitor.Core.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(OverlayStyle))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
