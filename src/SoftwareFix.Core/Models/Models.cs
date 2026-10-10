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

// Official Resource Models from lenovo.mbg.service.lmsa.flash.UserModelV2
public class ResourceModel
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("uri")]
    public string URI { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("unZip")]
    public bool UnZip { get; set; }

    [JsonProperty("md5")]
    public string MD5 { get; set; } = string.Empty;

    [JsonProperty("clientBehavior")]
    public string ClientBehavior { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    public int Id { get; set; }
    public bool Latest { get; set; }
}

public class ResourceResponseModel
{
    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("brand")]
    public string Brand { get; set; } = string.Empty;

    [JsonProperty("hwcode")]
    public string HWCode { get; set; } = string.Empty;

    [JsonProperty("modelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonProperty("realModelName")]
    public string RealModelName { get; set; } = string.Empty;

    [JsonProperty("saleModel")]
    public string SalesModel { get; set; } = string.Empty;

    [JsonProperty("flashFlow")]
    public string RecipeResource { get; set; } = string.Empty;

    [JsonProperty("iconResource")]
    public ResourceModel? IconResource { get; set; }

    [JsonProperty("toolResource")]
    public ResourceModel? ToolResource { get; set; }

    [JsonProperty("romResource")]
    public ResourceModel? RomResources { get; set; }

    [JsonProperty("countryCodeResource")]
    public ResourceModel? CountryCode { get; set; }

    [JsonProperty("backUpPopup")]
    public bool IsShowBackupTip { get; set; }

    [JsonProperty("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonProperty("marketName")]
    public string MarketName { get; set; } = string.Empty;

    [JsonProperty("latest")]
    public bool Latest { get; set; }

    [JsonProperty("romMatchId")]
    public string RomMatchId { get; set; } = string.Empty;

    [JsonProperty("fastboot")]
    public bool Fastboot { get; set; }

    [JsonIgnore]
    public string RomName => RomResources?.Name ?? "";

    [JsonIgnore]
    public string DownloadUrl => RomResources?.URI ?? "";

    [JsonIgnore]
    public string RomVersion => !string.IsNullOrEmpty(RealModelName) ? RealModelName : ModelName;

    [JsonIgnore]
    public string AndroidVersion => "";

    [JsonIgnore]
    public long FileSize => 4613734400;
}

// Official Warranty Info Model from lenovo.mbg.service.common.webservices.WebApiServices.WarrantyService
public class WarrantyInfoModel
{
    [JsonProperty("Start")]
    public string StartMonth { get; set; } = string.Empty;

    [JsonProperty("End")]
    public string EndMonth { get; set; } = string.Empty;

    [JsonProperty("MonthDiff")]
    public int MonthDiff { get; set; }

    [JsonProperty("ExpriedDays")]
    public int ExpiredDays { get; set; }

    [JsonProperty("InWarranty")]
    public bool InWarranty { get; set; }

    [JsonProperty("ShipLocation")]
    public string ShipLocation { get; set; } = string.Empty;

    [JsonProperty("Brand")]
    public string Brand { get; set; } = "Motorola";

    [JsonProperty("WarrantyStartDate")]
    public string WarrantyStartDate { get; set; } = string.Empty;

    [JsonProperty("WarrantyEndDate")]
    public string WarrantyEndDate { get; set; } = string.Empty;

    [JsonProperty("msn")]
    public string Msn { get; set; } = string.Empty;
}

// Model list returned by getModelNames.jhtml
public class DeviceModelInfoModel
{
    [JsonProperty("modelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonProperty("marketName")]
    public string MarketName { get; set; } = string.Empty;

    [JsonProperty("hwCode")]
    public string HwCode { get; set; } = string.Empty;
}

public class DeviceModelInfoListModel
{
    [JsonProperty("modelList")]
    public List<DeviceModelInfoModel> ModelList { get; set; } = new();

    [JsonProperty("moreModelList")]
    public List<DeviceModelInfoModel> MoreModelList { get; set; } = new();
}

// Official Request Wrapper matching RequestModel.cs
public class RequestEnvelope
{
    [JsonProperty("language")]
    public string Language { get; set; } = "pt-BR";

    [JsonProperty("windowsInfo")]
    public string WindowsInfo { get; set; } = "Linux x86_64";

    [JsonProperty("client")]
    public Dictionary<string, string> Client { get; set; } = new()
    {
        { "version", Configurations.Version }
    };

    [JsonProperty("dparams")]
    public object? DParams { get; set; }

    public RequestEnvelope(object? dparams = null)
    {
        DParams = dparams;
    }
}

public class DiagnosticItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Não testado";
    public string StatusColor { get; set; } = "#8FA7C2";
    public bool IsPassed { get; set; }
}
