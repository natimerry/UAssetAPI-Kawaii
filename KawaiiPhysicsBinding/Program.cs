using KawaiiPhysicsBinding;
using UAssetAPI;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage();
        }

        string command = args[0];

        if (!string.Equals(command, "port", StringComparison.OrdinalIgnoreCase))
        {
            return Usage();
        }

        return Port(args);
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  KawaiiPhysicsBinding port [usmap_path] <uasset_path> [--force-rebuild] [--patch-default-hidden-mats [bitmap_csv]]");
        Console.Error.WriteLine("  KawaiiPhysicsBinding port <uasset_path> [--force-rebuild] [--patch-default-hidden-mats [bitmap_csv]]");
        Console.Error.WriteLine("    --patch-default-hidden-mats reads LODHiddenMaterials carrier data.");
        Console.Error.WriteLine("    bitmap_csv or --default-hidden-material-bitmaps overrides carrier data.");
        return 2;
    }

    private static int Port(string[] args)
    {
        string? usmapPath = null;
        string? uassetPath = null;

        var options = new KawaiiPhysicsPortOptions();

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                switch (arg)
                {
                    case "--force-rebuild":
                    case "--force-rebuild-chain0":
                        options.ForceRebuildChain0 = true;
                        break;

                    case "--no-kawaii-physics":
                        options.PatchKawaiiPhysics = false;
                        break;

                    case "--patch-default-hidden-mats":
                        options.PatchDefaultHiddenMaterials = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            string bitmapArg = args[++i];
                            if (!string.IsNullOrWhiteSpace(bitmapArg))
                            {
                                options.DefaultHiddenMaterialBitmaps = KawaiiPhysicsBridge.ParseDefaultHiddenMaterialBitmaps(bitmapArg);
                            }
                        }
                        break;

                    case "--default-hidden-material-bitmaps":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine($"[KawaiiPhysicsBinding] Missing value for {arg}");
                            return 2;
                        }

                        options.PatchDefaultHiddenMaterials = true;
                        options.DefaultHiddenMaterialBitmaps = KawaiiPhysicsBridge.ParseDefaultHiddenMaterialBitmaps(args[++i]);
                        break;

                    default:
                        Console.Error.WriteLine($"[KawaiiPhysicsBinding] Unknown option: {arg}");
                        return 2;
                }

                continue;
            }

            if (usmapPath == null)
            {
                usmapPath = arg;
            }
            else if (uassetPath == null)
            {
                uassetPath = arg;
            }
            else
            {
                Console.Error.WriteLine($"[KawaiiPhysicsBinding] Unexpected positional arg: {arg}");
                return 2;
            }
        }

        if (uassetPath == null)
        {
            uassetPath = usmapPath;
            usmapPath = null;
        }

        if (LooksLikeUsmapPath(uassetPath) && IsUassetPath(usmapPath))
        {
            (usmapPath, uassetPath) = (uassetPath, usmapPath);
        }

        if (string.IsNullOrWhiteSpace(uassetPath))
        {
            Console.Error.WriteLine("[KawaiiPhysicsBinding] UAsset path was null or empty");
            return 2;
        }

        if (!File.Exists(uassetPath))
        {
            Console.Error.WriteLine($"[KawaiiPhysicsBinding] UAsset not found: {uassetPath}");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(usmapPath) && !File.Exists(usmapPath))
        {
            Console.Error.WriteLine($"[KawaiiPhysicsBinding] USMAP not found, continuing without mappings: {usmapPath}");
            usmapPath = null;
        }

        try
        {
            var result = KawaiiPhysicsBridge.PortAsset(usmapPath, uassetPath, options, Console.Error);

            Console.Error.WriteLine(
                $"[KawaiiPhysicsBinding] visited={result.VisitedAnimNodes} ported={result.PortedAnimNodes} skipped_existing={result.SkippedExistingChains}"
            );

            Console.Error.WriteLine($"Visited AnimNodes: {result.VisitedAnimNodes}");
            Console.Error.WriteLine($"Ported AnimNodes: {result.PortedAnimNodes}");
            Console.Error.WriteLine($"Skipped Existing Chains: {result.SkippedExistingChains}");
            Console.Error.WriteLine($"Patched DefaultHiddenMaterials LODs: {result.PatchedDefaultHiddenMaterialLods}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[KawaiiPhysicsBinding] failed:");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static bool LooksLikeUsmapPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUassetPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase);
    }
}
