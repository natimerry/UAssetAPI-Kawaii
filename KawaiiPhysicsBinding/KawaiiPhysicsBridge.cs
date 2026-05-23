using System.Collections.Concurrent;
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

        var asset = LoadAssetLikeCli(uassetPath, usmapPath, log);
        var result = KawaiiPhysicsLegacyPorter.PortLegacyAnimNodes(asset, options);

        if (result.PortedAnimNodes > 0)
        {
            asset.Write(uassetPath);
            log?.WriteLine($"[KawaiiPhysicsBinding] Patched: {uassetPath}");
        }

        log?.WriteLine(
            $"[KawaiiPhysicsBinding] visited={result.VisitedAnimNodes} ported={result.PortedAnimNodes} skipped_existing={result.SkippedExistingChains}"
        );

        return result;
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
