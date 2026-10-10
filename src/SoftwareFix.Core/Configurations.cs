using System;
using System.IO;

namespace SoftwareFix.Core;

public static class Configurations
{
    /// <summary>FileVersion oficial do Software Fix.exe (AssemblyInfo.cs descompilado).</summary>
    public const string Version = "7.6.2.10";
    public const string BaseHttpUrl = "https://lsa.lenovo.com";
    public static string ServiceInterfaceUrl => BaseHttpUrl + "/Interface";

    // Official Web Service Endpoints (from WebApiUrl.cs and WebServicesContext.cs)
    public static string UrlPublicKey => ServiceInterfaceUrl + "/common/rsa.jhtml";
    public static string UrlInitToken => ServiceInterfaceUrl + "/client/initToken.jhtml";
    public static string UrlDeviceInfo => ServiceInterfaceUrl + "/device/getDeviceInfo.jhtml";
    public static string UrlRomMatchParams => ServiceInterfaceUrl + "/rescueDevice/getRomMatchParams.jhtml";
    public static string UrlGetRom => ServiceInterfaceUrl + "/rescueDevice/getNewResource.jhtml";
    public static string UrlGetRomByImei => ServiceInterfaceUrl + "/rescueDevice/getNewResourceByImei.jhtml";
    public static string UrlGetRomBySn => ServiceInterfaceUrl + "/rescueDevice/getNewResourceBySN.jhtml";
    public static string UrlGetModelNames => ServiceInterfaceUrl + "/rescueDevice/getModelNames.jhtml";
    public static string UrlGetRescueModelRecipe => ServiceInterfaceUrl + "/rescueDevice/getRescueModelRecipe.jhtml";
    public static string UrlSmartMarketNames => ServiceInterfaceUrl + "/rescueDevice/smartMarketNames.jhtml";
    public static string UrlCallApi => ServiceInterfaceUrl + "/dictionary/getApiInfo.jhtml";
    public static string UrlRescueFlashInfo => ServiceInterfaceUrl + "/dataCollection/RescueflashInfo.jhtml";
    public static string UrlRescueDegradeInfo => ServiceInterfaceUrl + "/dataCollection/rescueDegradeInfo.jhtml";
    public static string UrlUploadLog => ServiceInterfaceUrl + "/dataCollection/nativeToolLog.jhtml";
    public static string UrlCheckMaVersion => ServiceInterfaceUrl + "/apk/download.jhtml";

    // --- Login / sessão (fluxo Lenovo ID OAuth — LenovoIdWindow/LenovoIdUserLogin) ---
    public static string UrlLoginCallback => ServiceInterfaceUrl + "/user/getSFUserInfo.jhtml";
    public static string UrlGuestLogin => ServiceInterfaceUrl + "/user/guestLogin.jhtml";
    public static string UrlUserRecordLogin => ServiceInterfaceUrl + "/user/recordLogin.jhtml";
    public static string UrlUserLogout => ServiceInterfaceUrl + "/user/logout.jhtml";
    public static string UrlDisposeToken => ServiceInterfaceUrl + "/client/deleteToken.jhtml";

    // --- Endpoints adicionais do plugin Rescue (WebServicesContext.cs descompilado) ---
    public static string UrlGetRescueModelNames => ServiceInterfaceUrl + "/rescueDevice/getRescueModelNames.jhtml";
    public static string UrlModelListByMarketName => ServiceInterfaceUrl + "/rescueDevice/modelListByMarketName.jhtml";
    public static string UrlGetResourceManual => ServiceInterfaceUrl + "/rescueDevice/getResource.jhtml";
    public static string UrlGetXamlList => ServiceInterfaceUrl + "/rescueDevice/getXamlList.jhtml";
    public static string UrlHwDetection => ServiceInterfaceUrl + "/dataCollection/HwDetectionInfo.jhtml";
    public static string UrlRescueSocialShare => ServiceInterfaceUrl + "/dataCollection/rescueSocialShare.jhtml";
    public static string UrlSurveyRescueSuccess => ServiceInterfaceUrl + "/survey/saveRescueSuccessRecord.jhtml";

    public static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string BaseDataPath
    {
        get
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return Path.Combine(UserHome, ".local", "share", "software-fix");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Software Fix");
        }
    }

    public static string ConfigPath
    {
        get
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return Path.Combine(UserHome, ".config", "software-fix");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Software Fix");
        }
    }

    public static string DownloadPath => Path.Combine(BaseDataPath, "download");
    public static string RomPath => Path.Combine(DownloadPath, "RomFiles");
    public static string ToolPath => Path.Combine(DownloadPath, "ToolFiles");
    public static string LogPath => Path.Combine(BaseDataPath, "logs");
    public static string TempPath => Path.Combine(BaseDataPath, "temp");
    public static string SessionFile => Path.Combine(ConfigPath, "session.json");
    public static string ClientUuidFile => Path.Combine(ConfigPath, "client_uuid");

    // --- MediaTek / MTK (mtkclient) -----------------------------------------
    // Fluxo validado no moto g17 (XT2623-1, MT6768 k68v1): PreLoader (0e8d:2000)
    // + DA assinado do vendor como --loader. Ver docs/MTK_RESCUE.md.
    public static string MtkDaDir => Path.Combine(BaseDataPath, "da");
    public const string MtkDaDefaultFileName = "DA_A15_lamuc_FORBID_SIGNED.bin";
    public static string MtkDaDefaultFile => Path.Combine(MtkDaDir, MtkDaDefaultFileName);
    public static string MtkLogDir => Path.Combine(LogPath, "mtk");
    public static string DumpDir => Path.Combine(BaseDataPath, "Dump");

    static Configurations()
    {
        Directory.CreateDirectory(BaseDataPath);
        Directory.CreateDirectory(ConfigPath);
        Directory.CreateDirectory(DownloadPath);
        Directory.CreateDirectory(RomPath);
        Directory.CreateDirectory(ToolPath);
        Directory.CreateDirectory(LogPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(MtkDaDir);
        Directory.CreateDirectory(MtkLogDir);
        Directory.CreateDirectory(DumpDir);
    }
}
