using SoftwareFix.Core;

namespace SoftwareFix.Core.Services;

/// <summary>
/// Espelho fiel de lenovo.mbg.service.common.webservices.WebApiUrl (descompilado
/// de lenovo.mbg.service.common.webservices.dll — Software Fix 7.6.2.10).
/// Contém TODOS os endpoints oficiais da API Lenovo utilizados pelo cliente.
/// Nomes mantidos idênticos ao original para rastreabilidade 1:1.
/// </summary>
public static class WebApiUrl
{
    private static string Iface => Configurations.ServiceInterfaceUrl;
    private static string Base => Configurations.BaseHttpUrl;

    // --- Autenticação / sessão -------------------------------------------
    public static string GET_PUBLIC_KEY => Iface + "/common/rsa.jhtml";
    public static string INIT_TOKEN => Iface + "/client/initToken.jhtml";
    public static string DISPOSE_TOKEN => Iface + "/client/deleteToken.jhtml";
    public static string LENOVOID_LOGIN_CALLBACK => Iface + "/user/getSFUserInfo.jhtml";
    public static string USER_LOGIN => Iface + "/user/login.jhtml";
    public static string USER_GUEST_LOGIN => Iface + "/user/guestLogin.jhtml";
    public static string USER_RECORD_LOGIN => Iface + "/user/recordLogin.jhtml";
    public static string USER_LOGOUT => Iface + "/user/logout.jhtml";
    public static string USER_FORGOT_PASSWORD => Iface + "/user/forgotPassword.jhtml";
    public static string USER_CHANGE_PASSWORD => Iface + "/user/changePassword.jhtml";
    public static string GET_TOKEN_NEAR_EXPIRED => Iface + "/user/getUserNearExpiration.jhtml";
    public static string PRIV_GET_PRIV_INFO => Iface + "/priv/getPrivInfo.jhtml";

    /// <summary>Definidos em runtime pelo fluxo OAuth Lenovo ID (ver LenovoIdWindow).</summary>
    public static string TOKEN_URL { get; set; } = string.Empty;
    public static string LOGIN_TOKEN { get; set; } = string.Empty;

    // --- Cliente / atualização -------------------------------------------
    public static string CLIENT_VERSION => Iface + "/client/getNextUpdateClient.jhtml";
    public static string UPDATE_VERSION => Iface + "/client/getPluginCategoryList.jhtml";
    public static string PLUGIN_VERSION => Iface + "/client/getClientPlugins.jhtml";
    public static string UPDATE_DOWNLOAD_URL => Iface + "/client/renewFileLink.jhtml";
    public static string USER_GUIDE => Iface + "/client/getUserGuide.jhtml";
    public static string HELP_URI => Iface + "/client/clientHelp.jhtml";
    public static string LANG_PACKAGE_INFO => Iface + "/client/languagePack.jhtml";
    public static string LOAD_WARRANTY_BANNER => Iface + "/client/motoCare.jhtml";
    public static string LOAD_COUPON => Iface + "/client/discountCoupon.jhtml";
    public static string CHECK_MA_VERSION => Iface + "/apk/download.jhtml";

    // --- Dispositivo / resgate (Rescue) ----------------------------------
    public static string GET_DEVICE_INFO => Iface + "/device/getDeviceInfo.jhtml";
    public static string GET_DEVICE_ICON => Iface + "/device/getDeviceIcon.jhtml";
    public static string GET_UPGRADEFLASH_MATCH_TYPES => Iface + "/rescueDevice/getParamType.jhtml";
    public static string RESUCE_AUTOMATCH_GETPARAMS_MAPPING => Iface + "/rescueDevice/getRomMatchParams.jhtml";
    public static string RESUCE_AUTOMATCH_GETROM => Iface + "/rescueDevice/getNewResource.jhtml";
    public static string RESUCE_CHECK_SUPPORT_FASTBOOT_MODE => Iface + "/rescueDevice/getMarketSupport.jhtml";
    public static string LOAD_SMART_DEVICE => Iface + "/rescueDevice/smartMarketNames.jhtml";
    public static string MODEL_READ_CONFIG => Iface + "/rescueDevice/modelReadConfigration.jhtml";
    public static string GET_FASTBOOTDATA_RECIPE => Iface + "/rescueDevice/getRescueModelRecipe.jhtml";
    public static string RESUCE_CHECK_MODEL_NAME_DRIVERS => Iface + "/model/getDriverSpecialConfig.jhtml";
    public static string LOAD_YOUTUBE_INFO => Iface + "/model/getYoutubeVideo.jhtml";
    public static string ROMFILE_CHECK_RULES => Iface + "/model/rules.jhtml";
    public static string GET_SUPPORT_FASTBOOT_BY_MODELNAME => Iface + "/model/isReadSupport.jhtml";
    public static string Webwervice_Get_RomResources => Iface + "/priv/getRomList.jhtml";

    // --- Molibdenum / Moli ------------------------------------------------
    public static string GET_MOLI_REQUEST_URL => Iface + "/moli/getMoliUrl.jhtml";
    public static string MOLI_INFO => Iface + "/moli/moliAndLena.jhtml";

    // --- Dicionário / B2B --------------------------------------------------
    public static string CALL_API_URL => Iface + "/dictionary/getApiInfo.jhtml";
    public static string CALL_B2B_ORDERS_URL => Iface + "/vip/getB2BInfo.jhtml";
    public static string CALL_B2B_ACTIVE_ORDERS_URL => Iface + "/vip/getActiveB2BInfos.jhtml";
    public static string CALL_B2B_QUERY_ORDER_URL => Iface + "/vip/getEnableB2BOrder.jhtml";
    public static string CALL_B2B_GET_ORDERID_URL => Iface + "/vip/getOrderNum.jhtml";
    public static string CALL_B2B_ORDER_BUY_URL => Iface + "/vip/buy.jhtml";
    public static string CALL_B2B_GET_PRICE_URL => Iface + "/vip/card.jhtml";

    // --- Notícias / survey / feedback ------------------------------------
    public static string NOTICE_URL => Iface + "/notice/getNoticeInfo.jhtml";
    public static string NOTICE_BROADCAST_URL => Iface + "/notice/getBroadcast.jhtml";
    public static string SURVEY_URL => Iface + "/survey/getIsNeedTrigger.jhtml";
    public static string GET_IS_NEED_TRIGGER_SURVER => Iface + "/survey/getIsNeedTriggerSurvey.jhtml";
    public static string SURVEY_REFRESH => Iface + "/survey/refreshTrigger.jhtml";
    public static string SURVEY_GET_QUESTIONS => Iface + "/survey/getAllQuestions.jhtml";
    public static string SURVEY_RECORD => Iface + "/survey/record.jhtml";
    public static string FEEDBACK_GET_LIST => Iface + "/feedback/getFeedbackList.jhtml";
    public static string FEEDBACK_GET_INFO => Iface + "/feedback/getFeedbackInfo.jhtml";
    public static string FEEDBACK_FILE_SINGNATURE => Iface + "/feedback/fileSignatureUrl.jhtml";
    public static string FEEDBACK_GET_HELPFUL => Iface + "/feedback/replyHelpful.jhtml";
    public static string FEEDBACK_GET_UPLOAD => Iface + "/feedback/postFeedbackInfo.jhtml";
    public static string FEEDBACK_GET_UPLOAD_GUEST => Iface + "/feedback/guestPostFeedbackInfo.jhtml";
    public static string FEEDBACK_GET_ISSUE_INFO => Iface + "/feedback/getFeedbackIssueInfo.jhtml";
    public static string GET_MUTIL_TUTORIALS_QUESTIONS => Iface + "/guide/getGuideQuestion.jhtml";
    public static string SHOW_FEEDBACK => Base + "/Tips/feedback.html";
    public static string NETWORK_CONNECT_CHECK => "https://lsa.lenovo.com/lmsa-web/index.jsp";
    public static string FORMAT_LENOVOID_ACCOUNT =>
        "https://passport.lenovo.com/interserver/authen/1.2/getaccountid?lpsust={0}&realm=lmsaclient";

    // --- Telemetria / data collection -------------------------------------
    public static string POST_UPGRADE_FLASH_INFO => Iface + "/dataCollection/UpgradeFlashInfo.jhtml";
    public static string UPLOAD_USER_DEVICE => Iface + "/registeredModel/addModels.jhtml";
    public static string DELETE_USER_DEVICE => Iface + "/registeredModel/models.jhtml";
    public static string FEEDBACK_BACKUP_RESTORE => Iface + "/dataCollection/addBackupRestore.jhtml";
    public static string UPLOAD_DOWNLOAD_SPEEDINFO => Iface + "/dataCollection/romDownloadInfo.jhtml";
    public static string RESUCE_FAILED_UPLOAD => Iface + "/dataCollection/uploadFile.jhtml";
    public static string FEEDBACK_NO_TRANSLATE => Iface + "/dataCollection/untranslatedSentences.jhtml";
    public static string USER_BEHAVIOR_COLLECTION => Iface + "/dataCollection/addUserBehavior.jhtml";
    public static string UPLOAD_RESCUE_TOOL_LOG => Iface + "/dataCollection/nativeToolLog.jhtml";
    public static string COLLECTION_RESCUE_SUCCESS_LOG_UPLOAD => Iface + "/dataCollection/rescueSuccessLog.jhtml";
    public static string COLLECTION_ASSISTANTAPP => Iface + "/dataCollection/assistantApp.jhtml";

    public static List<string> GET_INFO_LOG_LIST { get; } = new() { FEEDBACK_BACKUP_RESTORE };
}
