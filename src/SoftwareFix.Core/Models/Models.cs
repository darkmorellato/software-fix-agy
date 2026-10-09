using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SoftwareFix.Core.Models;

public class ApiResponse<T>
{
    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("desc")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("content")]
    public T Content { get; set; }
}

public class DeviceRescueInfo
{
    public string ModelName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Imei { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string RoCarrier { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string BlurVersion { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
    public string BasebandVersion { get; set; } = string.Empty;
    public string Ram { get; set; } = string.Empty;
    public string DualSim { get; set; } = string.Empty;
    public string Brand { get; set; } = "Motorola";
    public string ChannelId { get; set; } = string.Empty;
    public string Cid { get; set; } = string.Empty;
    public string SecureState { get; set; } = string.Empty;
    public string ErasePersonalData { get; set; } = string.Empty;
    public string FdrAllowed { get; set; } = string.Empty;
    public int RescueMark { get; set; }
}

public class RomPackage
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("romName")]
    public string RomName { get; set; } = string.Empty;

    [JsonProperty("romVersion")]
    public string RomVersion { get; set; } = string.Empty;

    [JsonProperty("androidVersion")]
    public string AndroidVersion { get; set; } = string.Empty;

    [JsonProperty("url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonProperty("fileSize")]
    public long FileSize { get; set; }

    [JsonProperty("md5")]
    public string Md5 { get; set; } = string.Empty;

    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonProperty("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonProperty("recipeUrl")]
    public string RecipeUrl { get; set; } = string.Empty;
}

public class DeviceCategoryItem
{
    [JsonProperty("categoryName")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonProperty("categoryId")]
    public string CategoryId { get; set; } = string.Empty;
}

public class MarketModelItem
{
    [JsonProperty("marketName")]
    public string MarketName { get; set; } = string.Empty;

    [JsonProperty("modelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonProperty("hwCode")]
    public string HwCode { get; set; } = string.Empty;
}
