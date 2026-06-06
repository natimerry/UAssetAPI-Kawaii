using System;
using System.Collections.Generic;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI;

public sealed class HiddenMaterialsResult
{
    public List<bool[]> PerLodFlags { get; } = new();
    public bool FoundUserData { get; set; }
    public List<string> Diagnostics { get; } = new();
}

public static class HiddenMaterialsReader
{
    public static HiddenMaterialsResult ReadFromAsset(UAsset asset)
    {
        var result = new HiddenMaterialsResult();
        if (asset == null)
        {
            result.Diagnostics.Add("Asset is null");
            return result;
        }

        for (int i = 0; i < asset.Exports.Count; i++)
        {
            string className = TryGetExportClassName(asset.Exports[i]);
            if (className == null || !LooksLikeCarrierClass(className)) continue;
            if (asset.Exports[i] is not NormalExport carrier || carrier.Data == null) continue;

            ArrayPropertyData lodArrayProp = null;
            foreach (PropertyData prop in carrier.Data)
            {
                if (prop is ArrayPropertyData arrayProperty &&
                    string.Equals(arrayProperty.Name?.Value?.Value, "LODHiddenMaterials", StringComparison.OrdinalIgnoreCase))
                {
                    lodArrayProp = arrayProperty;
                    break;
                }
            }

            if (lodArrayProp?.Value == null || lodArrayProp.Value.Length == 0) continue;

            result.FoundUserData = true;
            result.Diagnostics.Add(
                $"Found LODHiddenMaterials on export[{i}] (class={className}), {lodArrayProp.Value.Length} LOD(s)");

            foreach (PropertyData lodEntry in lodArrayProp.Value)
            {
                if (lodEntry is not StructPropertyData lodStruct)
                {
                    result.PerLodFlags.Add(Array.Empty<bool>());
                    continue;
                }

                bool[] flags = ExtractBoolArray(lodStruct, "HiddenMaterials");
                result.PerLodFlags.Add(flags);
                result.Diagnostics.Add($"  LOD[{result.PerLodFlags.Count - 1}]: {flags.Length} slot flag(s)");
            }

            return result;
        }

        result.Diagnostics.Add("No AssetUserData carrier with LODHiddenMaterials found");
        return result;
    }

    public static int InjectIntoLodInfo(NormalExport meshExport, HiddenMaterialsResult result, UAsset asset)
    {
        if (meshExport?.Data == null || result == null || !result.FoundUserData) return 0;

        ArrayPropertyData lodInfoProp = FindLodInfo(meshExport);
        if (lodInfoProp?.Value == null || lodInfoProp.Value.Length == 0)
        {
            result.Diagnostics.Add("LODInfo property not found on mesh export; nothing injected");
            return 0;
        }

        int modified = 0;
        for (int lodIdx = 0; lodIdx < lodInfoProp.Value.Length; lodIdx++)
        {
            if (lodInfoProp.Value[lodIdx] is not StructPropertyData lodStruct) continue;

            bool[] flags = lodIdx < result.PerLodFlags.Count
                ? result.PerLodFlags[lodIdx]
                : Array.Empty<bool>();

            if (flags.Length == 0 && Find(lodStruct, "DefaultHiddenMaterials") == null) continue;

            InsertOrReplaceDefaultHiddenMaterials(lodStruct, BuildBoolArray(asset, flags));
            modified++;
        }

        lodInfoProp.DummyStruct = lodInfoProp.Value.Length > 0
            ? lodInfoProp.Value[0] as StructPropertyData
            : lodInfoProp.DummyStruct;
        result.Diagnostics.Add($"Injected DefaultHiddenMaterials into {modified} LOD entry/entries");
        return modified;
    }

    public static int InjectBitmapsIntoLodInfo(NormalExport meshExport, IReadOnlyList<ulong> bitmaps, UAsset asset)
    {
        if (meshExport?.Data == null || bitmaps == null || bitmaps.Count == 0) return 0;

        ArrayPropertyData lodInfoProp = FindLodInfo(meshExport);
        if (lodInfoProp?.Value == null || lodInfoProp.Value.Length == 0) return 0;

        int modified = 0;
        int materialCount = GetMaterialCount(meshExport);
        for (int i = 0; i < lodInfoProp.Value.Length; i++)
        {
            if (lodInfoProp.Value[i] is not StructPropertyData lodStruct) continue;

            ulong bitmap = bitmaps.Count == 1
                ? bitmaps[0]
                : i < bitmaps.Count
                    ? bitmaps[i]
                    : bitmaps[bitmaps.Count - 1];
            int boolCount = MaterialHiddenArrayLength(lodStruct, bitmap, materialCount);
            InsertOrReplaceDefaultHiddenMaterials(lodStruct, BuildBoolArray(asset, bitmap, boolCount));
            modified++;
        }

        lodInfoProp.DummyStruct = lodInfoProp.Value.Length > 0
            ? lodInfoProp.Value[0] as StructPropertyData
            : lodInfoProp.DummyStruct;
        return modified;
    }

    public static List<bool[]> ExtractFromCookedMesh(NormalExport meshExport)
    {
        var output = new List<bool[]>();
        if (meshExport?.Data == null) return output;

        ArrayPropertyData lodInfoProp = FindLodInfo(meshExport);
        if (lodInfoProp?.Value == null) return output;

        foreach (PropertyData lodEntry in lodInfoProp.Value)
        {
            if (lodEntry is not StructPropertyData lodStruct)
            {
                output.Add(Array.Empty<bool>());
                continue;
            }

            output.Add(ExtractBoolArray(lodStruct, "DefaultHiddenMaterials"));
        }

        return output;
    }

    private static bool LooksLikeCarrierClass(string className)
    {
        return className.Contains("MaterialTagAssetUserData", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("HiddenMaterialsAssetUserData", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("RivalsMeshData", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("RivalsLODHiddenMaterialsData", StringComparison.OrdinalIgnoreCase);
    }

    private static ArrayPropertyData FindLodInfo(NormalExport export)
    {
        foreach (PropertyData prop in export.Data)
        {
            if (prop is ArrayPropertyData arrayProperty &&
                string.Equals(arrayProperty.Name?.Value?.Value, "LODInfo", StringComparison.OrdinalIgnoreCase))
            {
                return arrayProperty;
            }
        }

        return null;
    }

    private static PropertyData Find(StructPropertyData property, string name)
    {
        if (property?.Value == null) return null;

        foreach (PropertyData child in property.Value)
        {
            if (string.Equals(child?.Name?.Value?.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static void InsertOrReplaceDefaultHiddenMaterials(StructPropertyData lodStruct, ArrayPropertyData newProp)
    {
        if (lodStruct == null || newProp == null) return;

        lodStruct.Value ??= new List<PropertyData>();

        int existingIdx = -1;
        int afterLodMaterialMapIdx = -1;
        for (int i = 0; i < lodStruct.Value.Count; i++)
        {
            string name = lodStruct.Value[i]?.Name?.Value?.Value;
            if (name == null) continue;

            if (string.Equals(name, "DefaultHiddenMaterials", StringComparison.OrdinalIgnoreCase))
            {
                existingIdx = i;
            }
            else if (string.Equals(name, "LODMaterialMap", StringComparison.OrdinalIgnoreCase))
            {
                afterLodMaterialMapIdx = i + 1;
            }
        }

        if (existingIdx >= 0)
        {
            lodStruct.Value[existingIdx] = newProp;
        }
        else
        {
            int insertAt = afterLodMaterialMapIdx >= 0
                ? afterLodMaterialMapIdx
                : Math.Min(3, lodStruct.Value.Count);
            lodStruct.Value.Insert(insertAt, newProp);
        }

        lodStruct._originalStructHeader = null;
    }

    private static ArrayPropertyData BuildBoolArray(UAsset asset, bool[] flags)
    {
        var array = new ArrayPropertyData(Name(asset, "DefaultHiddenMaterials"))
        {
            ArrayType = Name(asset, "BoolProperty"),
            Value = new PropertyData[flags.Length]
        };

        for (int i = 0; i < flags.Length; i++)
        {
            array.Value[i] = new BoolPropertyData(FName.DefineDummy(asset, i.ToString(), int.MinValue))
            {
                Value = flags[i]
            };
        }

        return array;
    }

    private static ArrayPropertyData BuildBoolArray(UAsset asset, ulong bitmap, int count)
    {
        var flags = new bool[count];
        for (int i = 0; i < count; i++)
        {
            flags[i] = i < 64 && (bitmap & (1UL << i)) != 0;
        }

        return BuildBoolArray(asset, flags);
    }

    private static bool[] ExtractBoolArray(StructPropertyData container, string fieldName)
    {
        if (container?.Value == null) return Array.Empty<bool>();

        foreach (PropertyData field in container.Value)
        {
            if (string.Equals(field?.Name?.Value?.Value, fieldName, StringComparison.OrdinalIgnoreCase) &&
                field is ArrayPropertyData arrayProperty &&
                arrayProperty.Value != null)
            {
                var output = new bool[arrayProperty.Value.Length];
                for (int i = 0; i < arrayProperty.Value.Length; i++)
                {
                    if (arrayProperty.Value[i] is BoolPropertyData boolProperty)
                    {
                        output[i] = boolProperty.Value;
                    }
                    else if (arrayProperty.Value[i] is BytePropertyData byteProperty)
                    {
                        output[i] = byteProperty.Value != 0;
                    }
                }

                return output;
            }
        }

        return Array.Empty<bool>();
    }

    private static int GetMaterialCount(NormalExport export)
    {
        if (export is not SkeletalMeshExport skeletalMesh) return 0;

        try
        {
            skeletalMesh.EnsureExtraDataParsed();
            return skeletalMesh.GetMaterialCount();
        }
        catch
        {
            return 0;
        }
    }

    private static int MaterialHiddenArrayLength(StructPropertyData lodStruct, ulong bitmap, int materialCount)
    {
        if (Find(lodStruct, "DefaultHiddenMaterials") is ArrayPropertyData existing &&
            existing.Value != null &&
            existing.Value.Length > 0)
        {
            return existing.Value.Length;
        }

        if (materialCount > 0) return materialCount;
        return Math.Max(HighestSetBit(bitmap) + 1, 1);
    }

    private static int HighestSetBit(ulong value)
    {
        if (value == 0) return -1;

        int index = 0;
        while (value > 1)
        {
            value >>= 1;
            index++;
        }

        return index;
    }

    private static string TryGetExportClassName(Export export)
    {
        try
        {
            return export.GetExportClassType()?.Value?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static FName Name(UAsset asset, string value)
    {
        return new FName(asset, value);
    }
}
