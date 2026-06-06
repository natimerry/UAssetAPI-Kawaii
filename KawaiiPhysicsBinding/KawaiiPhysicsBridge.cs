using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace KawaiiPhysicsBinding;

internal static class KawaiiPhysicsBridge
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, Lazy<Usmap>> UsmapCache = new(PathComparer);

    public static KawaiiPhysicsLegacyPortResult PortAsset(
        string? usmapPath,
        string uassetPath,
        KawaiiPhysicsPortOptions options,
        TextWriter? log = null)
    {
        if (string.IsNullOrWhiteSpace(uassetPath))
        {
            throw new ArgumentException("UAsset path was null or empty", nameof(uassetPath));
        }

        if (!File.Exists(uassetPath))
        {
            throw new FileNotFoundException("UAsset not found", uassetPath);
        }

        if (!string.IsNullOrWhiteSpace(usmapPath) && !File.Exists(usmapPath))
        {
            log?.WriteLine($"[KawaiiPhysicsBinding] USMAP not found, continuing without mappings: {usmapPath}");
            usmapPath = null;
        }

        log?.WriteLine($"[KawaiiPhysicsBinding] Loading asset: {uassetPath}");
        log?.WriteLine($"[KawaiiPhysicsBinding] USMAP path: {(string.IsNullOrWhiteSpace(usmapPath) ? "null" : usmapPath)}");
        if (!options.PatchKawaiiPhysics)
        {
            log?.WriteLine("[KawaiiPhysicsBinding] KawaiiPhysics port disabled");
        }
        if (options.DefaultHiddenMaterialBitmaps != null)
        {
            log?.WriteLine($"[KawaiiPhysicsBinding] DefaultHiddenMaterials bitmap override rows: {options.DefaultHiddenMaterialBitmaps.Count}");
        }
        else if (options.PatchDefaultHiddenMaterials)
        {
            log?.WriteLine("[KawaiiPhysicsBinding] DefaultHiddenMaterials carrier autodetect enabled");
        }

        var asset = LoadAssetLikeCli(uassetPath, usmapPath, log);
        var result = KawaiiPhysicsLegacyPorter.PortLegacyAnimNodes(asset, options);

        if (result.PortedAnimNodes > 0 || result.PatchedDefaultHiddenMaterialLods > 0)
        {
            asset.Write(uassetPath);
            log?.WriteLine($"[KawaiiPhysicsBinding] Patched: {uassetPath}");
        }

        log?.WriteLine(
            $"[KawaiiPhysicsBinding] visited={result.VisitedAnimNodes} ported={result.PortedAnimNodes} skipped_existing={result.SkippedExistingChains} default_hidden_material_lods={result.PatchedDefaultHiddenMaterialLods}"
        );

        return result;
    }

    public static IReadOnlyList<ulong> ParseDefaultHiddenMaterialBitmaps(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("DefaultHiddenMaterials bitmap list was empty");
        }

        var bitmaps = new List<ulong>();
        foreach (string rawPart in value.Split(',', ';', '|'))
        {
            string part = rawPart.Trim();
            if (part.Length == 0) continue;

            bitmaps.Add(part.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.Parse(part[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : ulong.Parse(part, CultureInfo.InvariantCulture));
        }

        if (bitmaps.Count == 0)
        {
            throw new FormatException("DefaultHiddenMaterials bitmap list was empty");
        }

        return bitmaps;
    }

    public static void ApplyOptionsJson(KawaiiPhysicsPortOptions options, string? json)
    {
        if (options == null || string.IsNullOrWhiteSpace(json)) return;

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("KawaiiPhysics options JSON must be an object");
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            string key = NormalizeOptionKey(property.Name);
            var value = property.Value;

            switch (key)
            {
                case "usecurves":
                    options.UseCurves = ReadBool(value, property.Name);
                    break;
                case "worlddampinglocation":
                    options.WorldDampingLocation = ReadFloat(value, property.Name);
                    break;
                case "worlddampingrotation":
                    options.WorldDampingRotation = ReadFloat(value, property.Name);
                    break;
                case "stiffness":
                    options.Stiffness = ReadFloat(value, property.Name);
                    break;
                case "damping":
                    options.Damping = ReadFloat(value, property.Name);
                    break;
                case "gravityscale":
                    options.GravityScale = ReadFloat(value, property.Name);
                    break;
                case "simulationspace":
                    options.SimulationSpace = ReadString(value, property.Name);
                    break;
                case "teleportdistancethreshold":
                    options.TeleportDistanceThreshold = ReadFloat(value, property.Name);
                    break;
                case "teleportrotationthreshold":
                    options.TeleportRotationThreshold = ReadFloat(value, property.Name);
                    break;
                case "enablewarmup":
                    options.EnableWarmUp = ReadBool(value, property.Name);
                    break;
                case "warmupframes":
                    options.WarmUpFrames = ReadInt(value, property.Name);
                    break;
                case "clearcurvedata":
                    options.ClearCurveData = ReadBool(value, property.Name);
                    break;
                case "clearexternalforces":
                    options.ClearExternalForces = ReadBool(value, property.Name);
                    break;
                case "disablewind":
                    options.DisableWind = ReadBool(value, property.Name);
                    break;
                case "useworldspacegravity":
                    options.UseWorldSpaceGravity = ReadBool(value, property.Name);
                    break;
                case "useprojectgravity":
                    options.UseProjectGravity = ReadBool(value, property.Name);
                    break;
                case "gravityvector":
                    options.GravityVector = ReadVector(value, property.Name);
                    break;
                case "curves":
                    options.CurveOverrides = ReadCurves(value, property.Name);
                    break;
            }
        }
    }

    private static string NormalizeOptionKey(string value)
    {
        return value.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool ReadBool(JsonElement value, string name)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"{name} must be a boolean")
        };
    }

    private static float ReadFloat(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out float result))
        {
            throw new FormatException($"{name} must be a number");
        }
        return result;
    }

    private static int ReadInt(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new FormatException($"{name} must be an integer");
        }
        return result;
    }

    private static string ReadString(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"{name} must be a string");
        }
        return value.GetString() ?? string.Empty;
    }

    private static FVector ReadVector(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{name} must be an object with x, y, and z fields");
        }

        double? x = null;
        double? y = null;
        double? z = null;
        foreach (var property in value.EnumerateObject())
        {
            switch (NormalizeOptionKey(property.Name))
            {
                case "x":
                    x = ReadFloat(property.Value, $"{name}.x");
                    break;
                case "y":
                    y = ReadFloat(property.Value, $"{name}.y");
                    break;
                case "z":
                    z = ReadFloat(property.Value, $"{name}.z");
                    break;
            }
        }

        if (!x.HasValue || !y.HasValue || !z.HasValue)
        {
            throw new FormatException($"{name} must include x, y, and z fields");
        }

        return new FVector(x.Value, y.Value, z.Value);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<KawaiiPhysicsCurvePoint>> ReadCurves(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{name} must be an object of curve-name arrays");
        }

        var output = new Dictionary<string, IReadOnlyList<KawaiiPhysicsCurvePoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var curve in value.EnumerateObject())
        {
            if (curve.Value.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException($"{name}.{curve.Name} must be an array");
            }

            var points = new List<KawaiiPhysicsCurvePoint>();
            foreach (var point in curve.Value.EnumerateArray())
            {
                if (point.ValueKind == JsonValueKind.Array)
                {
                    if (point.GetArrayLength() < 2)
                    {
                        throw new FormatException($"{name}.{curve.Name} array points must contain time and value");
                    }

                    points.Add(new KawaiiPhysicsCurvePoint
                    {
                        Time = ReadFloat(point[0], $"{name}.{curve.Name}.time"),
                        Value = ReadFloat(point[1], $"{name}.{curve.Name}.value")
                    });
                    continue;
                }

                if (point.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException($"{name}.{curve.Name} points must be objects or [time,value] arrays");
                }

                float? time = null;
                float? pointValue = null;
                foreach (var field in point.EnumerateObject())
                {
                    switch (NormalizeOptionKey(field.Name))
                    {
                        case "time":
                        case "x":
                            time = ReadFloat(field.Value, $"{name}.{curve.Name}.time");
                            break;
                        case "value":
                        case "y":
                            pointValue = ReadFloat(field.Value, $"{name}.{curve.Name}.value");
                            break;
                    }
                }

                if (!time.HasValue || !pointValue.HasValue)
                {
                    throw new FormatException($"{name}.{curve.Name} object points must include time/value");
                }

                points.Add(new KawaiiPhysicsCurvePoint
                {
                    Time = time.Value,
                    Value = pointValue.Value
                });
            }

            output[curve.Name] = points;
        }

        return output;
    }

    private static UAsset LoadAssetLikeCli(string uassetPath, string? usmapPath, TextWriter? log)
    {
        try
        {
            var asset = new UAsset(uassetPath, EngineVersion.VER_UE5_3)
            {
                UseSeparateBulkDataFiles = true
            };

            log?.WriteLine(
                $"[KawaiiPhysicsBinding] Loaded without mappings: HasUnversionedProperties={asset.HasUnversionedProperties}, Exports={asset.Exports.Count}"
            );

            return asset;
        }
        catch (Exception noMappingsEx)
        {
            log?.WriteLine("[KawaiiPhysicsBinding] No-mapping load failed:");
            log?.WriteLine(noMappingsEx.ToString());

            if (string.IsNullOrWhiteSpace(usmapPath))
            {
                throw;
            }
        }

        try
        {
            log?.WriteLine($"[KawaiiPhysicsBinding] Retrying with USMAP: {usmapPath}");

            var fullUsmapPath = Path.GetFullPath(usmapPath!);
            var mappings = UsmapCache.GetOrAdd(
                fullUsmapPath,
                path => new Lazy<Usmap>(() => new Usmap(path), LazyThreadSafetyMode.ExecutionAndPublication)
            ).Value;

            var asset = new UAsset(uassetPath, EngineVersion.VER_UE5_3, mappings)
            {
                UseSeparateBulkDataFiles = true
            };

            log?.WriteLine(
                $"[KawaiiPhysicsBinding] Loaded with mappings: HasUnversionedProperties={asset.HasUnversionedProperties}, Exports={asset.Exports.Count}"
            );

            return asset;
        }
        catch (Exception mappedEx)
        {
            throw new InvalidOperationException(
                $"Failed to load asset with or without mappings: {uassetPath}",
                mappedEx
            );
        }
    }
}
