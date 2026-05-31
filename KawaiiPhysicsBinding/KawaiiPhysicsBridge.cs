using System.Collections.Concurrent;
using System.Globalization;
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
